using System.Collections.Concurrent;
using CutPig.GameEngine;

namespace CutPig.Services;

public class MatchManager
{
    private readonly ConcurrentDictionary<Guid, Match> _matchesByRoom = new();
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public static TimeSpan TurnTimeout { get; } = TimeSpan.FromSeconds(30);
    public static TimeSpan NextRoundDelay { get; } = TimeSpan.FromSeconds(20);
    public static TimeSpan WhiteWinChoiceTimeout { get; } = TimeSpan.FromSeconds(20);
    public static TimeSpan TrickCutTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan VoteResetTimeout { get; } = TimeSpan.FromSeconds(20);
    private const int VoteResetThreshold = 2; // số phiếu "Đồng ý" cần để chia bài lại
    public static TimeSpan FestivalRevealViewTimeout { get; } = TimeSpan.FromSeconds(5);  // xem bài sau khi lật hết
    public static TimeSpan FestivalAutoFlipTimeout { get; } = TimeSpan.FromSeconds(60);   // auto-lật nếu treo

    private object LockFor(Guid roomId) => _locks.GetOrAdd(roomId, _ => new object());

    public Match? GetByRoom(Guid roomId)
    {
        _matchesByRoom.TryGetValue(roomId, out var m);
        return m;
    }

    public Match Create(Guid roomId, Guid hostUserId, IReadOnlyList<(Guid UserId, string DisplayName, int SeatIndex, bool HasAvatar)> players, bool showOpponentCardCount = true)
    {
        lock (LockFor(roomId))
        {
            if (_matchesByRoom.TryGetValue(roomId, out var existing) && existing.Status != MatchStatus.Finished)
                return existing;

            var match = new Match { RoomId = roomId, HostUserId = hostUserId, ShowOpponentCardCount = showOpponentCardCount };
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
        // Round 1 luôn áp luật 3♠; round sau white-win cũng áp (carry-over qua flag).
        match.EnforceThreeSpadesOpening = isFirstRound || match.NextRoundOpensWithThreeSpades;
        match.NextRoundOpensWithThreeSpades = false;
        match.Status = MatchStatus.InProgress;
        match.CurrentTrick = null;
        match.CurrentTrickOwnerId = null;
        match.LastWonTrickCards = null;
        match.LastWonTrickWinnerId = null;
        match.FinishedCount = 0;
        match.FinishOrder.Clear();
        match.WhiteWinDeadline = null;
        match.TrickCutDeadline = null;
        match.VoteResetDeadline = null;
        match.VoteResetInitiatorId = null;
        match.PastFirstTrick = false;
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
            p.Surrendered = false;
            p.VoteResetChoice = null;
            p.FestivalWinner = false;
            p.FestivalRevealedIdx.Clear();
            // HasUsedVoteReset / HasUsedFestival KHÔNG reset ở đây: quyền là 1 lần / TRẬN (giữ qua các round),
            // chỉ false mặc định khi MatchPlayer được tạo trong Create.
        }
        match.FestivalRevealDeadline = null;
        match.FestivalAutoFlipDeadline = null;

        // Round lễ hội (Cào Rùa): tiêu cờ FestivalScheduled → round này là festival.
        match.IsFestivalRound = match.FestivalScheduled;
        match.FestivalScheduled = false;
        if (match.IsFestivalRound)
        {
            DealFestivalRound(match);
            return;
        }
        // Round thường: xoá người tổ chức lễ hội (chỉ giữ trong round festival để hiển thị).
        match.FestivalOrganizerId = null;

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

        SetupFirstTurn(match);
    }

