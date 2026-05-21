using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using CutPig.GameEngine;
using CutPig.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Hubs;

public class RoomHub : Hub
{
    private readonly AppDbContext _db;
    private readonly RoomPresenceTracker _presence;
    private readonly MatchManager _matches;

    public RoomHub(AppDbContext db, RoomPresenceTracker presence, MatchManager matches)
    {
        _db = db;
        _presence = presence;
        _matches = matches;
    }

    private static string GroupName(Guid roomId) => $"room:{roomId}";

    private async Task<(Guid UserId, AppUser User)?> AuthenticateAsync()
    {
        var http = Context.GetHttpContext();
        var token = http?.Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) return null;
        var record = await _db.AuthTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.Token == token);
        if (record == null || record.ExpiresAt < DateTime.UtcNow || record.User == null) return null;
        return (record.UserId, record.User);
    }

    public async Task<RoomStateDto?> JoinRoom(string code)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");

        var room = await _db.Rooms
            .Include(r => r.Seats).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant());
        if (room == null) throw new HubException("Phòng không tồn tại.");

        _presence.Add(Context.ConnectionId, auth.Value.UserId, room.Id);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room.Id));

        var state = BuildState(room);
        await Clients.OthersInGroup(GroupName(room.Id)).SendAsync("RoomState", state);

        // If a match is in progress for this room, push match state + private hand
        var match = _matches.GetByRoom(room.Id);
        if (match != null && match.Status == GameEngine.MatchStatus.InProgress)
        {
            await Clients.Caller.SendAsync("MatchState", BuildMatchPublic(match));
            var player = match.Players.FirstOrDefault(p => p.UserId == auth.Value.UserId);
            if (player != null)
            {
                await Clients.Caller.SendAsync("PrivateHand", new PrivateHandDto(
                    match.Id,
                    player.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList()));
            }
        }
        return state;
    }

    public async Task TakeSeat(int seatIndex)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");

        var roomId = _presence.Remove(Context.ConnectionId)?.RoomId;
        if (roomId.HasValue) _presence.Add(Context.ConnectionId, auth.Value.UserId, roomId.Value);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var room = await _db.Rooms
            .Include(r => r.Seats).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(r => r.Id == roomId);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (room.Status != RoomStatus.Waiting) throw new HubException("Phòng đã bắt đầu.");
        if (seatIndex < 0 || seatIndex >= room.MaxSeats) throw new HubException("Vị trí ghế không hợp lệ.");

        var existingForUser = room.Seats.FirstOrDefault(s => s.UserId == auth.Value.UserId);
        var existingAtSeat = room.Seats.FirstOrDefault(s => s.SeatIndex == seatIndex);
        if (existingAtSeat != null && existingAtSeat.UserId != auth.Value.UserId)
            throw new HubException("Ghế đã có người ngồi.");

        if (existingForUser != null)
        {
            existingForUser.SeatIndex = seatIndex;
        }
        else
        {
            room.Seats.Add(new RoomSeat
            {
                RoomId = room.Id,
                SeatIndex = seatIndex,
                UserId = auth.Value.UserId,
                User = auth.Value.User
            });
        }
        await _db.SaveChangesAsync();

        var fresh = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstAsync(r => r.Id == room.Id);
        await Clients.Group(GroupName(room.Id)).SendAsync("RoomState", BuildState(fresh));
    }

    public async Task LeaveSeat()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var presence = _presence.Remove(Context.ConnectionId);
        if (presence != null) _presence.Add(Context.ConnectionId, auth.Value.UserId, presence.Value.RoomId);
        if (presence == null) return;

        var roomId = presence.Value.RoomId;
        var room = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstOrDefaultAsync(r => r.Id == roomId);
        if (room == null) return;
        if (room.Status != RoomStatus.Waiting) throw new HubException("Phòng đã bắt đầu.");

        var seat = room.Seats.FirstOrDefault(s => s.UserId == auth.Value.UserId);
        if (seat != null)
        {
            room.Seats.Remove(seat);
            _db.RoomSeats.Remove(seat);
            await _db.SaveChangesAsync();
        }
        var fresh = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstAsync(r => r.Id == roomId);
        await Clients.Group(GroupName(roomId)).SendAsync("RoomState", BuildState(fresh));
    }

    public async Task StartGame()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var presence = _presence.Remove(Context.ConnectionId);
        if (presence != null) _presence.Add(Context.ConnectionId, auth.Value.UserId, presence.Value.RoomId);
        if (presence == null) throw new HubException("Chưa vào phòng nào.");

        var room = await _db.Rooms.Include(r => r.Seats).FirstOrDefaultAsync(r => r.Id == presence.Value.RoomId);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (room.HostUserId != auth.Value.UserId) throw new HubException("Chỉ chủ phòng được bắt đầu.");
        if (room.Status != RoomStatus.Waiting) throw new HubException("Phòng đã bắt đầu.");
        if (room.Seats.Count < 2) throw new HubException("Cần ít nhất 2 người chơi.");

        room.Status = RoomStatus.Playing;
        room.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var fresh = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstAsync(r => r.Id == room.Id);
        await Clients.Group(GroupName(room.Id)).SendAsync("RoomState", BuildState(fresh));

        // Create match + deal
        var matchPlayers = fresh.Seats
            .OrderBy(s => s.SeatIndex)
            .Select(s => (
                s.UserId,
                DisplayName: string.IsNullOrWhiteSpace(s.User?.DisplayName) ? (s.User?.Username ?? "") : s.User!.DisplayName,
                s.SeatIndex))
            .ToList();
        var match = _matches.Create(room.Id, matchPlayers);

        await Clients.Group(GroupName(room.Id)).SendAsync("GameStarted", room.Id);
        await Clients.Group(GroupName(room.Id)).SendAsync("MatchState", BuildMatchPublic(match));
        await SendPrivateHandsAsync(match);
    }

    public async Task<MatchPublicStateDto?> RequestMatchState()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var presence = _presence.Remove(Context.ConnectionId);
        if (presence != null) _presence.Add(Context.ConnectionId, auth.Value.UserId, presence.Value.RoomId);
        if (presence == null) return null;

        var match = _matches.GetByRoom(presence.Value.RoomId);
        if (match == null) return null;

        // Resend private hand to the requesting connection too
        var player = match.Players.FirstOrDefault(p => p.UserId == auth.Value.UserId);
        if (player != null)
        {
            await Clients.Caller.SendAsync("PrivateHand", new PrivateHandDto(
                match.Id,
                player.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList()));
        }
        return BuildMatchPublic(match);
    }

    public async Task PlayCards(List<CardDto> cards)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var presence = _presence.Remove(Context.ConnectionId);
        if (presence != null) _presence.Add(Context.ConnectionId, auth.Value.UserId, presence.Value.RoomId);
        if (presence == null) throw new HubException("Chưa vào phòng nào.");

        var parsed = cards.Select(c => new Card(c.Rank, (Suit)c.Suit)).ToList();
        PlayResult result;
        try
        {
            result = _matches.Play(presence.Value.RoomId, auth.Value.UserId, parsed);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(presence.Value.RoomId)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        // Resend private hand to the player who just played
        var player = result.Match.Players.First(p => p.UserId == auth.Value.UserId);
        await SendPrivateHandToUserAsync(presence.Value.RoomId, player);

        if (result.MatchEnded)
        {
            await FinalizeMatchAsync(result.Match);
        }
    }

    public async Task PassTurn()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var presence = _presence.Remove(Context.ConnectionId);
        if (presence != null) _presence.Add(Context.ConnectionId, auth.Value.UserId, presence.Value.RoomId);
        if (presence == null) throw new HubException("Chưa vào phòng nào.");

        PassResult result;
        try
        {
            result = _matches.Pass(presence.Value.RoomId, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(presence.Value.RoomId)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        if (result.MatchEnded)
        {
            await FinalizeMatchAsync(result.Match);
        }
    }

    private async Task SendPrivateHandsAsync(Match match)
    {
        foreach (var player in match.Players)
        {
            await SendPrivateHandToUserAsync(match.RoomId, player);
        }
    }

    private async Task SendPrivateHandToUserAsync(Guid roomId, MatchPlayer player)
    {
        var conns = _presence.ConnectionsFor(roomId, player.UserId);
        if (conns.Count == 0) return;
        var dto = new PrivateHandDto(
            roomId,
            player.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList());
        await Clients.Clients(conns).SendAsync("PrivateHand", dto);
    }

    private async Task FinalizeMatchAsync(Match match)
    {
        // Compute scores using basic rank-only mode of TienLenScoringService.
        // Rank points: #1=+2, #2=+1, #3=-1, #4=-2 for 4 players.
        // For 2-3 players we use a proportional simple table.
        var scores = ComputeBasicRankScores(match.Players.Count, match);
        var results = match.Players
            .OrderBy(p => p.FinalRank ?? int.MaxValue)
            .Select(p => new MatchEndResultDto(p.UserId, p.DisplayName, p.FinalRank ?? 0, scores[p.UserId]))
            .ToList();
        await Clients.Group(GroupName(match.RoomId)).SendAsync("MatchEnd", new MatchEndDto(match.Id, results));

        // Mark room finished
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == match.RoomId);
        if (room != null)
        {
            room.Status = RoomStatus.Finished;
            room.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        _matches.Remove(match.RoomId);
    }

    private static Dictionary<Guid, int> ComputeBasicRankScores(int playerCount, Match match)
    {
        // 4-player: +2/+1/-1/-2
        // 3-player: +2/0/-2
        // 2-player: +1/-1
        int[] table = playerCount switch
        {
            4 => new[] { 2, 1, -1, -2 },
            3 => new[] { 2, 0, -2 },
            2 => new[] { 1, -1 },
            _ => Enumerable.Range(0, playerCount).Select(_ => 0).ToArray()
        };
        var dict = new Dictionary<Guid, int>();
        foreach (var p in match.Players)
        {
            var rank = (p.FinalRank ?? playerCount) - 1;
            dict[p.UserId] = table[Math.Clamp(rank, 0, table.Length - 1)];
        }
        return dict;
    }

    private static MatchPublicStateDto BuildMatchPublic(Match m)
    {
        return new MatchPublicStateDto(
            m.Id,
            m.RoomId,
            (int)m.Status,
            m.CurrentTurnSeatIndex,
            m.CurrentTrickOwnerId,
            m.CurrentTrick?.Cards.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.TurnDeadline,
            m.Players.Select(p => new MatchPlayerDto(
                p.UserId,
                p.DisplayName,
                p.SeatIndex,
                p.Hand.Count,
                p.FinalRank)).ToList());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var info = _presence.Remove(Context.ConnectionId);
        if (info != null)
        {
            var room = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstOrDefaultAsync(r => r.Id == info.Value.RoomId);
            if (room != null)
            {
                await Clients.Group(GroupName(info.Value.RoomId)).SendAsync("RoomState", BuildState(room));
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private RoomStateDto BuildState(Room room)
    {
        var seats = room.Seats
            .OrderBy(s => s.SeatIndex)
            .Select(s => new RoomSeatDto(
                s.SeatIndex,
                s.UserId,
                s.User?.Username ?? "",
                string.IsNullOrWhiteSpace(s.User?.DisplayName) ? (s.User?.Username ?? "") : s.User!.DisplayName,
                s.UserId == room.HostUserId,
                _presence.IsOnline(room.Id, s.UserId)))
            .ToList();

        return new RoomStateDto(
            room.Id,
            room.Code,
            room.GameType,
            room.MaxSeats,
            (int)room.Status,
            room.HostUserId,
            room.CreatedAt,
            room.StartedAt,
            seats);
    }
}
