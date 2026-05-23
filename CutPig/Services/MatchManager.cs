using System.Collections.Concurrent;
using CutPig.GameEngine;

namespace CutPig.Services;

public class MatchManager
{
    private readonly ConcurrentDictionary<Guid, Match> _matchesByRoom = new();
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public static TimeSpan TurnTimeout { get; } = TimeSpan.FromSeconds(60);
    public static TimeSpan NextRoundDelay { get; } = TimeSpan.FromSeconds(20);
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
        match.JudgeTriggered = false;
        foreach (var p in match.Players)
        {
            p.Hand.Clear();
            p.FinalRank = null;
            p.PassedThisTrick = false;
            p.WhiteWinReason = null;
            p.WhiteWinAccepted = null;
            p.FinishedWithThreeOfSpades = false;
            p.StuckWithThreeOfSpades = false;
            p.HasPlayedThisRound = false;
            p.JudgeIsWinner = false;
            p.JudgeIsVictim = false;
            p.JudgeIsPardoned = false;
            p.JudgeHeldValue = 0;
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
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
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
            player.HasPlayedThisRound = true;
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

                if (CheckAndApplyJudge(match, userId))
                    return new PlayResult(combo, justFinished, true, match);
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
                match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
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
            player.HasPlayedThisRound = true;
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

                // Phán xử: nếu Nhất về và còn player khác chưa ra bài
                if (CheckAndApplyJudge(match, userId))
                    return new PlayResult(combo, justFinished, true, match);
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
                match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
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
                    current.HasPlayedThisRound = true;
                    RecordChopPlay(match, userId, combo);

                    if (current.Hand.Count == 0)
                    {
                        match.FinishedCount++;
                        current.FinalRank = match.FinishedCount;
                        match.FinishOrder.Add(userId);
                        if (match.FinishedCount == 1) match.PreviousRoundWinnerId = userId;
                        if (smallest.Rank == 3 && smallest.Suit == Suit.Spades)
                            current.FinishedWithThreeOfSpades = true;

                        if (CheckAndApplyJudge(match, userId))
                            return new PassResult(false, true, match);
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
                match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
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
    /// Check for "Phán xử" (judge) trigger after a player just finishes #1.
    /// If any other active player has not played any card this round, switch the round into judge mode:
    ///   - Mark winner JudgeIsWinner, victims JudgeIsVictim (with held value), pardoned JudgeIsPardoned.
    ///   - Case A (0 pardoned) / Case B (1 pardoned): end the round immediately; assign FinalRank to all.
    ///   - Case C (≥2 pardoned): only victims get final rank (= n, tied at last); pardoned continue playing.
    /// Returns true if judge triggered the round to end (caller should stop further turn advancement).
    /// </summary>
    private static bool CheckAndApplyJudge(Match match, Guid winnerId)
    {
        // Already triggered? Skip.
        if (match.JudgeTriggered) return false;
        var winner = match.Players.FirstOrDefault(p => p.UserId == winnerId);
        if (winner == null || winner.FinalRank != 1) return false;

        // Collect victims: other players who haven't played yet
        var others = match.Players.Where(p => p.UserId != winnerId).ToList();
        var victims = others.Where(p => !p.HasPlayedThisRound).ToList();
        if (victims.Count == 0) return false;

        // Activate judge mode
        match.JudgeTriggered = true;
        winner.JudgeIsWinner = true;
        foreach (var v in victims)
        {
            v.JudgeIsVictim = true;
            v.JudgeHeldValue = TienLenComboEngine.ComputeHeldValue(v.Hand);
        }
        var pardoned = others.Where(p => p.HasPlayedThisRound).ToList();
        foreach (var p in pardoned)
            p.JudgeIsPardoned = true;

        if (pardoned.Count >= 2)
        {
            // Case C: victims share the last rank; pardoned continue playing normally
            int lastRank = match.Players.Count;
            foreach (var v in victims)
            {
                v.FinalRank = lastRank;
                match.FinishOrder.Add(v.UserId);
                match.FinishedCount++;
            }
            return false; // round continues with pardoned playing
        }

        // Case A or B: end the round immediately. Pardoned (if any) gets rank 2, victims share last.
        // Order: winner (1), pardoned (2 if exists), victims (tied at last).
        int nextRank = 2;
        foreach (var p in pardoned)
        {
            p.FinalRank = nextRank++;
            match.FinishOrder.Add(p.UserId);
            match.FinishedCount++;
        }
        int victimRank = nextRank;
        foreach (var v in victims)
        {
            v.FinalRank = victimRank;
            match.FinishOrder.Add(v.UserId);
            match.FinishedCount++;
        }
        SettleTrickChopChain(match);
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
        return true;
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

        // Phán xử path: replaces base rank + chop-pig + 3♠ scoring entirely.
        if (match.JudgeTriggered)
        {
            return ComputeJudgeScores(match);
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

    public record RoundScoreBreakdown(int BaseRank, int Chop, int ThreeOfSpades, int Judge, int WhiteWin, int Total);

    /// <summary>Per-player breakdown of the round score by component (for UI display).</summary>
    public RoundScoreBreakdown[] ComputeRoundScoreBreakdowns(Match match)
    {
        int n = match.Players.Count;
        var result = new RoundScoreBreakdown[n];

        var winnerCount = match.Players.Count(p => p.WhiteWinReason != null);
        if (winnerCount > 0)
        {
            int loserCount = n - winnerCount;
            int perWinner = 2 * loserCount;
            int perLoser = -2 * winnerCount;
            for (int i = 0; i < n; i++)
            {
                int v = match.Players[i].WhiteWinReason != null ? perWinner : perLoser;
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, v, v);
            }
            return result;
        }

        if (match.JudgeTriggered)
        {
            var judgeScores = ComputeJudgeScores(match);
            var winnerJudge = match.Players.FirstOrDefault(p => p.JudgeIsWinner);
            int winnerIdx = winnerJudge != null ? match.Players.IndexOf(winnerJudge) : -1;
            bool stack3s = winnerJudge?.FinishedWithThreeOfSpades ?? false;

            for (int i = 0; i < n; i++)
            {
                int threeBonus = stack3s ? (i == winnerIdx ? (n - 1) : -1) : 0;
                int judgePart = judgeScores[i] - threeBonus;
                result[i] = new RoundScoreBreakdown(0, 0, threeBonus, judgePart, 0, judgeScores[i]);
            }
            return result;
        }

        int[] table = n switch
        {
            4 => new[] { 2, 1, -1, -2 },
            3 => new[] { 2, 0, -2 },
            2 => new[] { 1, -1 },
            _ => Enumerable.Range(0, n).Select(_ => 0).ToArray()
        };

        var baseRank = new int[n];
        var chop = new int[n];
        var three = new int[n];

        for (int i = 0; i < n; i++)
        {
            var rank = (match.Players[i].FinalRank ?? n) - 1;
            baseRank[i] = table[Math.Clamp(rank, 0, table.Length - 1)];
            if (match.RoundChopExtra.TryGetValue(match.Players[i].UserId, out var chopVal))
                chop[i] = chopVal;
        }

        var winner = match.Players.FirstOrDefault(p => p.FinalRank == 1 && p.FinishedWithThreeOfSpades);
        if (winner != null)
        {
            for (int i = 0; i < n; i++)
                three[i] += (match.Players[i].UserId == winner.UserId) ? (n - 1) : -1;
        }
        var loser = match.Players.FirstOrDefault(p => p.FinalRank == n && p.StuckWithThreeOfSpades);
        if (loser != null)
        {
            for (int i = 0; i < n; i++)
                three[i] += (match.Players[i].UserId == loser.UserId) ? -3 : 1;
        }

        for (int i = 0; i < n; i++)
        {
            int total = baseRank[i] + chop[i] + three[i];
            result[i] = new RoundScoreBreakdown(baseRank[i], chop[i], three[i], 0, 0, total);
        }
        return result;
    }

    /// <summary>
    /// Judge ("Phán xử") scoring: each victim loses (4 + JudgeHeldValue). Winner gains the sum.
    /// Pardoned players:
    ///   - Case A (no pardoned): no extra.
    ///   - Case B (1 pardoned): pardoned loses -1, winner +1.
    ///   - Case C (≥2 pardoned): pardoned play a sub-round determining Nhì/Ba/... among themselves with
    ///     standard rank scoring (+1/-1 for 2, +2/0/-2 for 3, etc.) plus chop-pig + 3♠ between them.
    /// </summary>
    private static int[] ComputeJudgeScores(Match match)
    {
        int n = match.Players.Count;
        var scores = new int[n];
        var winnerIdx = -1;

        // Apply victim penalty
        for (int i = 0; i < n; i++)
        {
            var p = match.Players[i];
            if (p.JudgeIsWinner) winnerIdx = i;
            if (p.JudgeIsVictim)
            {
                int penalty = 4 + p.JudgeHeldValue;
                scores[i] -= penalty;
                if (winnerIdx >= 0) scores[winnerIdx] += penalty;
                else scores[Array.FindIndex(match.Players.ToArray(), x => x.JudgeIsWinner)] += penalty;
            }
        }
        // (If winnerIdx was -1 above, the inner branch handles it; recompute for the next blocks.)
        if (winnerIdx < 0) winnerIdx = Array.FindIndex(match.Players.ToArray(), x => x.JudgeIsWinner);

        var pardoned = match.Players.Where(p => p.JudgeIsPardoned).ToList();

        if (pardoned.Count == 1)
        {
            // Case B: pardoned -1, winner +1
            int pi = match.Players.IndexOf(pardoned[0]);
            scores[pi] -= 1;
            scores[winnerIdx] += 1;
        }
        else if (pardoned.Count >= 2)
        {
            // Case C: sub-round among pardoned by their FinalRank.
            // Sort pardoned by FinalRank ascending → assign sub-rank table.
            var ordered = pardoned.OrderBy(p => p.FinalRank ?? int.MaxValue).ToList();
            int m = ordered.Count;
            int[] subTable = m switch
            {
                3 => new[] { 2, 0, -2 },
                2 => new[] { 1, -1 },
                _ => Enumerable.Range(0, m).Select(_ => 0).ToArray()
            };
            for (int k = 0; k < m; k++)
            {
                int idx = match.Players.IndexOf(ordered[k]);
                scores[idx] += subTable[k];
            }

            // Chop-pig settlements only count for pardoned (their plays were tracked in chain).
            foreach (var p in pardoned)
            {
                int idx = match.Players.IndexOf(p);
                if (match.RoundChopExtra.TryGetValue(p.UserId, out var chop))
                    scores[idx] += chop;
            }
        }

        // Stack 3♠ bonus when the judge winner finished with 3♠ (applies on top of judge scoring).
        var winner = match.Players[winnerIdx];
        if (winner.FinishedWithThreeOfSpades)
        {
            int playerCount = match.Players.Count;
            for (int i = 0; i < playerCount; i++)
            {
                if (i == winnerIdx) scores[i] += (playerCount - 1);
                else scores[i] -= 1;
            }
        }
        // Note: "đui 3♠" (Chót còn 3♠) is intentionally skipped in judge mode per rule.

        return scores;
    }
}

public record PlayResult(Combo Played, bool PlayerFinished, bool RoundEnded, Match Match);
public record PassResult(bool NewTrick, bool RoundEnded, Match Match);
