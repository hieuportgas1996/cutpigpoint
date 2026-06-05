using System.Text.Json;
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
    private readonly ChatStore _chat;
    private readonly ILogger<RoomHub> _logger;

    public RoomHub(AppDbContext db, RoomPresenceTracker presence, MatchManager matches, ChatStore chat, ILogger<RoomHub> logger)
    {
        _db = db;
        _presence = presence;
        _matches = matches;
        _chat = chat;
        _logger = logger;
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

        // Push recent chat history to caller
        var history = _chat.Recent(room.Id, 50)
            .Select(m => new ChatMessageDto(m.Id, m.UserId, m.DisplayName, m.Text, m.CreatedAt))
            .ToList();
        await Clients.Caller.SendAsync("ChatHistory", new ChatHistoryDto(history));

        // If a match exists for this room (in any active phase), push match state + private hand
        var match = _matches.GetByRoom(room.Id);
        if (match != null && match.Status != GameEngine.MatchStatus.Finished)
        {
            await Clients.Caller.SendAsync("MatchState", BuildMatchPublic(match));
            await Clients.Caller.SendAsync("RoundHistory", new RoundHistoryDto(match.Id, match.RoundHistory.ToList()));
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
        try
        {
            var auth = await AuthenticateAsync();
            if (auth == null) throw new HubException("Unauthorized");

            var roomId = _presence.CurrentRoom(Context.ConnectionId);
            if (roomId == null) throw new HubException("Chưa vào phòng nào.");

            var room = await _db.Rooms
                .Include(r => r.Seats)
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
                _db.RoomSeats.Add(new RoomSeat
                {
                    RoomId = room.Id,
                    SeatIndex = seatIndex,
                    UserId = auth.Value.UserId
                });
            }
            await _db.SaveChangesAsync();

            var fresh = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstAsync(r => r.Id == room.Id);
            await Clients.Group(GroupName(room.Id)).SendAsync("RoomState", BuildState(fresh));
        }
        catch (HubException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TakeSeat failed seatIndex={Seat}", seatIndex);
            throw new HubException($"Lỗi khi ngồi vào ghế: {ex.Message}");
        }
    }

    public async Task LeaveSeat()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomIdNullable = _presence.CurrentRoom(Context.ConnectionId);
        if (roomIdNullable == null) return;
        var roomId = roomIdNullable.Value;

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

    public async Task SetShowOpponentCardCount(bool show)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (room.HostUserId != auth.Value.UserId) throw new HubException("Chỉ chủ phòng được đổi cài đặt.");
        if (room.Status != RoomStatus.Waiting) throw new HubException("Chỉ đổi được khi chưa bắt đầu chơi.");

        room.ShowOpponentCardCount = show;
        await _db.SaveChangesAsync();

        var fresh = await _db.Rooms.Include(r => r.Seats).ThenInclude(s => s.User).FirstAsync(r => r.Id == roomId);
        await Clients.Group(GroupName(roomId.Value)).SendAsync("RoomState", BuildState(fresh));
    }

    public async Task StartGame()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var room = await _db.Rooms.Include(r => r.Seats).FirstOrDefaultAsync(r => r.Id == roomId);
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
                s.SeatIndex,
                HasAvatar: !string.IsNullOrEmpty(s.User?.AvatarData)))
            .ToList();
        var match = _matches.Create(room.Id, fresh.HostUserId, matchPlayers, fresh.ShowOpponentCardCount);

        await Clients.Group(GroupName(room.Id)).SendAsync("GameStarted", room.Id);
        await Clients.Group(GroupName(room.Id)).SendAsync("MatchState", BuildMatchPublic(match));
        await SendPrivateHandsAsync(match);

        // If white-win was detected immediately, emit RoundEnd
        if (match.Status == MatchStatus.WaitingNextRound)
        {
            await EmitRoundEndAsync(match);
        }
    }

    public async Task StartNextRound()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.StartNextRound(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        await SendPrivateHandsAsync(match);

        if (match.Status == MatchStatus.WaitingNextRound)
        {
            await EmitRoundEndAsync(match);
        }
    }

    public async Task RespondWhiteWin(bool accept)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = accept
                ? _matches.AcceptWhiteWin(roomId.Value, auth.Value.UserId)
                : _matches.DeclineWhiteWin(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        if (match.Status == MatchStatus.WaitingNextRound)
        {
            await EmitRoundEndAsync(match);
        }
    }

    public async Task CutNewTrick(List<CardDto> cards)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var parsed = cards.Select(c => new Card(c.Rank, (Suit)c.Suit)).ToList();
        PlayResult result;
        try
        {
            result = _matches.CutNewTrick(roomId.Value, auth.Value.UserId, parsed);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        var player = result.Match.Players.First(p => p.UserId == auth.Value.UserId);
        await SendPrivateHandToUserAsync(roomId.Value, player);
        if (result.RoundEnded)
        {
            await EmitRoundEndAsync(result.Match);
        }
    }

    public async Task SendChat(string text)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        if (string.IsNullOrWhiteSpace(text)) return;
        var trimmed = text.Trim();
        if (trimmed.Length > 300) trimmed = trimmed[..300];

        var displayName = string.IsNullOrWhiteSpace(auth.Value.User.DisplayName)
            ? auth.Value.User.Username
            : auth.Value.User.DisplayName;
        var msg = _chat.Append(roomId.Value, auth.Value.UserId, displayName, trimmed);
        var dto = new ChatMessageDto(msg.Id, msg.UserId, msg.DisplayName, msg.Text, msg.CreatedAt);
        await Clients.Group(GroupName(roomId.Value)).SendAsync("ChatMessage", dto);
    }

    public async Task DeclineTrickCut()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.DeclineTrickCut(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    /// <summary>Spinner creates a wheel pool (preview) — visible to every viewer before the actual spin.</summary>
    public async Task CreateLuckyWheelPreview(string code, int min, int max, bool doubled)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == Domain.RoomStatus.Finished);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (!room.Seats.Any(s => s.UserId == auth.Value.UserId)) throw new HubException("Không có quyền.");
        if (!string.IsNullOrEmpty(room.LuckyWheelJson)) throw new HubException("Vòng quay đã có kết quả rồi.");

        var (spinner, _) = await GetSpinnerAndDonors(room);
        if (spinner.UserId != auth.Value.UserId) throw new HubException("Chỉ người hạng bét được tạo vòng quay.");
        if (!AreAllDonorsDecided(room)) throw new HubException("Chờ Nhất/Nhì quyết định sponsor xong đã.");

        if (min < 1) throw new HubException("Min phải ≥ 1.");
        if (max < min) throw new HubException("Max phải ≥ Min.");
        if (max > 1000) throw new HubException("Max quá lớn (≤ 1000).");

        var pool = BuildShuffledPool(min, max, doubled);
        var preview = new LuckyWheelPreviewDto(pool, min, max, doubled, auth.Value.UserId);
        room.LuckyWheelPreviewJson = JsonSerializer.Serialize(preview);
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room.Id));
        await Clients.Group(GroupName(room.Id)).SendAsync("WheelPreview", preview);
        await Clients.Group(GroupName(room.Id)).SendAsync("HistoryUpdated", Services.RoomHistoryBuilder.Build(room));
    }

    /// <summary>Spinner clears the current preview before spin (allow re-entering min/max).</summary>
    public async Task ResetLuckyWheelPreview(string code)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == Domain.RoomStatus.Finished);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (!room.Seats.Any(s => s.UserId == auth.Value.UserId)) throw new HubException("Không có quyền.");
        if (!string.IsNullOrEmpty(room.LuckyWheelJson)) throw new HubException("Vòng quay đã có kết quả rồi.");

        var (spinner, _) = await GetSpinnerAndDonors(room);
        if (spinner.UserId != auth.Value.UserId) throw new HubException("Chỉ người hạng bét được đổi vòng quay.");

        room.LuckyWheelPreviewJson = null;
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room.Id));
        await Clients.Group(GroupName(room.Id)).SendAsync("WheelPreviewCleared");
        await Clients.Group(GroupName(room.Id)).SendAsync("HistoryUpdated", Services.RoomHistoryBuilder.Build(room));
    }

    /// <summary>
    /// Lucky wheel spin: dùng preview pool đã tạo, server pick result index, broadcast WheelSpinStarted.
    /// </summary>
    public async Task StartLuckyWheelSpin(string code)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == Domain.RoomStatus.Finished);
        if (room == null) throw new HubException("Phòng không tồn tại.");
        if (!room.Seats.Any(s => s.UserId == auth.Value.UserId)) throw new HubException("Không có quyền.");
        if (!string.IsNullOrEmpty(room.LuckyWheelJson)) throw new HubException("Vòng quay đã có kết quả rồi.");
        if (string.IsNullOrEmpty(room.LuckyWheelPreviewJson)) throw new HubException("Chưa tạo vòng quay.");

        var (spinner, _) = await GetSpinnerAndDonors(room);
        if (spinner.UserId != auth.Value.UserId) throw new HubException("Chỉ người hạng bét được quay vòng.");

        LuckyWheelPreviewDto? preview = null;
        try { preview = JsonSerializer.Deserialize<LuckyWheelPreviewDto>(room.LuckyWheelPreviewJson); } catch { }
        if (preview == null) throw new HubException("Preview lỗi, vui lòng tạo lại.");

        var rng = new Random();
        int resultIndex = rng.Next(preview.Pool.Count);
        int result = preview.Pool[resultIndex];

        var wheel = new LuckyWheelDto(preview.Min, preview.Max, preview.Double, result, auth.Value.UserId);
        room.LuckyWheelJson = JsonSerializer.Serialize(wheel);
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room.Id));
        var dto = new WheelSpinStartedDto(preview.Pool, resultIndex, preview.Min, preview.Max, preview.Double, auth.Value.UserId);
        await Clients.Group(GroupName(room.Id)).SendAsync("WheelSpinStarted", dto);
        // Broadcast HistoryUpdated để mọi client thấy luckyWheel đã chốt → khi animation kết thúc,
        // section rơi vào nhánh `existing` và bảng tổng kết tiền tự hiện ra.
        await Clients.Group(GroupName(room.Id)).SendAsync("HistoryUpdated", Services.RoomHistoryBuilder.Build(room));
    }

    private static List<int> BuildShuffledPool(int min, int max, bool doubled)
    {
        var pool = new List<int>();
        for (int n = min; n <= max; n++) pool.Add(n);
        if (doubled) pool.AddRange(pool.ToList());
        var rng = new Random();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }

    private async Task<(RoomFinalScoreEntryDto Spinner, HashSet<Guid> AllowedDonors)> GetSpinnerAndDonors(Domain.Room room)
    {
        await Task.CompletedTask;
        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        if (scores.Count == 0) throw new HubException("Phòng chưa có bảng điểm.");
        var spinner = scores.OrderBy(s => s.TotalScore).First();
        var orderedDesc = scores.OrderByDescending(s => s.TotalScore).ToList();
        var donors = new HashSet<Guid>();
        if (orderedDesc.Count > 0 && orderedDesc[0].TotalScore > 0) donors.Add(orderedDesc[0].UserId);
        if (orderedDesc.Count > 1 && orderedDesc[1].TotalScore > 0) donors.Add(orderedDesc[1].UserId);
        return (spinner, donors);
    }

    private static bool AreAllDonorsDecided(Domain.Room room)
    {
        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        var orderedDesc = scores.OrderByDescending(s => s.TotalScore).ToList();
        var donors = new HashSet<Guid>();
        if (orderedDesc.Count > 0 && orderedDesc[0].TotalScore > 0) donors.Add(orderedDesc[0].UserId);
        if (orderedDesc.Count > 1 && orderedDesc[1].TotalScore > 0) donors.Add(orderedDesc[1].UserId);
        if (donors.Count == 0) return true; // không có donor → ok luôn

        // Nếu không có ai âm điểm thì sponsor cũng vô nghĩa → coi như xong.
        bool anyNeg = scores.Any(s => s.TotalScore < 0);
        if (!anyNeg) return true;

        List<Guid> decided = new();
        if (!string.IsNullOrEmpty(room.SponsorDecisionsJson))
        {
            try { decided = JsonSerializer.Deserialize<List<Guid>>(room.SponsorDecisionsJson) ?? new(); } catch { }
        }
        return donors.All(d => decided.Contains(d));
    }

    public async Task EndMatch()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var match = _matches.GetByRoom(roomId.Value);
        if (match == null) throw new HubException("Trận không tồn tại.");
        if (match.HostUserId != auth.Value.UserId) throw new HubException("Chỉ chủ phòng được kết thúc trận.");

        await FinalizeMatchAsync(match);
    }

    public async Task<MatchPublicStateDto?> RequestMatchState()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) return null;

        var match = _matches.GetByRoom(roomId.Value);
        if (match == null) return null;

        // Resend private hand to the requesting connection too
        var player = match.Players.FirstOrDefault(p => p.UserId == auth.Value.UserId);
        if (player != null)
        {
            await Clients.Caller.SendAsync("PrivateHand", new PrivateHandDto(
                match.Id,
                player.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList()));
        }
        await Clients.Caller.SendAsync("RoundHistory", new RoundHistoryDto(match.Id, match.RoundHistory.ToList()));
        return BuildMatchPublic(match);
    }

    public async Task PlayCards(List<CardDto> cards)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        var parsed = cards.Select(c => new Card(c.Rank, (Suit)c.Suit)).ToList();
        PlayResult result;
        try
        {
            result = _matches.Play(roomId.Value, auth.Value.UserId, parsed);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        var player = result.Match.Players.First(p => p.UserId == auth.Value.UserId);
        await SendPrivateHandToUserAsync(roomId.Value, player);

        if (result.RoundEnded)
        {
            await EmitRoundEndAsync(result.Match);
        }
    }

    public async Task PassTurn()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        PassResult result;
        try
        {
            result = _matches.Pass(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        if (result.RoundEnded)
        {
            await EmitRoundEndAsync(result.Match);
        }
    }

    public async Task Surrender()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        PassResult result;
        try
        {
            result = _matches.Surrender(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        if (result.RoundEnded)
        {
            await EmitRoundEndAsync(result.Match);
        }
    }

    public async Task StartVoteReset()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        VoteResetResult result;
        try
        {
            result = _matches.StartVoteReset(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        if (result.Dealt)
        {
            await SendPrivateHandsAsync(result.Match);
            if (result.Match.Status == MatchStatus.WaitingNextRound)
                await EmitRoundEndAsync(result.Match); // bài mới về trắng
        }
    }

    public async Task RespondVoteReset(bool accept)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        VoteResetResult result;
        try
        {
            result = _matches.RespondVoteReset(roomId.Value, auth.Value.UserId, accept);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(result.Match));
        if (result.Dealt)
        {
            await SendPrivateHandsAsync(result.Match);
            if (result.Match.Status == MatchStatus.WaitingNextRound)
                await EmitRoundEndAsync(result.Match);
        }
    }

    public async Task ScheduleFestival()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.ScheduleFestival(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task ActivateStarOfHope()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.ActivateStarOfHope(roomId.Value, auth.Value.UserId);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task RespondGamble(bool accept)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.RespondGamble(roomId.Value, auth.Value.UserId, accept);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task ScheduleBreak(int gameType = 1)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.ScheduleBreak(roomId.Value, auth.Value.UserId, (GameEngine.BreakGameType)gameType); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task SubmitMathNumber(int number)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.SubmitMathNumber(roomId.Value, auth.Value.UserId, number); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task SubmitMathAnswer(int optionIndex)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.SubmitMathAnswer(roomId.Value, auth.Value.UserId, optionIndex); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        // Math quiz finalize chỉ qua timer (reveal phase), không kết thúc tức thì ở đây.
    }

    public async Task SubmitMemoryAnswer(int optionIndex)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.SubmitMemoryAnswer(roomId.Value, auth.Value.UserId, optionIndex); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task SubmitReflexCell(int cellIndex)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.SubmitReflexCell(roomId.Value, auth.Value.UserId, cellIndex); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task SubmitRpsChoice(int choice)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.SubmitRpsChoice(roomId.Value, auth.Value.UserId, (GameEngine.RpsChoice)choice); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        // Nếu giải lao vừa kết thúc → emit round-end (bảng xếp hạng + điểm).
        if (match.Status == MatchStatus.WaitingNextRound)
            await Clients.Group(GroupName(roomId.Value)).SendAsync("RoundEnd", _matches.BuildRoundEndDto(match));
    }

    public async Task ActivateXiDach()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.ActivateXiDach(roomId.Value, auth.Value.UserId); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    public async Task DrawXiDachCard()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.DrawXiDachCard(roomId.Value, auth.Value.UserId); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        await SendPrivateHandsAsync(match);
        if (match.Status == MatchStatus.WaitingNextRound) await EmitRoundEndAsync(match);
    }

    public async Task StandXiDach()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.StandXiDach(roomId.Value, auth.Value.UserId); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        if (match.Status == MatchStatus.WaitingNextRound) await EmitRoundEndAsync(match);
    }

    public async Task CompareXiDach(Guid targetUserId)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.CompareXiDachPlayer(roomId.Value, auth.Value.UserId, targetUserId); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        if (match.Status == MatchStatus.WaitingNextRound) await EmitRoundEndAsync(match);
    }

    public async Task CompareXiDachAll()
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try { match = _matches.CompareXiDachPlayer(roomId.Value, auth.Value.UserId, null); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
        if (match.Status == MatchStatus.WaitingNextRound) await EmitRoundEndAsync(match);
    }

    public async Task FlipFestivalCard(bool flipAll, int cardIndex)
    {
        var auth = await AuthenticateAsync();
        if (auth == null) throw new HubException("Unauthorized");
        var roomId = _presence.CurrentRoom(Context.ConnectionId);
        if (roomId == null) throw new HubException("Chưa vào phòng nào.");

        Match match;
        try
        {
            match = _matches.FlipFestivalCard(roomId.Value, auth.Value.UserId, flipAll, cardIndex);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Clients.Group(GroupName(roomId.Value)).SendAsync("MatchState", BuildMatchPublic(match));
    }

    private async Task EmitRoundEndAsync(Match match)
    {
        var dto = _matches.BuildRoundEndDto(match);
        await Clients.Group(GroupName(match.RoomId)).SendAsync("RoundEnd", dto);
        await Clients.Group(GroupName(match.RoomId)).SendAsync("MatchState", BuildMatchPublic(match));
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
        var finalScores = match.Players
            .OrderByDescending(p => p.TotalScore)
            .Select(p => new RoundResultEntryDto(p.UserId, p.DisplayName, 0, 0, p.TotalScore, null, 0, false, false, false, false, false, 0, 0, 0, 0, 0, 0, new HeldItemsDto(0, 0, false, false, false), new List<HeldDetailDto>()))
            .ToList();
        await Clients.Group(GroupName(match.RoomId)).SendAsync("MatchEnd", new MatchEndDto(match.Id, finalScores));

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == match.RoomId);
        if (room != null)
        {
            room.Status = RoomStatus.Finished;
            room.FinishedAt = DateTime.UtcNow;
            var userIds = match.Players.Select(p => p.UserId).ToList();
            var avatarMap = await _db.AppUsers
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, HasAvatar = !string.IsNullOrEmpty(u.AvatarData) })
                .ToDictionaryAsync(x => x.Id, x => x.HasAvatar);
            var persistScores = match.Players
                .OrderByDescending(p => p.TotalScore)
                .Select(p => new RoomFinalScoreEntryDto(
                    p.UserId,
                    p.DisplayName,
                    p.TotalScore,
                    avatarMap.GetValueOrDefault(p.UserId, false)))
                .ToList();
            room.FinalScoresJson = JsonSerializer.Serialize(persistScores);
            await _db.SaveChangesAsync();
        }
        _matches.Remove(match.RoomId);
        _chat.Clear(match.RoomId);
    }

    /// <summary>Build public match state DTO. PUBLIC STATIC để MatchTimerService dùng chung — KHÔNG duplicate
    /// (bug cũ: timer có bản copy riêng thiếu field RPS/gamble → client treo ở round đặc biệt).</summary>
    public static MatchPublicStateDto BuildMatchPublic(Match m)
    {
        return new MatchPublicStateDto(
            m.Id,
            m.RoomId,
            (int)m.Status,
            m.RoundNumber,
            m.CurrentTurnSeatIndex,
            m.CurrentTrickOwnerId,
            m.CurrentTrick?.Cards.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.TurnDeadline,
            m.NextRoundAt,
            m.HostUserId,
            m.Players.Select(p => new MatchPlayerDto(
                p.UserId,
                p.DisplayName,
                p.SeatIndex,
                p.Hand.Count,
                p.FinalRank,
                p.PassedThisTrick,
                p.TotalScore,
                p.WhiteWinReason,
                p.WhiteWinAccepted,
                p.HasAvatar,
                p.Surrendered,
                p.VoteResetChoice,
                p.HasUsedVoteReset,
                p.HasUsedFestival,
                p.FestivalWinner,
                p.FestivalRevealedIdx.Count,
                m.IsFestivalRound
                    ? p.Hand.Select((c, i) => p.FestivalRevealedIdx.Contains(i) ? new CardDto(c.Rank, (int)c.Suit) : (CardDto?)null).ToList()
                    : null,
                p.HasUsedStarOfHope,
                p.IsStarOfHope,
                p.HasUsedXiDach,
                p.IsXiDachDealer,
                p.XiDachStood,
                p.XiDachSettled,
                p.XiDachRevealed,
                (m.IsXiDachRound && p.XiDachRevealed) ? XiDachEngine.Total(p.Hand) : 0,
                (m.IsXiDachRound && p.XiDachRevealed) ? p.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList() : null,
                p.WinStreak,
                p.IsGambling,
                p.HasUsedBreak)).ToList(),
            m.WhiteWinDeadline,
            m.TrickCutDeadline,
            m.PendingTrickWinnerId,
            m.TrickCutCandidates.Count > 0 ? new List<Guid>(m.TrickCutCandidates) : null,
            m.LastWonTrickCards?.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.LastWonTrickWinnerId,
            m.ShowOpponentCardCount,
            m.VoteResetDeadline,
            m.VoteResetInitiatorId,
            m.PastFirstTrick,
            m.FestivalScheduled,
            m.IsFestivalRound,
            m.FestivalOrganizerId,
            m.FestivalRevealDeadline,
            m.FestivalAutoFlipDeadline,
            m.StarOfHopeScheduledUserId,
            m.XiDachScheduledUserId,
            m.IsXiDachRound,
            m.XiDachDealerId,
            m.XiDachTurnUserId,
            m.XiDachTurnDeadline,
            m.GambleOfferUserId,
            m.GambleScheduledUserId,
            m.IsGambleRound,
            m.GambleOfferDeadline,
            m.BreakScheduled,
            (int)m.BreakScheduledType,
            m.BreakOrganizerId,
            m.IsBreakRound,
            BuildRpsState(m.Rps),
            m.RpsChoiceDeadline,
            m.RpsRevealUntil,
            (int)m.BreakGame,
            BuildMathState(m),
            m.MathPickDeadline,
            m.MathAnswerDeadline,
            m.MathRevealUntil,
            BuildMemoryState(m),
            m.MemoryViewDeadline,
            m.MemoryAnswerDeadline,
            m.MemoryRevealUntil,
            BuildReflexState(m),
            m.ReflexCooldownUntil,
            m.ReflexAnswerDeadline,
            m.ReflexRevealUntil);
    }

    private static RpsStateDto? BuildRpsState(GameEngine.RpsTournament? t)
    {
        if (t == null) return null;
        static RpsMatchupDto M(GameEngine.RpsMatchup m) => new(
            m.PlayerAId, m.PlayerBId, m.WinTarget, m.WinsA, m.WinsB, m.WinnerId, m.LoserId,
            m.ChoiceA != GameEngine.RpsChoice.None, m.ChoiceB != GameEngine.RpsChoice.None,
            (int)m.LastChoiceA, (int)m.LastChoiceB, (int)m.LastOutcome, m.HasLast);
        return new RpsStateDto((int)t.Stage, M(t.Round1A), M(t.Round1B), M(t.ThirdPlace), M(t.Final), new List<Guid>(t.FinalRanking));
    }

    /// <summary>
    /// Build state Tính toán cho broadcast. Pha trả lời: KHÔNG lộ đáp án đúng (CorrectIndex=-1) hay chọn của ai
    /// (ChosenIndex=-1) — chỉ lộ ai đã trả lời. Pha hiện đáp án (reveal): lộ CorrectIndex + chọn + đúng/sai + thời gian.
    /// </summary>
    private static MathQuizStateDto? BuildMathState(Match m)
    {
        if (m.BreakGame != GameEngine.BreakGameType.Math) return null;
        bool isPick = m.Status == MatchStatus.BreakMathPick;
        bool reveal = m.MathRevealUntil.HasValue;
        int phase = isPick ? 0 : reveal ? 2 : 1;

        var picks = m.MathPicks.Select(kv => new MathPickDto(kv.Key, kv.Value)).ToList();
        int qIdx = m.MathCurrentQuestion;

        MathQuestionDto? question = null;
        var answered = new List<Guid>();
        if (!isPick && m.MathQuestions != null && qIdx < m.MathQuestions.Count)
        {
            var q = m.MathQuestions[qIdx];
            question = new MathQuestionDto(q.Expression, new List<int>(q.Options), reveal ? q.CorrectIndex : -1);
            foreach (var p in m.Players)
                if (m.MathAnswers.TryGetValue(p.UserId, out var l) && l.Count > qIdx && l[qIdx].Answered)
                    answered.Add(p.UserId);
        }

        var results = new List<MathPlayerResultDto>();
        foreach (var p in m.Players)
        {
            m.MathAnswers.TryGetValue(p.UserId, out var list);
            list ??= new();
            int correctCount = list.Count(a => a.Correct);
            long totalCorrectMs = list.Where(a => a.Correct).Sum(a => a.ElapsedMs);
            var cur = (qIdx < list.Count) ? list[qIdx] : null;
            results.Add(new MathPlayerResultDto(
                p.UserId,
                reveal && cur != null ? cur.ChosenIndex : -1,   // chỉ lộ chọn ở pha reveal
                cur?.Answered ?? false,
                reveal && (cur?.Correct ?? false),
                reveal && cur != null ? cur.ElapsedMs : 0,
                correctCount,
                totalCorrectMs));
        }

        return new MathQuizStateDto(
            phase, picks,
            m.MathQuestions?.Count ?? MathQuizEngine.NumQuestions,
            qIdx, question, answered, results, new List<Guid>());
    }

    /// <summary>
    /// Build state Trí nhớ cho broadcast. Pha view: gửi đủ lưới 9 logo. Pha trả lời: ẩn lưới + ẩn đáp án đúng
    /// + ẩn chọn của người khác (chỉ lộ ai đã trả lời). Pha reveal: lộ AnswerSlug + CorrectIndex + chọn/đúng-sai/time.
    /// </summary>
    private static MemoryGameStateDto? BuildMemoryState(Match m)
    {
        if (m.BreakGame != GameEngine.BreakGameType.Memory || m.MemoryBoard == null) return null;
        bool isView = m.Status == MatchStatus.BreakMemoryView;
        bool reveal = m.MemoryRevealUntil.HasValue;
        int phase = isView ? 0 : reveal ? 2 : 1;
        int qIdx = m.MemoryCurrentQuestion;

        MemoryQuestionDto? question = null;
        string? answerSlug = null;
        var answered = new List<Guid>();
        if (!isView && qIdx < m.MemoryBoard.Questions.Count)
        {
            var q = m.MemoryBoard.Questions[qIdx];
            question = new MemoryQuestionDto(q.CellIndex, new List<string>(q.Options), reveal ? q.CorrectIndex : -1);
            if (reveal) answerSlug = q.AnswerSlug;
            foreach (var p in m.Players)
                if (m.MemoryAnswers.TryGetValue(p.UserId, out var l) && l.Count > qIdx && l[qIdx].Answered)
                    answered.Add(p.UserId);
        }

        var results = new List<MathPlayerResultDto>();
        foreach (var p in m.Players)
        {
            m.MemoryAnswers.TryGetValue(p.UserId, out var list);
            list ??= new();
            int correctCount = list.Count(a => a.Correct);
            long totalCorrectMs = list.Where(a => a.Correct).Sum(a => a.ElapsedMs);
            var cur = (qIdx < list.Count) ? list[qIdx] : null;
            results.Add(new MathPlayerResultDto(
                p.UserId,
                reveal && cur != null ? cur.ChosenIndex : -1,
                cur?.Answered ?? false,
                reveal && (cur?.Correct ?? false),
                reveal && cur != null ? cur.ElapsedMs : 0,
                correctCount,
                totalCorrectMs));
        }

        return new MemoryGameStateDto(
            phase,
            isView ? new List<string>(m.MemoryBoard.Grid) : null,   // chỉ gửi lưới ở pha view
            m.MemoryBoard.Questions.Count,
            qIdx, question, answerSlug, answered, results);
    }

    /// <summary>
    /// Build state Phản xạ cho broadcast (bài 52 lá, lưới 4×4, tìm 3 lá). Pha cooldown (0): grid RỖNG (ẩn) + chưa gửi đề
    /// → tránh đoán trước. Pha click (1): gửi lưới 16 lá + đề (TargetCards 3 lá) nhưng KHÔNG gửi TargetIndexes.
    /// Pha reveal (2): gửi cả TargetIndexes + đúng/sai/time. Client tự nhớ lá MÌNH đã chọn.
    /// </summary>
    private static ReflexGameStateDto? BuildReflexState(Match m)
    {
        if (m.BreakGame != GameEngine.BreakGameType.Reflex || m.ReflexRounds == null) return null;
        bool cooldown = m.Status == MatchStatus.BreakReflexCooldown;
        bool reveal = m.ReflexRevealUntil.HasValue;
        int phase = cooldown ? 0 : reveal ? 2 : 1;
        int rIdx = m.ReflexCurrentRound;
        var round = m.ReflexRounds[rIdx];

        var grid = cooldown
            ? new List<CardDto>()   // ẩn lưới ở cooldown
            : round.Grid.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList();
        var answered = new List<Guid>();
        if (!cooldown)
            foreach (var p in m.Players)
                if (m.ReflexAnswers.TryGetValue(p.UserId, out var l) && l.Count > rIdx && l[rIdx].Answered)
                    answered.Add(p.UserId);

        var results = new List<MathPlayerResultDto>();
        foreach (var p in m.Players)
        {
            m.ReflexAnswers.TryGetValue(p.UserId, out var list);
            list ??= new();
            int correctCount = list.Count(a => a.Correct);
            long totalCorrectMs = list.Where(a => a.Correct).Sum(a => a.ElapsedMs);
            var cur = (rIdx < list.Count) ? list[rIdx] : null;
            results.Add(new MathPlayerResultDto(
                p.UserId,
                -1,                                  // không dùng ChosenIndex cho game này (chọn 3 lá)
                cur?.Answered ?? false,
                reveal && (cur?.Correct ?? false),
                reveal && cur != null ? cur.ElapsedMs : 0,
                correctCount,
                totalCorrectMs));
        }

        return new ReflexGameStateDto(
            phase, grid, m.ReflexRounds.Count, rIdx,
            cooldown ? null : round.TargetCards.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),  // đề 3 lá từ pha click
            reveal ? new List<int>(round.TargetIndexes) : null,   // 3 ô đúng chỉ lộ ở reveal
            answered, results);
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
                _presence.IsOnline(room.Id, s.UserId),
                !string.IsNullOrEmpty(s.User?.AvatarData)))
            .ToList();

        return new RoomStateDto(
            room.Id,
            room.Code,
            room.Name,
            room.GameType,
            room.MaxSeats,
            (int)room.Status,
            room.HostUserId,
            room.CreatedAt,
            room.StartedAt,
            seats,
            room.ShowOpponentCardCount);
    }
}