    /// <summary>
    /// Deal round "Lễ hội" Cào Rùa: chia 3 lá/người, xác định người bài mạnh nhất (FestivalWinner),
    /// gán FinalRank theo độ mạnh (cho hiển thị/lịch sử), rồi chuyển sang WaitingNextRound — round này
    /// được resolve ngay, không có pha đánh bài. KHÔNG đụng PreviousRoundWinnerId (giữ người Nhất
    /// round trước-lễ-hội để đi đầu round Tiến Lên kế tiếp).
    /// </summary>
    private static void DealFestivalRound(Match match)
    {
        var deck = Deck.Shuffle(Deck.Build(), Random.Shared);
        int idx = 0;
        foreach (var p in match.Players)
        {
            for (int i = 0; i < 3 && idx < deck.Count; i++, idx++)
                p.Hand.Add(deck[idx]);
            p.Hand = p.Hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();
        }

        // Tìm độ mạnh cao nhất → mọi người đạt mức đó là winner (đồng hạng → chia đều pot khi tính điểm).
        var strengths = match.Players
            .Select(p => (Player: p, S: CaoRuaEngine.Strength(p.Hand)))
            .ToList();
        var best = strengths.Max(x => (x.S.Tier, x.S.Tiebreak));
        // Xếp FinalRank: winner = 1, còn lại = 2 (đồng hạng nhì) — chỉ để DTO/lịch sử có thứ tự.
        foreach (var (player, s) in strengths)
        {
            bool isWinner = (s.Tier, s.Tiebreak) == best;
            player.FestivalWinner = isWinner;
            player.FinalRank = isWinner ? 1 : 2;
            player.FestivalRevealedIdx.Clear();
        }

        // Vào pha nặn bài: mỗi người tự lật 3 lá của mình. Auto-lật sau 60s nếu treo.
        match.Status = MatchStatus.FestivalReveal;
        match.FestivalRevealDeadline = null;
        match.FestivalAutoFlipDeadline = DateTime.UtcNow + FestivalAutoFlipTimeout;
    }

    /// <summary>
    /// Player lật bài Cào Rùa của CHÍNH MÌNH. flipAll=true → lật cả 3 lá; ngược lại lật lá tại cardIndex
    /// (0..2, bất kỳ thứ tự nào). Trả về match đã cập nhật.
    /// </summary>
    public Match FlipFestivalCard(Guid roomId, Guid userId, bool flipAll, int cardIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.FestivalReveal)
                throw new InvalidOperationException("Không trong pha nặn bài lễ hội.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");

