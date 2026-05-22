using System.Collections.Concurrent;
using CutPig.GameEngine;

namespace CutPig.Services;

public class MatchManager
{
    private readonly ConcurrentDictionary<Guid, Match> _matchesByRoom = new();
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public static TimeSpan TurnTimeout { get; } = TimeSpan.FromSeconds(45);
    public static TimeSpan WhiteWinChoiceTimeout { get; } = TimeSpan.FromSeconds(10);
    public static TimeSpan TrickCutTimeout { get; } = TimeSpan.FromSeconds(5);

    private object LockFor(Guid roomId) => _locks.GetOrAdd(roomId, _ => new object());

    public Match? GetByRoom(Guid roomId)
    {
        _matchesByRoom.TryGetValue(roomId, out var m);
        return m;
    }

    public Match Create(Guid roomId, Guid hostUserId, IReadOnlyList<(Guid UserId, string DisplayName, int SeatIndex, bool HasAvatar)> players)
    {
        lock (LockFor(roomId))
        {
            if (_matchesByRoom.TryGetValue(roomId, out var existing) && existing.Status != MatchStatus.Finished)
                return existing;

            var match = new Match { RoomId = roomId, HostUserId = hostUserId };
            foreach (var p in players.OrderBy(p => p.SeatIndex))
            {
                match.Players.Add(new MatchPlayer
                {
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    HasAvatar = p.HasAvatar,
                    SeatIndex = p.SeatIndex,
                });
            }
            DealRound(match, isFirstRound: true);
            _matchesByRoom[roomId] = match;
            return match;
        }
    }

