using System.Collections.Concurrent;
using CutPig.GameEngine;

namespace CutPig.Services;

public class MatchManager
{
    private readonly ConcurrentDictionary<Guid, Match> _matchesByRoom = new();
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public static TimeSpan TurnTimeout { get; } = TimeSpan.FromSeconds(30);

    private object LockFor(Guid roomId) => _locks.GetOrAdd(roomId, _ => new object());

    public Match? GetByRoom(Guid roomId)
    {
        _matchesByRoom.TryGetValue(roomId, out var m);
        return m;
    }

    public Match Create(Guid roomId, IReadOnlyList<(Guid UserId, string DisplayName, int SeatIndex)> players)
    {
        lock (LockFor(roomId))
        {
            if (_matchesByRoom.TryGetValue(roomId, out var existing) && existing.Status == MatchStatus.InProgress)
                return existing;

            var match = new Match { RoomId = roomId };
            foreach (var p in players.OrderBy(p => p.SeatIndex))
            {
                match.Players.Add(new MatchPlayer
                {
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    SeatIndex = p.SeatIndex,
                });
            }

            // Deal
            var deck = Deck.Shuffle(Deck.Build(), Random.Shared);
            for (int i = 0; i < deck.Count; i++)
            {
                match.Players[i % match.Players.Count].Hand.Add(deck[i]);
            }
            foreach (var p in match.Players)
                p.Hand = p.Hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();

            // First turn: player holding 3 of Spades
            var firstSeat = match.Players.FindIndex(p => p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades));
            if (firstSeat < 0) firstSeat = 0;
            match.CurrentTurnSeatIndex = firstSeat;
            match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            match.Log.Add(new MatchLogEntry(MatchLogKind.Deal, Guid.Empty, null, DateTime.UtcNow));

            _matchesByRoom[roomId] = match;
            return match;
        }
    }

    public void Remove(Guid roomId)
    {
        _matchesByRoom.TryRemove(roomId, out _);
    }

    public PlayResult Play(Guid roomId, Guid userId, IReadOnlyList<Card> cards)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");

            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.FinalRank.HasValue)
                throw new InvalidOperationException("Bạn đã hết bài.");
            var current = match.Players[match.CurrentTurnSeatIndex];
            if (current.UserId != userId)
                throw new InvalidOperationException("Chưa đến lượt bạn.");

            if (cards == null || cards.Count == 0)
                throw new InvalidOperationException("Chưa chọn bài.");
            foreach (var c in cards)
                if (!player.Hand.Contains(c))
                    throw new InvalidOperationException("Bài không có trong tay.");

            var combo = TienLenComboEngine.Detect(cards)
                ?? throw new InvalidOperationException("Bộ bài không hợp lệ.");

            // First move of match must include 3 of Spades
            bool isMatchOpener = match.Log.Count(e => e.Kind == MatchLogKind.Play) == 0;
            if (isMatchOpener && !cards.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                throw new InvalidOperationException("Nước đầu tiên phải có 3 bích.");

            if (match.CurrentTrick != null)
            {
                if (!TienLenComboEngine.Beats(match.CurrentTrick, combo))
                    throw new InvalidOperationException("Bộ này không chặn được nước trước.");
            }

            // Apply
            foreach (var c in cards) player.Hand.Remove(c);
            match.CurrentTrick = combo;
            match.CurrentTrickOwnerId = userId;
            match.Log.Add(new MatchLogEntry(MatchLogKind.Play, userId, combo.Cards, DateTime.UtcNow));

            bool justFinished = false;
            if (player.Hand.Count == 0)
            {
                match.FinishedCount++;
                player.FinalRank = match.FinishedCount;
                match.FinishOrder.Add(userId);
                match.Log.Add(new MatchLogEntry(MatchLogKind.PlayerFinished, userId, null, DateTime.UtcNow));
                justFinished = true;
            }

            // Check match end (only one or zero active player remaining)
            var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
            if (remaining.Count <= 1)
            {
                foreach (var p in remaining)
                {
                    match.FinishedCount++;
                    p.FinalRank = match.FinishedCount;
                    match.FinishOrder.Add(p.UserId);
                }
                match.Status = MatchStatus.Finished;
                match.Log.Add(new MatchLogEntry(MatchLogKind.MatchEnd, Guid.Empty, null, DateTime.UtcNow));
                return new PlayResult(combo, justFinished, true, match);
            }

            // Advance turn (skip already-finished)
            AdvanceTurn(match, skipCurrent: true);
            match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            return new PlayResult(combo, justFinished, false, match);
        }
    }

    public PassResult Pass(Guid roomId, Guid userId, bool isAutoPass = false)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");

            var current = match.Players[match.CurrentTurnSeatIndex];
            if (current.UserId != userId)
                throw new InvalidOperationException("Chưa đến lượt bạn.");

            // Cannot pass when there is no trick to beat (opener or new trick after all-pass)
            if (match.CurrentTrick == null)
            {
                if (isAutoPass)
                {
                    // Auto-pass on a free turn: forfeit current trick (play the smallest single)
                    // To keep simple: force play smallest card
                    var smallest = current.Hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).First();
                    // delegate to Play to avoid duplicating logic — but Play locks too.
                    // We're already inside lock, so re-enter via internal impl by releasing pattern is risky.
                    // Simpler: throw — caller decides. But for auto-pass we just kick to next anyway.
                    // For now: auto-pass on free turn = play smallest single inline.
                    var combo = TienLenComboEngine.Detect(new[] { smallest })!;
                    current.Hand.Remove(smallest);
                    match.CurrentTrick = combo;
                    match.CurrentTrickOwnerId = userId;
                    match.Log.Add(new MatchLogEntry(MatchLogKind.AutoPass, userId, combo.Cards, DateTime.UtcNow));

                    if (current.Hand.Count == 0)
                    {
                        match.FinishedCount++;
                        current.FinalRank = match.FinishedCount;
                        match.FinishOrder.Add(userId);
                    }
                    var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
                    if (remaining.Count <= 1)
                    {
                        foreach (var p in remaining)
                        {
                            match.FinishedCount++;
                            p.FinalRank = match.FinishedCount;
                            match.FinishOrder.Add(p.UserId);
                        }
                        match.Status = MatchStatus.Finished;
                        return new PassResult(false, true, match);
                    }
                    AdvanceTurn(match, skipCurrent: true);
                    match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
                    return new PassResult(false, false, match);
                }
                throw new InvalidOperationException("Không thể bỏ qua khi đang mở nước.");
            }

            match.Log.Add(new MatchLogEntry(isAutoPass ? MatchLogKind.AutoPass : MatchLogKind.Pass, userId, null, DateTime.UtcNow));

            // Mark seat passed for this trick. We track via simple convention: if turn comes back to trick owner, start new trick.
            AdvanceTurn(match, skipCurrent: true);

            // If the next seat IS the trick owner, that means everyone else passed → new trick
            bool newTrick = false;
            if (match.CurrentTrickOwnerId.HasValue
                && match.Players[match.CurrentTurnSeatIndex].UserId == match.CurrentTrickOwnerId.Value)
            {
                match.CurrentTrick = null;
                match.CurrentTrickOwnerId = null;
                match.Log.Add(new MatchLogEntry(MatchLogKind.NewTrick, Guid.Empty, null, DateTime.UtcNow));
                newTrick = true;
            }

            match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            return new PassResult(newTrick, false, match);
        }
    }

    private static void AdvanceTurn(Match match, bool skipCurrent)
    {
        int n = match.Players.Count;
        int next = match.CurrentTurnSeatIndex;
        for (int i = 0; i < n; i++)
        {
            next = (next + 1) % n;
            if (!match.Players[next].FinalRank.HasValue) break;
        }
        match.CurrentTurnSeatIndex = next;
    }

    public IEnumerable<Match> AllActive() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.InProgress);
}

public record PlayResult(Combo Played, bool PlayerFinished, bool MatchEnded, Match Match);
public record PassResult(bool NewTrick, bool MatchEnded, Match Match);