            if (flipAll)
            {
                for (int i = 0; i < player.Hand.Count; i++) player.FestivalRevealedIdx.Add(i);
            }
            else if (cardIndex >= 0 && cardIndex < player.Hand.Count)
            {
                player.FestivalRevealedIdx.Add(cardIndex);
            }
            CheckFestivalRevealComplete(match);
            return match;
        }
    }

    /// <summary>Khi mọi người đã lật hết → set deadline xem bài 5s (timer sẽ finalize → RoundEnd).</summary>
    private static void CheckFestivalRevealComplete(Match match)
    {
        bool allRevealed = match.Players.All(p => p.FestivalRevealedIdx.Count >= p.Hand.Count);
        if (allRevealed && match.FestivalRevealDeadline == null)
        {
            match.FestivalRevealDeadline = DateTime.UtcNow + FestivalRevealViewTimeout;
            match.FestivalAutoFlipDeadline = null;
        }
    }

    /// <summary>Timer: hết 60s mà chưa lật hết → tự lật toàn bộ rồi set deadline xem bài 5s.</summary>
    public Match? AutoFlipFestival(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.FestivalReveal) return null;
            foreach (var p in match.Players)
                for (int i = 0; i < p.Hand.Count; i++) p.FestivalRevealedIdx.Add(i);
            CheckFestivalRevealComplete(match);
            return match;
        }
    }

    /// <summary>Timer: hết 5s xem bài → resolve round lễ hội (chuyển WaitingNextRound để emit RoundEnd).</summary>
    public Match? FinalizeFestival(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.FestivalReveal) return null;
            match.FestivalRevealDeadline = null;
            match.FestivalAutoFlipDeadline = null;
            match.Status = MatchStatus.WaitingNextRound;
            match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
            return match;
        }
    }

    private static void SetupFirstTurn(Match match)
    {
        // Determine first turn
        int firstSeat;
        if (match.EnforceThreeSpadesOpening)
        {
            // Player holding 3 of Spades; nếu 3♠ rơi vào bài úp → seat 0
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
            // Chuyển sang InProgress: thiếu dòng này thì status kẹt ở WhiteWinChoice
            // → Play/Pass throw "Ván chưa bắt đầu" → game treo sau khi từ chối về trắng.
            match.Status = MatchStatus.InProgress;
            SetupFirstTurn(match);
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
        // Round sau white-win áp luật 3♠ đi đầu giống round 1
        match.NextRoundOpensWithThreeSpades = true;
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
            // Có nước đánh mới → ẩn thông báo "thắng vòng trước".
            match.LastWonTrickCards = null;
            match.LastWonTrickWinnerId = null;
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
                if (cards.Count == 1 && cards[0].Rank == 3 && cards[0].Suit == Suit.Spades)
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
                    if (p.Hand.Count == 1 && p.Hand[0].Rank == 3 && p.Hand[0].Suit == Suit.Spades)
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
        // Lưu lá thắng trick để client báo "ai thắng vòng bằng gì" trước khi mở nước mới.
        match.LastWonTrickCards = match.CurrentTrick?.Cards.ToList();
        match.LastWonTrickWinnerId = ownerId;
        match.CurrentTrick = null;
        match.CurrentTrickOwnerId = null;
        match.TrickCutDeadline = null;
        match.PendingTrickWinnerId = null;
        match.TrickCutCandidates.Clear();
        match.PastFirstTrick = true; // trick 1 vừa kết thúc → khoá vote chia bài lại
        match.Status = MatchStatus.InProgress;
        foreach (var p in match.Players) p.PassedThisTrick = false;
        var ownerSeat = match.Players.FindIndex(p => p.UserId == ownerId);
        // Người mở nước mới = người thắng trick (owner). Nếu owner đã hết bài → người active KẾ TIẾP
        // owner theo seat order (anchor vào ownerSeat trước khi advance, không phải từ lượt hiện tại).
        if (ownerSeat >= 0 && !match.Players[ownerSeat].FinalRank.HasValue)
        {
            match.CurrentTurnSeatIndex = ownerSeat;
        }
        else
        {
            match.CurrentTurnSeatIndex = ownerSeat;
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

            bool isMatchOpener = match.EnforceThreeSpadesOpening
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
            // Có nước đánh mới → ẩn thông báo "thắng vòng trước".
            match.LastWonTrickCards = null;
            match.LastWonTrickWinnerId = null;
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
                if (cards.Count == 1 && cards[0].Rank == 3 && cards[0].Suit == Suit.Spades)
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
                    if (p.Hand.Count == 1 && p.Hand[0].Rank == 3 && p.Hand[0].Suit == Suit.Spades)
                        p.StuckWithThreeOfSpades = true;
                }
                SettleTrickChopChain(match);
                match.Status = MatchStatus.WaitingNextRound;
                match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
                return new PlayResult(combo, justFinished, true, match);
            }

            // Nếu không còn active player nào chưa pass (mọi đối thủ active khác đã pass) → trick kết thúc ngay,
            // reset trick + clear pass flags để mọi người vào lại trick mới (đúng rule pass-tracking per-trick).
            // - Cutter chưa finish → lượt mở nước mới về cutter.
            // - Cutter vừa finish (đánh lá cuối) → lượt về active player kế tiếp theo seat order (không kẹt ở người đã hết bài).
            bool anyOtherActiveNotPassed = match.Players.Any(p =>
                p.UserId != userId
                && !p.FinalRank.HasValue
                && !p.PassedThisTrick);
            if (!anyOtherActiveNotPassed)
            {
                SettleTrickChopChain(match);
                // Lưu lá thắng trick để client báo "ai thắng vòng bằng gì" trước khi mở nước mới.
                match.LastWonTrickCards = match.CurrentTrick?.Cards.ToList();
                match.LastWonTrickWinnerId = userId;
                match.CurrentTrick = null;
                match.CurrentTrickOwnerId = null;
                foreach (var p in match.Players) p.PassedThisTrick = false;
                var cutterSeat = match.Players.FindIndex(p => p.UserId == userId);
                match.CurrentTurnSeatIndex = cutterSeat;
                if (justFinished)
                {
                    // Cutter đã hết bài → trao lượt cho active player kế tiếp.
                    AdvanceTurnSkippingPassed(match);
                }
                match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
                return new PlayResult(combo, justFinished, false, match);
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
                    // Có nước đánh mới → ẩn thông báo "thắng vòng trước".
                    match.LastWonTrickCards = null;
                    match.LastWonTrickWinnerId = null;
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
                            if (p.Hand.Count == 1 && p.Hand[0].Rank == 3 && p.Hand[0].Suit == Suit.Spades)
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
                // Chỉ mở window "Chặn?" nếu combo thắng trick là thứ 4-đôi-thông có thể chặt
                // (con 2, đôi 2, 3 đôi thông, tứ quý non-2, 4 đôi thông nhỏ hơn). Nếu trick thắng
                // bằng combo khác (vd sảnh, đôi thường) → 4-đôi-thông không làm gì được, skip popup.
                var ownerId = match.CurrentTrickOwnerId.Value;
                var cutCandidates = match.CurrentTrick != null
                    && TienLenComboEngine.IsBeatableByFourPairRun(match.CurrentTrick)
                    ? match.Players
                        .Where(p => p.UserId != ownerId
                            && !p.FinalRank.HasValue
                            && TienLenComboEngine.HasFourPairRunInHand(p.Hand))
                        .Select(p => p.UserId)
                        .ToList()
                    : new List<Guid>();

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
                    // Lưu lá thắng trick để client báo "ai thắng vòng bằng gì" trước khi mở nước mới.
                    match.LastWonTrickCards = match.CurrentTrick?.Cards.ToList();
                    match.LastWonTrickWinnerId = ownerId;
                    match.CurrentTrick = null;
                    match.CurrentTrickOwnerId = null;
                    match.PastFirstTrick = true; // trick 1 vừa kết thúc → khoá vote chia bài lại
                    foreach (var p in match.Players) p.PassedThisTrick = false;
                    var ownerSeat = match.Players.FindIndex(p => p.UserId == ownerId);
                    // Người mở nước mới = người thắng trick (owner). Nếu owner đã hết bài → người
                    // active KẾ TIẾP owner theo seat order (KHÔNG phải kế tiếp người vừa pass cuối cùng).
                    if (ownerSeat >= 0 && !match.Players[ownerSeat].FinalRank.HasValue)
                    {
                        match.CurrentTurnSeatIndex = ownerSeat;
                    }
                    else
                    {
                        match.CurrentTurnSeatIndex = ownerSeat;
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
    /// Player tự nguyện đầu hàng: bị gán hạng chót còn trống thấp nhất (n, rồi n-1 cho người đầu hàng sau),
    /// bài giữ nguyên để tính held penalty như về chót bình thường. Ván tiếp tục cho người còn lại.
    /// KHÔNG tăng FinishedCount (người về Nhất/Nhì... vẫn chiếm hạng trên qua FinishedCount).
    /// </summary>
    public PassResult Surrender(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.FinalRank.HasValue)
                throw new InvalidOperationException("Bạn đã hết bài / đã có thứ hạng.");

            int n = match.Players.Count;
            int surrenderedBefore = match.Players.Count(p => p.Surrendered);
            player.Surrendered = true;
            player.FinalRank = n - surrenderedBefore; // người đầu hàng đầu tiên = chót (n), sau = n-1...
            player.PassedThisTrick = false;
            match.FinishOrder.Add(userId);

            bool wasCurrentTurn = match.CurrentTurnSeatIndex == player.SeatIndex;

            if (match.CurrentTrickOwnerId == userId && match.CurrentTrick != null)
            {
                // Người đầu hàng đang giữ trick (vừa thắng vòng, đến lượt mở nước) → reset trick,
                // trao lượt mở nước cho người active kế tiếp.
                SettleTrickChopChain(match);
                match.LastWonTrickCards = match.CurrentTrick.Cards.ToList();
                match.LastWonTrickWinnerId = null;
                match.CurrentTrick = null;
                match.CurrentTrickOwnerId = null;
                match.PastFirstTrick = true;
                foreach (var p in match.Players) p.PassedThisTrick = false;
                AdvanceTurnSkippingPassed(match);
                match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
            }
            else if (wasCurrentTurn)
            {
                // Đến lượt người đầu hàng (giữa trick) → bỏ qua, trao lượt cho người active kế tiếp.
                AdvanceTurnSkippingPassed(match);
                match.TurnDeadline = DateTime.UtcNow + TurnTimeout;

                // Corner case: mọi người active còn lại đều đã pass → trick reset về owner (nếu owner còn bài).
                var curr = match.Players[match.CurrentTurnSeatIndex];
                bool noActiveMover = curr.FinalRank.HasValue || curr.PassedThisTrick;
                if (noActiveMover && match.CurrentTrick != null && match.CurrentTrickOwnerId.HasValue)
                {
                    var ownerId = match.CurrentTrickOwnerId.Value;
                    SettleTrickChopChain(match);
                    match.LastWonTrickCards = match.CurrentTrick.Cards.ToList();
                    match.LastWonTrickWinnerId = ownerId;
                    match.CurrentTrick = null;
                    match.CurrentTrickOwnerId = null;
                    match.PastFirstTrick = true;
                    foreach (var p in match.Players) p.PassedThisTrick = false;
                    var ownerSeat = match.Players.FindIndex(p => p.UserId == ownerId);
                    match.CurrentTurnSeatIndex = ownerSeat;
                    if (ownerSeat < 0 || match.Players[ownerSeat].FinalRank.HasValue)
                        AdvanceTurnSkippingPassed(match);
                    match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
                }
            }

            // Kết thúc ván nếu chỉ còn ≤1 người chưa có thứ hạng.
            var remaining = match.Players.Where(p => !p.FinalRank.HasValue).ToList();
            if (remaining.Count <= 1)
            {
                foreach (var p in remaining)
                {
                    match.FinishedCount++;
                    p.FinalRank = match.FinishedCount;
                    match.FinishOrder.Add(p.UserId);
                    if (p.Hand.Count == 1 && p.Hand[0].Rank == 3 && p.Hand[0].Suit == Suit.Spades)
                        p.StuckWithThreeOfSpades = true;
                }
                SettleTrickChopChain(match);
                match.Status = MatchStatus.WaitingNextRound;
                match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
                return new PassResult(false, true, match);
            }
            return new PassResult(false, false, match);
        }
    }

    /// <summary>
    /// Bất kỳ player nào mở vote chia bài lại — chỉ khi đang trick 1 (chưa qua trick thứ 2) và chưa
    /// có ai về. Initiator tự động tính 1 phiếu "Đồng ý". Đủ 2 phiếu là chia lại.
    /// </summary>
    public VoteResetResult StartVoteReset(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.PastFirstTrick)
                throw new InvalidOperationException("Đã qua trick 1, không thể vote chia bài lại.");
            if (match.FinishedCount > 0 || match.Players.Any(p => p.FinalRank.HasValue))
                throw new InvalidOperationException("Đã có người về, không thể vote chia bài lại.");
            var initiator = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (initiator.HasUsedVoteReset)
                throw new InvalidOperationException("Bạn đã dùng quyền vote chia bài lại trong ván này.");

            match.Status = MatchStatus.VoteReset;
            match.VoteResetInitiatorId = userId;
            match.VoteResetDeadline = DateTime.UtcNow + VoteResetTimeout;
            foreach (var p in match.Players) p.VoteResetChoice = null;
            // Initiator tự động đồng ý + tiêu quyền.
            initiator.VoteResetChoice = true;
            initiator.HasUsedVoteReset = true;
            bool dealt = TryResolveVoteReset(match);
            return new VoteResetResult(match, dealt);
        }
    }

    /// <summary>Player bỏ phiếu trong phase VoteReset. Mỗi người 1 phiếu/ván.</summary>
    public VoteResetResult RespondVoteReset(Guid roomId, Guid userId, bool accept)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (match.Status != MatchStatus.VoteReset)
                throw new InvalidOperationException("Không trong lúc vote chia bài lại.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.VoteResetChoice.HasValue)
                throw new InvalidOperationException("Bạn đã bỏ phiếu rồi.");

            player.VoteResetChoice = accept;
            // KHÔNG tiêu quyền của người chỉ bỏ phiếu (kể cả "Đồng ý") — chỉ NGƯỜI MỞ VOTE (initiator)
            // mới mất quyền. Người respond vẫn được tự mở vote của mình sau này.
            bool dealt = TryResolveVoteReset(match);
            return new VoteResetResult(match, dealt);
        }
    }

    /// <summary>Timer service gọi khi VoteResetDeadline qua — treat phiếu chưa bỏ là "Bỏ".</summary>
    public VoteResetResult? ResolveVoteResetTimeout(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)) return null;
            if (match.Status != MatchStatus.VoteReset) return null;
            foreach (var p in match.Players.Where(p => !p.VoteResetChoice.HasValue))
                p.VoteResetChoice = false;
            bool dealt = TryResolveVoteReset(match);
            return new VoteResetResult(match, dealt);
        }
    }

    /// <summary>Returns true nếu vote vừa giải quyết bằng cách chia bài lại (hub cần re-broadcast hand).</summary>
    private static bool TryResolveVoteReset(Match match)
    {
        int yes = match.Players.Count(p => p.VoteResetChoice == true);
        int decided = match.Players.Count(p => p.VoteResetChoice.HasValue);

        if (yes >= VoteResetThreshold)
        {
            // Đủ phiếu → chia bài lại CÙNG round number (giữ nguyên luật mở nước của round này).
            int keepRound = match.RoundNumber;
            bool keepEnforce3S = match.EnforceThreeSpadesOpening;
            bool keepFestivalScheduled = match.FestivalScheduled; // vote-reset KHÔNG biến round hiện tại thành lễ hội
            match.VoteResetDeadline = null;
            match.VoteResetInitiatorId = null;
            DealRound(match, isFirstRound: false);
            match.FestivalScheduled = keepFestivalScheduled;     // hoàn lại lịch lễ hội cho round SAU
            match.RoundNumber = keepRound;                       // DealRound đã +1, hoàn lại để không nhảy số ván
            match.EnforceThreeSpadesOpening = keepEnforce3S;     // giữ luật 3♠ nếu đây là round 1 / sau white-win
            // Nếu cần ép 3♠ mà bài mới không phải white-win, re-run SetupFirstTurn để chọn đúng người cầm 3♠
            // (DealRound đã set turn theo PreviousRoundWinnerId vì isFirstRound=false).
            if (keepEnforce3S && match.Status == MatchStatus.InProgress) SetupFirstTurn(match);
            return true;
        }

        // Chưa đủ phiếu nhưng vẫn còn người chưa bỏ → chờ tiếp.
        if (decided < match.Players.Count) return false;

        // Tất cả đã bỏ mà không đủ → huỷ vote, chơi tiếp như cũ.
        match.VoteResetDeadline = null;
        match.VoteResetInitiatorId = null;
        match.Status = MatchStatus.InProgress;
        match.TurnDeadline = DateTime.UtcNow + TurnTimeout;
        return false;
    }

    /// <summary>
    /// Player "Tổ chức lễ hội": đánh dấu round KẾ TIẾP là Cào Rùa. Bất kỳ lúc nào trong round đang chơi.
    /// Chỉ 1 người/round được đặt (FestivalScheduled), mỗi người 1 lần/TRẬN (HasUsedFestival).
    /// Round hiện tại vẫn chơi bình thường đến hết.
    /// </summary>
    public Match ScheduleFestival(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.IsFestivalRound)
                throw new InvalidOperationException("Đang trong round lễ hội rồi.");
            if (match.FestivalScheduled)
                throw new InvalidOperationException("Đã có người tổ chức lễ hội cho round sau.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.HasUsedFestival)
                throw new InvalidOperationException("Bạn đã dùng quyền tổ chức lễ hội trong trận này.");

            match.FestivalScheduled = true;
            match.FestivalOrganizerId = userId;
            player.HasUsedFestival = true;
            return match;
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
            // Case C: victims share the last rank; pardoned continue playing normally.
            // KHÔNG tăng FinishedCount cho victim — victim bị ghim ở hạng chót, còn pardoned mới là
            // người "về tiếp theo" nên phải chiếm các hạng 2,3,... Nếu cộng FinishedCount ở đây thì
            // pardoned về sau bị đẩy hạng sai (bug: pardoned về Nhì lại tính thành Ba).
            int lastRank = match.Players.Count;
            foreach (var v in victims)
            {
                v.FinalRank = lastRank;
                match.FinishOrder.Add(v.UserId);
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
            // Rule: chặt heo bằng "đơn thuần" (single 2 chặn single 2) không tính điểm.
            // Chỉ tính khi cutter cuối dùng combo lớn (đôi 2, sám 2, tứ quý, 3-đôi-thông, 4-đôi-thông).
            if (last.Kind == ComboKind.Single)
            {
                chain.Clear();
                return;
            }
            // Rule: người bị chặt cuối (second-to-last) đã HẾT BÀI (đã có thứ hạng — Nhất/Nhì/Ba bất kỳ)
            // thì không phải trả tiền chặt — không còn ai để đòi pot. Vd P1 đánh 2♠ rồi hết bài, P2 pass,
            // P3 chặt 2♠ bằng 3-đôi-thông → P3 không ăn gì (second-to-last = P1 đã về). Nhưng nếu second-to-last
            // còn bài (chưa về) thì vẫn gánh toàn bộ pot chain[0..^1], kể cả phần heo của người đã hết bài.
            var secondLastPlayer = match.Players.FirstOrDefault(p => p.UserId == secondLast.PlayerId);
            if (secondLastPlayer != null && secondLastPlayer.FinalRank.HasValue)
            {
                chain.Clear();
                return;
            }
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
            match.TrickChopChain.Add((playerId, value, combo.Kind));
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

    public IEnumerable<Match> AllVoteReset() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.VoteReset);

    public IEnumerable<Match> AllFestivalReveal() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.FestivalReveal);

    public int[] ComputeRoundScores(Match match)
    {
        // Returns score for each player in seat order
        var n = match.Players.Count;
        var scores = new int[n];

        // Lễ hội (Cào Rùa): mỗi loser -2, pot = 2×(số loser) chia đều cho winner(s). Zero-sum.
        if (match.IsFestivalRound)
        {
            int winnerCnt = match.Players.Count(p => p.FestivalWinner);
            int loserCnt = n - winnerCnt;
            if (winnerCnt > 0 && loserCnt > 0)
            {
                int pot = 2 * loserCnt;
                int perWinner = pot / winnerCnt;
                int rem = pot % winnerCnt;
                int wi = 0;
                for (int i = 0; i < n; i++)
                {
                    if (match.Players[i].FestivalWinner)
                        scores[i] = perWinner + (wi++ < rem ? 1 : 0);
                    else
                        scores[i] = -2;
                }
            }
            // winnerCnt == n (mọi người đồng hạng) → hoà, scores giữ 0.
            return scores;
        }

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

        // Chót còn held: người Chót (FinalRank == n) còn heo / tứ quý / 3-đôi-thông / 4-đôi-thông trong tay
        // → Chót -held, người về kế trên (FinalRank == n-1) +held. Zero-sum giữa 2 người.
        var chot = match.Players.FirstOrDefault(p => p.FinalRank == n);
        if (chot != null)
        {
            int held = TienLenComboEngine.ComputeHeldValue(chot.Hand);
            if (held > 0)
            {
                var above = match.Players.FirstOrDefault(p => p.FinalRank == n - 1);
                if (above != null)
                {
                    int chotIdx = match.Players.IndexOf(chot);
                    int aboveIdx = match.Players.IndexOf(above);
                    scores[chotIdx] -= held;
                    scores[aboveIdx] += held;
                }
            }
        }

        return scores;
    }

    /// <summary>Read-only snapshot of per-player chop-pig deltas for the current round (for DTOs).</summary>
    public IReadOnlyDictionary<Guid, int> GetRoundChopExtras(Match match) => match.RoundChopExtra;

    /// <summary>
    /// Tính điểm round, cộng vào TotalScore, build RoundEndDto và append vào RoundHistory.
    /// Dùng chung cho RoomHub.EmitRoundEndAsync và MatchTimerService.EmitRoundEndAsync để tránh lệch logic.
    /// (Idempotent KHÔNG đảm bảo — gọi đúng 1 lần mỗi khi round kết thúc.)
    /// </summary>
    public Dtos.RoundEndDto BuildRoundEndDto(Match match)
    {
        var roundScores = ComputeRoundScores(match);
        var breakdowns = ComputeRoundScoreBreakdowns(match);
        var chopExtras = match.RoundChopExtra;
        bool wasWhiteWin = match.Players.Any(p => p.WhiteWinReason != null);

        for (int i = 0; i < match.Players.Count; i++)
            match.Players[i].TotalScore += roundScores[i];

        var entries = match.Players
            .OrderBy(p => p.FinalRank ?? int.MaxValue)
            .Select(p =>
            {
                int idx = match.Players.IndexOf(p);
                int chop = chopExtras.TryGetValue(p.UserId, out var v) ? v : 0;
                var bd = breakdowns[idx];
                var held = TienLenComboEngine.ComputeHeldBreakdown(p.Hand);
                var heldDetails = TienLenComboEngine.ComputeHeldDetails(p.Hand)
                    .Select(d => new Dtos.HeldDetailDto(d.Label, d.Value)).ToList();
                List<Dtos.CardDto>? festCards = match.IsFestivalRound
                    ? p.Hand.Select(c => new Dtos.CardDto(c.Rank, (int)c.Suit)).ToList()
                    : null;
                string? festLabel = match.IsFestivalRound ? CaoRuaEngine.Label(p.Hand) : null;
                return new Dtos.RoundResultEntryDto(
                    p.UserId, p.DisplayName,
                    p.FinalRank ?? 0,
                    roundScores[idx],
                    p.TotalScore,
                    p.WhiteWinReason,
                    chop,
                    p.FinishedWithThreeOfSpades,
                    p.StuckWithThreeOfSpades,
                    p.JudgeIsWinner,
                    p.JudgeIsVictim,
                    p.JudgeIsPardoned,
                    p.JudgeHeldValue,
                    bd.BaseRank,
                    bd.ThreeOfSpades,
                    bd.Judge,
                    bd.WhiteWin,
                    bd.HeldPenalty,
                    new Dtos.HeldItemsDto(held.BlackPigs, held.RedPigs, held.HasFourOfAKind, held.HasThreePairRun, held.HasFourPairRun),
                    heldDetails,
                    match.IsFestivalRound ? bd.Festival : 0,
                    p.FestivalWinner,
                    festCards,
                    festLabel);
            })
            .ToList();

        var dto = new Dtos.RoundEndDto(match.Id, match.RoundNumber, wasWhiteWin, match.JudgeTriggered, entries, match.IsFestivalRound);
        match.RoundHistory.Add(dto);
        return dto;
    }

    public record RoundScoreBreakdown(int BaseRank, int Chop, int ThreeOfSpades, int Judge, int WhiteWin, int HeldPenalty, int Total, int Festival = 0);

    /// <summary>Per-player breakdown of the round score by component (for UI display).</summary>
    public RoundScoreBreakdown[] ComputeRoundScoreBreakdowns(Match match)
    {
        int n = match.Players.Count;
        var result = new RoundScoreBreakdown[n];

        // Lễ hội (Cào Rùa): toàn bộ điểm vào component Festival.
        if (match.IsFestivalRound)
        {
            var fest = ComputeRoundScores(match);
            for (int i = 0; i < n; i++)
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, 0, 0, fest[i], fest[i]);
            return result;
        }

        var winnerCount = match.Players.Count(p => p.WhiteWinReason != null);
        if (winnerCount > 0)
        {
            int loserCount = n - winnerCount;
            int perWinner = 2 * loserCount;
            int perLoser = -2 * winnerCount;
            for (int i = 0; i < n; i++)
            {
                int v = match.Players[i].WhiteWinReason != null ? perWinner : perLoser;
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, v, 0, v);
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
                result[i] = new RoundScoreBreakdown(0, 0, threeBonus, judgePart, 0, 0, judgeScores[i]);
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

        var heldPenalty = new int[n];
        var chot = match.Players.FirstOrDefault(p => p.FinalRank == n);
        if (chot != null)
        {
            int held = TienLenComboEngine.ComputeHeldValue(chot.Hand);
            if (held > 0)
            {
                var above = match.Players.FirstOrDefault(p => p.FinalRank == n - 1);
                if (above != null)
                {
                    int chotIdx = match.Players.IndexOf(chot);
                    int aboveIdx = match.Players.IndexOf(above);
                    heldPenalty[chotIdx] -= held;
                    heldPenalty[aboveIdx] += held;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            int total = baseRank[i] + chop[i] + three[i] + heldPenalty[i];
            result[i] = new RoundScoreBreakdown(baseRank[i], chop[i], three[i], 0, 0, heldPenalty[i], total);
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

        // Áp chop-pig settlements cho mọi case (A/B/C). Chain đã zero-sum theo cặp nên cộng tất cả
        // entries (winner / pardoned / victim) giữ tổng zero-sum xuyên suốt.
        for (int i = 0; i < n; i++)
        {
            var pid = match.Players[i].UserId;
            if (match.RoundChopExtra.TryGetValue(pid, out var chop))
                scores[i] += chop;
        }

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

            // (Chop-pig đã được apply ở khối chung trên, không lặp lại.)

            // Pardoned chót còn held (heo / 3-đôi / tứ quý / 4-đôi) → -held, mỗi pardoned khác chia đều +held
            // (zero-sum trong nhóm pardoned). Held=0 không phạt thêm.
            var lastPardoned = ordered[^1];
            int lastHeld = TienLenComboEngine.ComputeHeldValue(lastPardoned.Hand);
            if (lastHeld > 0)
            {
                int lastIdx = match.Players.IndexOf(lastPardoned);
                scores[lastIdx] -= lastHeld;
                var others = pardoned.Where(p => p.UserId != lastPardoned.UserId).ToList();
                if (others.Count > 0)
                {
                    int share = lastHeld / others.Count;
                    int remainder = lastHeld % others.Count;
                    for (int k = 0; k < others.Count; k++)
                    {
                        int idx = match.Players.IndexOf(others[k]);
                        scores[idx] += share + (k < remainder ? 1 : 0);
                    }
                }
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

        return scores;
    }
}

public record PlayResult(Combo Played, bool PlayerFinished, bool RoundEnded, Match Match);
public record PassResult(bool NewTrick, bool RoundEnded, Match Match);
public record VoteResetResult(Match Match, bool Dealt);