    /// <summary>Deal a new round inside an existing match (host-triggered or system auto-trigger).</summary>
    public Match StartNextRound(Guid roomId, Guid? hostUserId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (hostUserId.HasValue && match.HostUserId != hostUserId.Value)
                throw new InvalidOperationException("Chỉ chủ phòng được mở ván mới.");
            if (match.Status != MatchStatus.WaitingNextRound)
                throw new InvalidOperationException("Ván trước chưa kết thúc.");

            DealRound(match, isFirstRound: false);
            return match;
        }
    }

    private static void DealRound(Match match, bool isFirstRound)
    {
        match.RoundNumber = isFirstRound ? 1 : match.RoundNumber + 1;
        match.Status = MatchStatus.InProgress;
        match.CurrentTrick = null;
        match.CurrentTrickOwnerId = null;
        match.FinishedCount = 0;
        match.FinishOrder.Clear();
        match.WhiteWinDeadline = null;
        match.TrickCutDeadline = null;
        match.PendingTrickWinnerId = null;
        match.TrickCutCandidates.Clear();
        match.TrickChopChain.Clear();
        match.RoundChopExtra.Clear();
        foreach (var p in match.Players)
        {
            p.Hand.Clear();
            p.FinalRank = null;
            p.PassedThisTrick = false;
            p.WhiteWinReason = null;
            p.WhiteWinAccepted = null;
            p.FinishedWithThreeOfSpades = false;
            p.StuckWithThreeOfSpades = false;
        }

        // Deal exactly 13 cards each; remaining cards are buried.
        var deck = Deck.Shuffle(Deck.Build(), Random.Shared);
        int idx = 0;
        foreach (var p in match.Players)
        {
            for (int i = 0; i < 13 && idx < deck.Count; i++, idx++)
                p.Hand.Add(deck[idx]);
            p.Hand = p.Hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();
        }

        // Detect white-win candidates
        bool anyWhiteWin = false;
        foreach (var p in match.Players)
        {
            var reason = TienLenComboEngine.DetectWhiteWin(p.Hand);
            if (reason != null)
            {
                p.WhiteWinReason = reason;
                anyWhiteWin = true;
            }
        }

        if (anyWhiteWin)
        {
            // Move to choice phase: each candidate must accept/decline
            match.Status = MatchStatus.WhiteWinChoice;
            match.WhiteWinDeadline = DateTime.UtcNow + WhiteWinChoiceTimeout;
            return;
        }

        SetupFirstTurn(match, isFirstRound);
    }

    private static void SetupFirstTurn(Match match, bool isFirstRound)
    {
        // Determine first turn
        int firstSeat;
        if (isFirstRound)
        {
            // Player holding 3 of Spades
            firstSeat = match.Players.FindIndex(p => p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades));
            if (firstSeat < 0) firstSeat = 0;
        }
        else
        {
            // Winner of previous round
            firstSeat = match.PreviousRoundWinnerId.HasValue
                ? match.Players.FindIndex(p => p.UserId == match.PreviousRoundWinnerId.Value)
                : 0;
            if (firstSeat < 0) firstSeat = 0;
        }
        match.CurrentTurnSeatIndex = firstSeat;
        match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
    }

    public void Remove(Guid roomId)
    {
        _matchesByRoom.TryRemove(roomId, out _);
    }

    /// <summary>
    /// Player chooses accept/decline white-win during WhiteWinChoice phase.
    /// Returns true if the round was fully resolved (status changed to WaitingNextRound or InProgress).
    /// </summary>
    public Match RespondWhiteWin(Guid roomId, Guid userId, bool accept)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (match.Status != MatchStatus.WhiteWinChoice)
                throw new InvalidOperationException("Không trong lúc chọn về trắng.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.WhiteWinReason == null)
                throw new InvalidOperationException("Bạn không có bộ về trắng.");
            if (player.WhiteWinAccepted.HasValue)
                throw new InvalidOperationException("Đã chọn rồi.");

            player.WhiteWinAccepted = accept;
            TryResolveWhiteWinChoice(match);
            return match;
        }
    }

    /// <summary>Called by timer service when WhiteWinDeadline passes — treat unset as decline.</summary>
    public Match? ResolveWhiteWinTimeout(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)) return null;
            if (match.Status != MatchStatus.WhiteWinChoice) return null;
            foreach (var p in match.Players.Where(p => p.WhiteWinReason != null && !p.WhiteWinAccepted.HasValue))
                p.WhiteWinAccepted = false;
            TryResolveWhiteWinChoice(match);
            return match;
        }
    }

    private static void TryResolveWhiteWinChoice(Match match)
    {
        var candidates = match.Players.Where(p => p.WhiteWinReason != null).ToList();
        if (candidates.Any(p => !p.WhiteWinAccepted.HasValue)) return; // còn người chưa chọn

        var accepted = candidates.Where(p => p.WhiteWinAccepted == true).ToList();
        match.WhiteWinDeadline = null;

        if (accepted.Count == 0)
        {
            // Không ai về trắng → chơi bình thường, xoá WhiteWinReason để không tính điểm white-win
            foreach (var p in candidates)
            {
                p.WhiteWinReason = null;
                p.WhiteWinAccepted = null;
            }
            SetupFirstTurn(match, isFirstRound: match.RoundNumber == 1);
            return;
        }

        // Có người về trắng → kết thúc ván ngay
        // Người từ chối: clear reason, sẽ chia điểm như người không về trắng
        foreach (var p in candidates.Where(p => p.WhiteWinAccepted != true))
        {
            p.WhiteWinReason = null;
        }

        int rank = 1;
        foreach (var p in match.Players.Where(p => p.WhiteWinReason != null))
        {
            p.FinalRank = rank;
            match.FinishOrder.Add(p.UserId);
            match.FinishedCount++;
        }
        rank = match.FinishedCount + 1;
        foreach (var p in match.Players.Where(p => p.WhiteWinReason == null))
        {
            p.FinalRank = rank++;
            match.FinishOrder.Add(p.UserId);
            match.FinishedCount++;
        }
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Player with 4-pair-run interrupts the trick reset to play it. Returns updated match.
    /// </summary>
    public PlayResult CutNewTrick(Guid roomId, Guid userId, IReadOnlyList<Card> cards)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (match.Status != MatchStatus.PendingTrickCut)
                throw new InvalidOperationException("Không trong lúc chặn trick.");
            if (!match.TrickCutCandidates.Contains(userId))
                throw new InvalidOperationException("Bạn không có quyền chặn.");

            var player = match.Players.First(p => p.UserId == userId);
            foreach (var c in cards)
                if (!player.Hand.Contains(c))
                    throw new InvalidOperationException("Bài không có trong tay.");
            var combo = TienLenComboEngine.Detect(cards)
                ?? throw new InvalidOperationException("Bộ bài không hợp lệ.");
            if (!TienLenComboEngine.IsFourPairRun(combo))
                throw new InvalidOperationException("Chỉ được chặn bằng 4 đôi thông.");

            // Apply: 4-pair-run beats the trick that just won (single 2 / pair 2)
            // Replace current trick with the 4-pair-run, switch owner to cutter, resume play
            foreach (var c in cards) player.Hand.Remove(c);
            match.CurrentTrick = combo;
            match.CurrentTrickOwnerId = userId;
            RecordChopPlay(match, userId, combo);
            match.Status = MatchStatus.InProgress;
            match.TrickCutDeadline = null;
            match.PendingTrickWinnerId = null;
            match.TrickCutCandidates.Clear();
            foreach (var p in match.Players) p.PassedThisTrick = false;
            // Cutter is now "active" again
            player.PassedThisTrick = false;

            bool justFinished = false;
            if (player.Hand.Count == 0)
            {
                match.FinishedCount++;
                player.FinalRank = match.FinishedCount;
                match.FinishOrder.Add(userId);
                justFinished = true;
                if (match.FinishedCount == 1) match.PreviousRoundWinnerId = userId;
                if (cards.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                    player.FinishedWithThreeOfSpades = true;
            }

            var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
            if (remaining.Count <= 1)
            {
                foreach (var p in remaining)
                {
                    match.FinishedCount++;
                    p.FinalRank = match.FinishedCount;
                    match.FinishOrder.Add(p.UserId);
                    if (p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                        p.StuckWithThreeOfSpades = true;
                }
                SettleTrickChopChain(match);
                match.Status = MatchStatus.WaitingNextRound;
                match.NextRoundAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                return new PlayResult(combo, justFinished, true, match);
            }

            // Next turn after cutter
            match.CurrentTurnSeatIndex = match.Players.FindIndex(p => p.UserId == userId);
            AdvanceTurnSkippingPassed(match);
            match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            return new PlayResult(combo, justFinished, false, match);
        }
    }

    /// <summary>Player declines to cut, or timer expires → finalize the trick reset.</summary>
    public Match? ResolveTrickCutTimeout(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)) return null;
            if (match.Status != MatchStatus.PendingTrickCut) return null;
            FinalizeTrickReset(match);
            return match;
        }
    }

    public Match DeclineTrickCut(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (match.Status != MatchStatus.PendingTrickCut)
                throw new InvalidOperationException("Không trong lúc chặn trick.");
            if (!match.TrickCutCandidates.Remove(userId))
                throw new InvalidOperationException("Bạn không có quyền chặn.");

            if (match.TrickCutCandidates.Count == 0)
            {
                FinalizeTrickReset(match);
            }
            return match;
        }
    }

    private static void FinalizeTrickReset(Match match)
    {
        if (!match.PendingTrickWinnerId.HasValue) return;
        var ownerId = match.PendingTrickWinnerId.Value;
        SettleTrickChopChain(match);
        match.CurrentTrick = null;
        match.CurrentTrickOwnerId = null;
        match.TrickCutDeadline = null;
        match.PendingTrickWinnerId = null;
        match.TrickCutCandidates.Clear();
        match.Status = MatchStatus.InProgress;
        foreach (var p in match.Players) p.PassedThisTrick = false;
        var ownerSeat = match.Players.FindIndex(p => p.UserId == ownerId);
        if (ownerSeat >= 0 && !match.Players[ownerSeat].FinalRank.HasValue)
        {
            match.CurrentTurnSeatIndex = ownerSeat;
        }
        else
        {
            AdvanceTurnSkippingPassed(match);
        }
        match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
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

            bool isMatchOpener = match.RoundNumber == 1
                && match.CurrentTrick == null
                && match.Players.All(p => p.Hand.Count >= 12); // nobody has played yet

            // Only enforce 3-of-spades opening if 3♠ was actually dealt (vs being in the buried remainder for 2-3 players)
            bool threeOfSpadesInPlay = match.Players.Any(p => p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades));
            if (isMatchOpener && threeOfSpadesInPlay && !cards.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
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
            RecordChopPlay(match, userId, combo);
            // If player was previously passed in this trick but used 4-pair-run, clear pass flag (they're back in)
            if (TienLenComboEngine.IsFourPairRun(combo) && player.PassedThisTrick)
            {
                player.PassedThisTrick = false;
            }

            bool justFinished = false;
            if (player.Hand.Count == 0)
            {
                match.FinishedCount++;
                player.FinalRank = match.FinishedCount;
                match.FinishOrder.Add(userId);
                justFinished = true;
                if (match.FinishedCount == 1) match.PreviousRoundWinnerId = userId;
                if (cards.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                    player.FinishedWithThreeOfSpades = true;
            }

            // Check round end (only one or zero active player remaining)
            var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
            if (remaining.Count <= 1)
            {
                foreach (var p in remaining)
                {
                    match.FinishedCount++;
                    p.FinalRank = match.FinishedCount;
                    match.FinishOrder.Add(p.UserId);
                    if (p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                        p.StuckWithThreeOfSpades = true;
                }
                SettleTrickChopChain(match);
                match.Status = MatchStatus.WaitingNextRound;
                match.NextRoundAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                return new PlayResult(combo, justFinished, true, match);
            }

            AdvanceTurnSkippingPassed(match);
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

            if (match.CurrentTrick == null)
            {
                if (isAutoPass)
                {
                    // Auto-pass on free turn: play smallest single
                    var smallest = current.Hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).First();
                    var combo = TienLenComboEngine.Detect(new[] { smallest })!;
                    current.Hand.Remove(smallest);
                    match.CurrentTrick = combo;
                    match.CurrentTrickOwnerId = userId;
                    RecordChopPlay(match, userId, combo);

                    if (current.Hand.Count == 0)
                    {
                        match.FinishedCount++;
                        current.FinalRank = match.FinishedCount;
                        match.FinishOrder.Add(userId);
                        if (match.FinishedCount == 1) match.PreviousRoundWinnerId = userId;
                        if (smallest.Rank == 3 && smallest.Suit == Suit.Spades)
                            current.FinishedWithThreeOfSpades = true;
                    }
                    var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
                    if (remaining.Count <= 1)
                    {
                        foreach (var p in remaining)
                        {
                            match.FinishedCount++;
                            p.FinalRank = match.FinishedCount;
                            match.FinishOrder.Add(p.UserId);
                            if (p.Hand.Any(c => c.Rank == 3 && c.Suit == Suit.Spades))
                                p.StuckWithThreeOfSpades = true;
                        }
                        SettleTrickChopChain(match);
                        match.Status = MatchStatus.WaitingNextRound;
                match.NextRoundAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                        return new PassResult(false, true, match);
                    }
                    AdvanceTurnSkippingPassed(match);
                    match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
                    return new PassResult(false, false, match);
                }
                throw new InvalidOperationException("Không thể bỏ qua khi đang mở nước.");
            }

            current.PassedThisTrick = true;

            // If all other active players passed → trick won by owner
            bool allOthersPassed = match.Players.All(p =>
                p.FinalRank.HasValue
                || p.UserId == match.CurrentTrickOwnerId
                || p.PassedThisTrick);

            bool newTrick = false;
            bool pendingCut = false;
            if (allOthersPassed && match.CurrentTrickOwnerId.HasValue)
            {
                // Check if any non-owner, non-finished player still holds 4-pair-run → offer trick-cut window
                var ownerId = match.CurrentTrickOwnerId.Value;
                var cutCandidates = match.Players
                    .Where(p => p.UserId != ownerId
                        && !p.FinalRank.HasValue
                        && TienLenComboEngine.HasFourPairRunInHand(p.Hand))
                    .Select(p => p.UserId)
                    .ToList();

                if (cutCandidates.Count > 0)
                {
                    match.Status = MatchStatus.PendingTrickCut;
                    match.PendingTrickWinnerId = ownerId;
                    match.TrickCutCandidates.Clear();
                    match.TrickCutCandidates.AddRange(cutCandidates);
                    match.TrickCutDeadline = DateTime.UtcNow + TrickCutTimeout;
                    pendingCut = true;
                }
                else
                {
                    SettleTrickChopChain(match);
                    match.CurrentTrick = null;
                    match.CurrentTrickOwnerId = null;
                    foreach (var p in match.Players) p.PassedThisTrick = false;
                    var ownerSeat = match.Players.FindIndex(p => p.UserId == ownerId);
                    if (ownerSeat >= 0 && !match.Players[ownerSeat].FinalRank.HasValue)
                    {
                        match.CurrentTurnSeatIndex = ownerSeat;
                    }
                    else
                    {
                        AdvanceTurnSkippingPassed(match);
                    }
                    newTrick = true;
                }
            }
            else
            {
                AdvanceTurnSkippingPassed(match);
            }

            if (!pendingCut)
                match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            return new PassResult(newTrick, false, match);
        }
    }

    /// <summary>
    /// Settle the chop-pig chain at end of trick: if chain has ≥2 entries, the second-to-last player
    /// pays the sum of chopValue of chain[0..^1] to the last player. Intermediate players net zero.
    /// Then clear the chain. Safe to call when chain is empty or has 1 entry (no-op).
    /// </summary>
    private static void SettleTrickChopChain(Match match)
    {
        var chain = match.TrickChopChain;
        if (chain.Count >= 2)
        {
            var last = chain[^1];
            var secondLast = chain[^2];
            int pot = 0;
            for (int i = 0; i < chain.Count - 1; i++) pot += chain[i].ChopValue;
            if (pot > 0)
            {
                AddChopExtra(match, last.PlayerId, +pot);
                AddChopExtra(match, secondLast.PlayerId, -pot);
            }
        }
        chain.Clear();
    }

    private static void AddChopExtra(Match match, Guid playerId, int delta)
    {
        match.RoundChopExtra.TryGetValue(playerId, out var current);
        match.RoundChopExtra[playerId] = current + delta;
    }

    /// <summary>Append a play to the chop chain (only if combo has nonzero chop value).</summary>
    private static void RecordChopPlay(Match match, Guid playerId, Combo combo)
    {
        var value = TienLenComboEngine.ChopValue(combo);
        if (value > 0)
            match.TrickChopChain.Add((playerId, value));
    }

    /// <summary>Advance to next seat that is still active (not finished, not passed this trick).</summary>
    private static void AdvanceTurnSkippingPassed(Match match)
    {
        int n = match.Players.Count;
        int next = match.CurrentTurnSeatIndex;
        for (int i = 0; i < n; i++)
        {
            next = (next + 1) % n;
            var p = match.Players[next];
            if (p.FinalRank.HasValue) continue;
            if (p.PassedThisTrick) continue;
            match.CurrentTurnSeatIndex = next;
            return;
        }
        // No valid next → keep current (will be handled by caller)
    }

    public IEnumerable<Match> AllActive() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.InProgress);

    public IEnumerable<Match> AllWaitingNextRound() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.WaitingNextRound);

    public IEnumerable<Match> AllWhiteWinChoice() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.WhiteWinChoice);

    public IEnumerable<Match> AllPendingTrickCut() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.PendingTrickCut);

    public int[] ComputeRoundScores(Match match)
    {
        // Returns score for each player in seat order
        var n = match.Players.Count;
        var scores = new int[n];

        // White-win path: each loser pays 2 per winner; winners share the total equally.
        // (Chop-pig extras don't apply on white-win since the round ends before any trick is played.)
        var winnerCount = match.Players.Count(p => p.WhiteWinReason != null);
        if (winnerCount > 0)
        {
            int loserCount = n - winnerCount;
            int perWinner = 2 * loserCount;
            int perLoser = -2 * winnerCount;
            for (int i = 0; i < n; i++)
            {
                scores[i] = match.Players[i].WhiteWinReason != null ? perWinner : perLoser;
            }
            return scores;
        }

        // Normal path: base rank score + chop-pig settlements + 3♠ bonus/penalty.
        int[] table = n switch
        {
            4 => new[] { 2, 1, -1, -2 },
            3 => new[] { 2, 0, -2 },
            2 => new[] { 1, -1 },
            _ => Enumerable.Range(0, n).Select(_ => 0).ToArray()
        };
        for (int i = 0; i < n; i++)
        {
            var rank = (match.Players[i].FinalRank ?? n) - 1;
            scores[i] = table[Math.Clamp(rank, 0, table.Length - 1)];
            if (match.RoundChopExtra.TryGetValue(match.Players[i].UserId, out var chop))
                scores[i] += chop;
        }

        // Thắng cuối bằng 3♠: người Nhất +(n-1), mỗi người khác -1.
        var winner = match.Players.FirstOrDefault(p => p.FinalRank == 1 && p.FinishedWithThreeOfSpades);
        if (winner != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (match.Players[i].UserId == winner.UserId) scores[i] += (n - 1);
                else scores[i] -= 1;
            }
        }

        // Đui 3♠: người về Chót (FinalRank == n) còn 3♠ trong tay → -3, mỗi người khác +1.
        // (Không zero-sum với <4 người — theo rule user.)
        var loser = match.Players.FirstOrDefault(p => p.FinalRank == n && p.StuckWithThreeOfSpades);
        if (loser != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (match.Players[i].UserId == loser.UserId) scores[i] -= 3;
                else scores[i] += 1;
            }
        }

        return scores;
    }

    /// <summary>Read-only snapshot of per-player chop-pig deltas for the current round (for DTOs).</summary>
    public IReadOnlyDictionary<Guid, int> GetRoundChopExtras(Match match) => match.RoundChopExtra;
}

public record PlayResult(Combo Played, bool PlayerFinished, bool RoundEnded, Match Match);
public record PassResult(bool NewTrick, bool RoundEnded, Match Match);
