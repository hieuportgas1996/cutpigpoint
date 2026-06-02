using System.Collections.Concurrent;
using CutPig.GameEngine;

namespace CutPig.Services;

public class MatchManager
{
    private readonly ConcurrentDictionary<Guid, Match> _matchesByRoom = new();
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public static TimeSpan TurnTimeout { get; } = TimeSpan.FromSeconds(30);
    public static TimeSpan NextRoundDelay { get; } = TimeSpan.FromSeconds(20);
    public static TimeSpan WhiteWinChoiceTimeout { get; } = TimeSpan.FromSeconds(60); // cửa sổ về trắng trong trick 1
    public static TimeSpan TrickCutTimeout { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan VoteResetTimeout { get; } = TimeSpan.FromSeconds(20);
    private const int VoteResetThreshold = 2; // số phiếu "Đồng ý" cần để chia bài lại
    public static TimeSpan FestivalRevealViewTimeout { get; } = TimeSpan.FromSeconds(5);  // xem bài sau khi lật hết
    public static TimeSpan FestivalAutoFlipTimeout { get; } = TimeSpan.FromSeconds(60);   // auto-lật nếu treo
    public static TimeSpan XiDachTurnTimeout { get; } = TimeSpan.FromSeconds(60);          // 60s/lượt rút bài xì dách

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
        match.RoundChopDetails.Clear();
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
            p.IsStarOfHope = false;
            p.IsXiDachDealer = false;
            p.XiDachStood = false;
            p.XiDachSettled = false;
            p.XiDachDelta = 0;
            p.XiDachRevealed = false;
            // HasUsedVoteReset / HasUsedFestival / HasUsedStarOfHope / HasUsedXiDach KHÔNG reset ở đây:
            // quyền là 1 lần / TRẬN (giữ qua các round), chỉ false mặc định khi MatchPlayer tạo trong Create.
        }
        match.FestivalRevealDeadline = null;
        match.FestivalAutoFlipDeadline = null;
        match.XiDachDealerId = null;
        match.XiDachTurnUserId = null;
        match.XiDachTurnDeadline = null;

        // Ngôi Sao Hi Vọng: tiêu cờ đã đặt lịch round trước → round NÀY người đó là star (điểm giao dịch ×2).
        // Áp cho cả round thường lẫn round lễ hội.
        if (match.StarOfHopeScheduledUserId is Guid starId)
        {
            var star = match.Players.FirstOrDefault(p => p.UserId == starId);
            if (star != null) star.IsStarOfHope = true;
            match.StarOfHopeScheduledUserId = null;
        }

        // Round Sát Phạt (Xì Dách): tiêu cờ XiDachScheduledUserId → round này là xì dách, người đó là Nhà Cái.
        match.IsXiDachRound = match.XiDachScheduledUserId.HasValue;
        if (match.IsXiDachRound)
        {
            DealXiDachRound(match, match.XiDachScheduledUserId!.Value);
            match.XiDachScheduledUserId = null;
            return;
        }

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

        // Rule mới: KHÔNG dừng game chờ chọn. Round chơi bình thường ngay; người có bộ về trắng
        // được bấm "Về trắng" bất kỳ lúc nào TRONG TRICK 1 (chưa qua trick 2) và trong 60s.
        // Hết trick 1 / hết 60s → cửa sổ đóng (CloseWhiteWinWindow xoá WhiteWinReason).
        if (anyWhiteWin)
            match.WhiteWinDeadline = DateTime.UtcNow + WhiteWinChoiceTimeout;

        SetupFirstTurn(match);
    }

    /// <summary>Đóng cửa sổ về trắng (hết trick 1 hoặc hết 60s): xoá mọi WhiteWinReason chưa được chốt.</summary>
    private static void CloseWhiteWinWindow(Match match)
    {
        if (match.WhiteWinDeadline == null) return;
        foreach (var p in match.Players)
        {
            p.WhiteWinReason = null;
            p.WhiteWinAccepted = null;
        }
        match.WhiteWinDeadline = null;
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

    // ==================== Xì Dách (Sát Phạt) ====================

    /// <summary>
    /// Deal round Sát Phạt (Xì Dách): chia 2 lá/người, dealerId làm Nhà Cái. Vào pha rút bài tuần tự.
    /// Nếu Nhà Cái có Xì Dách/Xì Vàng ngay từ 2 lá → lật hết, ăn toàn bộ → kết thúc round luôn.
    /// Nếu Nhà Cái KHÔNG đặc biệt → bắt đầu lượt rút từ player đầu tiên (không phải nhà cái).
    /// KHÔNG đụng PreviousRoundWinnerId (giữ người Nhất round trước để đi đầu round TLMN kế tiếp).
    /// </summary>
    private static void DealXiDachRound(Match match, Guid dealerId)
    {
        var deck = Deck.Shuffle(Deck.Build(), Random.Shared);
        int idx = 0;
        foreach (var p in match.Players)
        {
            p.Hand.Clear();
            for (int i = 0; i < 2 && idx < deck.Count; i++, idx++) p.Hand.Add(deck[idx]);
            p.IsXiDachDealer = (p.UserId == dealerId);
        }
        match.XiDachDealerId = dealerId;
        match.Status = MatchStatus.XiDachPlaying;

        var dealer = match.Players.First(p => p.UserId == dealerId);

        // Nhà Cái đặc biệt sớm (Xì Dách / Xì Vàng) → lật hết, ăn toàn bộ player → kết thúc round.
        var dealerKind = XiDachEngine.Classify(dealer.Hand);
        if (dealerKind is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang)
        {
            foreach (var p in match.Players) p.XiDachRevealed = true;
            SettleAllXiDachPairs(match);
            EndXiDachRound(match);
            return;
        }

        // Players đặc biệt sớm (Xì Dách / Xì Vàng) → chốt cặp ngay, không cần rút.
        foreach (var p in match.Players.Where(p => !p.IsXiDachDealer))
        {
            var k = XiDachEngine.Classify(p.Hand);
            if (k is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang)
                SettleXiDachPair(match, dealer, p);
        }

        // Bắt đầu lượt rút: player đầu tiên (seat order) chưa chốt; nếu hết → tới nhà cái.
        AdvanceXiDachTurn(match, startFromBeginning: true);
    }

    /// <summary>Chốt điểm 1 cặp Nhà Cái ↔ player (lật + tính delta zero-sum). Idempotent: bỏ qua nếu đã chốt.</summary>
    private static void SettleXiDachPair(Match match, MatchPlayer dealer, MatchPlayer player)
    {
        if (player.XiDachSettled || player.IsXiDachDealer) return;
        int delta = XiDachEngine.ComparePlayerDelta(dealer.Hand, player.Hand);
        player.XiDachDelta = delta;
        dealer.XiDachDelta -= delta; // zero-sum
        player.XiDachSettled = true;
        player.XiDachRevealed = true;
        dealer.XiDachRevealed = true;
    }

    /// <summary>Chốt tất cả cặp chưa chốt (dùng khi nhà cái đặc biệt sớm hoặc kết thúc round).</summary>
    private static void SettleAllXiDachPairs(Match match)
    {
        var dealer = match.Players.First(p => p.IsXiDachDealer);
        foreach (var p in match.Players.Where(p => !p.IsXiDachDealer && !p.XiDachSettled))
            SettleXiDachPair(match, dealer, p);
    }

    /// <summary>
    /// Chuyển lượt rút xì dách sang người kế tiếp CHƯA dừng/chốt (players trước, nhà cái sau cùng).
    /// startFromBeginning=true → quét từ player đầu (sau khi deal). Khi không còn player nào cần rút
    /// → tới lượt nhà cái. Khi nhà cái cũng xong → sang pha so điểm (XiDachCompare).
    /// </summary>
    private static void AdvanceXiDachTurn(Match match, bool startFromBeginning)
    {
        // Người cần rút = chưa chốt cặp (player), chưa dừng, chưa quắc, chưa đủ 5 lá đặc biệt.
        bool NeedsTurn(MatchPlayer p)
        {
            if (p.XiDachSettled) return false;            // đã chốt (đặc biệt sớm)
            if (p.XiDachStood) return false;              // đã dừng
            var k = XiDachEngine.Classify(p.Hand);
            if (k is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang) return false;
            if (XiDachEngine.IsBust(p.Hand)) return false; // quắc rồi → không tới lượt nữa
            return true;
        }

        // Players (không phải nhà cái) theo thứ tự seat.
        var players = match.Players.Where(p => !p.IsXiDachDealer).OrderBy(p => p.SeatIndex).ToList();
        var nextPlayer = players.FirstOrDefault(NeedsTurn);
        if (nextPlayer != null)
        {
            SetXiDachTurn(match, nextPlayer);
            return;
        }

        // Hết players → tới nhà cái nếu nhà cái còn cần rút.
        var dealer = match.Players.First(p => p.IsXiDachDealer);
        if (NeedsTurn(dealer))
        {
            SetXiDachTurn(match, dealer);
            return;
        }

        // Mọi người đã chốt/dừng/quắc → sang pha so điểm.
        EnterXiDachCompare(match);
    }

    private static void SetXiDachTurn(Match match, MatchPlayer p)
    {
        match.XiDachTurnUserId = p.UserId;
        match.XiDachTurnDeadline = DateTime.UtcNow + XiDachTurnTimeout;
        match.Status = MatchStatus.XiDachPlaying;
    }

    /// <summary>Sang pha so điểm: nhà cái lần lượt bấm "So" từng player còn lại. Nếu không còn ai → kết thúc.</summary>
    private static void EnterXiDachCompare(Match match)
    {
        match.XiDachTurnUserId = null;
        match.XiDachTurnDeadline = null;
        bool anyUnsettled = match.Players.Any(p => !p.IsXiDachDealer && !p.XiDachSettled);
        if (!anyUnsettled)
        {
            EndXiDachRound(match);
            return;
        }
        match.Status = MatchStatus.XiDachCompare;
    }

    /// <summary>Kết thúc round xì dách: chốt nốt cặp còn lại, gán delta, sang WaitingNextRound.</summary>
    private static void EndXiDachRound(Match match)
    {
        SettleAllXiDachPairs(match);
        foreach (var p in match.Players) p.XiDachRevealed = true;
        match.XiDachTurnUserId = null;
        match.XiDachTurnDeadline = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>Player/nhà cái rút thêm 1 lá. Validate: đúng lượt, đang pha rút, chưa quắc, chưa đủ 5 lá.</summary>
    public Match DrawXiDachCard(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.XiDachPlaying)
                throw new InvalidOperationException("Không trong pha rút bài xì dách.");
            if (match.XiDachTurnUserId != userId)
                throw new InvalidOperationException("Chưa tới lượt bạn.");
            var p = match.Players.First(x => x.UserId == userId);
            if (p.Hand.Count >= XiDachEngine.MaxCards)
                throw new InvalidOperationException("Đã đủ 5 lá, không rút thêm.");

            // Rút 1 lá ngẫu nhiên từ phần còn lại của bộ (loại các lá đã chia).
            DrawOneCard(match, p);

            // Sau khi rút: nếu quắc / đủ 5 lá → tự kết thúc lượt người này.
            if (XiDachEngine.IsBust(p.Hand) || p.Hand.Count >= XiDachEngine.MaxCards
                || XiDachEngine.Classify(p.Hand) is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang)
            {
                AdvanceXiDachTurn(match, startFromBeginning: false);
            }
            else
            {
                // Còn rút tiếp được → reset deadline cho cùng người.
                match.XiDachTurnDeadline = DateTime.UtcNow + XiDachTurnTimeout;
            }
            return match;
        }
    }

    /// <summary>Player/nhà cái "dừng" rút. Validate: đúng lượt, được phép dừng (đạt ngưỡng).</summary>
    public Match StandXiDach(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.XiDachPlaying)
                throw new InvalidOperationException("Không trong pha rút bài xì dách.");
            if (match.XiDachTurnUserId != userId)
                throw new InvalidOperationException("Chưa tới lượt bạn.");
            var p = match.Players.First(x => x.UserId == userId);
            if (!XiDachEngine.CanStand(p.Hand, p.IsXiDachDealer))
                throw new InvalidOperationException(p.IsXiDachDealer
                    ? "Nhà cái phải đạt 15 điểm mới được dừng."
                    : "Phải đạt 16 điểm mới được dừng.");
            p.XiDachStood = true;
            AdvanceXiDachTurn(match, startFromBeginning: false);
            return match;
        }
    }

    /// <summary>Nhà cái bấm "So" với 1 player trong pha XiDachCompare → chốt cặp đó. Hết người → kết thúc round.</summary>
    public Match CompareXiDachPlayer(Guid roomId, Guid dealerUserId, Guid targetUserId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.XiDachCompare)
                throw new InvalidOperationException("Không trong pha so điểm xì dách.");
            if (match.XiDachDealerId != dealerUserId)
                throw new InvalidOperationException("Chỉ Nhà Cái được so điểm.");
            var dealer = match.Players.First(p => p.IsXiDachDealer);
            var target = match.Players.FirstOrDefault(p => p.UserId == targetUserId && !p.IsXiDachDealer)
                ?? throw new InvalidOperationException("Không tìm thấy người chơi để so.");
            if (target.XiDachSettled)
                throw new InvalidOperationException("Đã so người này rồi.");

            SettleXiDachPair(match, dealer, target);

            // Hết người chưa so → kết thúc round.
            if (!match.Players.Any(p => !p.IsXiDachDealer && !p.XiDachSettled))
                EndXiDachRound(match);
            return match;
        }
    }

    /// <summary>Timer: hết 60s lượt rút → tự xử (rút nếu buộc rút; dừng nếu được phép; nếu không thì rút).</summary>
    public Match? AutoAdvanceXiDach(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.XiDachPlaying) return null;
            if (match.XiDachTurnUserId is not Guid uid) return null;
            var p = match.Players.First(x => x.UserId == uid);
            if (XiDachEngine.CanStand(p.Hand, p.IsXiDachDealer))
                p.XiDachStood = true;                       // được dừng → auto dừng
            else if (p.Hand.Count < XiDachEngine.MaxCards)
                DrawOneCard(match, p);                       // buộc rút → auto rút 1 lá
            AdvanceXiDachTurn(match, startFromBeginning: false);
            return match;
        }
    }

    /// <summary>Rút 1 lá ngẫu nhiên CHƯA có trên tay ai (build deck mới, loại các lá đang dùng).</summary>
    private static void DrawOneCard(Match match, MatchPlayer p)
    {
        var used = new HashSet<(int, int)>(match.Players.SelectMany(x => x.Hand).Select(c => (c.Rank, (int)c.Suit)));
        var remaining = Deck.Build().Where(c => !used.Contains((c.Rank, (int)c.Suit))).ToList();
        if (remaining.Count == 0) return;
        var card = remaining[Random.Shared.Next(remaining.Count)];
        p.Hand.Add(card);
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
    /// Player bấm "Về trắng" trong trick 1. Hợp lệ khi: round InProgress, chưa qua trick 1
    /// (!PastFirstTrick), trong 60s, có WhiteWinReason. → kết thúc round NGAY, tính điểm white-win.
    /// Multi-winner: ai đã accept (gồm người này) đều là winner; người có bộ nhưng chưa kịp → thua.
    /// </summary>
    public Match AcceptWhiteWin(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.PastFirstTrick || match.WhiteWinDeadline == null)
                throw new InvalidOperationException("Đã hết cửa sổ về trắng (qua trick 1).");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.WhiteWinReason == null)
                throw new InvalidOperationException("Bạn không có bộ về trắng.");

            player.WhiteWinAccepted = true;
            EndRoundWhiteWin(match);
            return match;
        }
    }

    /// <summary>Player từ chối về trắng (ẩn nút). Chỉ đánh dấu, round vẫn chơi tiếp bình thường.</summary>
    public Match DeclineWhiteWin(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId);
            if (player?.WhiteWinReason != null)
                player.WhiteWinAccepted = false;
            return match;
        }
    }

    /// <summary>Timer: hết 60s mà chưa ai chốt → đóng cửa sổ về trắng, round chơi tiếp bình thường.</summary>
    public Match? ExpireWhiteWinWindow(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)) return null;
            if (match.Status != MatchStatus.InProgress || match.WhiteWinDeadline == null) return null;
            CloseWhiteWinWindow(match);
            return match;
        }
    }

    /// <summary>Kết thúc round bằng white-win: winner = ai đã accept; gán hạng + điểm + WaitingNextRound.</summary>
    private static void EndRoundWhiteWin(Match match)
    {
        // Người có bộ nhưng KHÔNG accept → bỏ reason, tính như người thua.
        foreach (var p in match.Players.Where(p => p.WhiteWinReason != null && p.WhiteWinAccepted != true))
            p.WhiteWinReason = null;

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
        match.WhiteWinDeadline = null;
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
        CloseWhiteWinWindow(match);   // hết trick 1 → đóng cửa sổ về trắng
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
        CloseWhiteWinWindow(match);   // hết trick 1 → đóng cửa sổ về trắng
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
                CloseWhiteWinWindow(match);
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
                    CloseWhiteWinWindow(match);
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
            // Ngôi Sao Hi Vọng đã kích cho ROUND HIỆN TẠI phải sống sót qua re-deal (star vẫn là star ở bài mới).
            Guid? keepStarId = match.Players.FirstOrDefault(p => p.IsStarOfHope)?.UserId;
            match.VoteResetDeadline = null;
            match.VoteResetInitiatorId = null;
            match.StarOfHopeScheduledUserId = keepStarId;        // DealRound tiêu lại để re-set IsStarOfHope cho bài mới
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
            if (match.IsFestivalRound || match.IsXiDachRound)
                throw new InvalidOperationException("Đang trong round đặc biệt rồi.");
            if (match.FestivalScheduled)
                throw new InvalidOperationException("Đã có người tổ chức lễ hội cho round sau.");
            if (match.XiDachScheduledUserId.HasValue)
                throw new InvalidOperationException("Round sau đã là Sát Phạt rồi.");
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
    /// Player kích hoạt "Ngôi Sao Hi Vọng": round KẾ TIẾP người này là star (mọi giao dịch điểm với
    /// player này ×2). Bất kỳ lúc nào trong round đang chơi. Chỉ 1 người/round được kích
    /// (StarOfHopeScheduledUserId), mỗi người 1 lần/TRẬN (HasUsedStarOfHope). Round hiện tại không đổi.
    /// </summary>
    public Match ActivateStarOfHope(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.IsFestivalRound)
                throw new InvalidOperationException("Đang trong round lễ hội rồi.");
            if (match.StarOfHopeScheduledUserId.HasValue)
                throw new InvalidOperationException("Đã có người kích Ngôi Sao Hi Vọng cho round sau.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.HasUsedStarOfHope)
                throw new InvalidOperationException("Bạn đã dùng quyền Ngôi Sao Hi Vọng trong trận này.");

            match.StarOfHopeScheduledUserId = userId;
            player.HasUsedStarOfHope = true;
            return match;
        }
    }

    /// <summary>
    /// Player tổ chức "Sát Phạt": round KẾ TIẾP là Xì Dách, người này làm Nhà Cái. Bất kỳ lúc nào trong
    /// round InProgress. Chỉ 1 người/round (XiDachScheduledUserId), mỗi người 1 lần/TRẬN (HasUsedXiDach).
    /// Loại trừ lẫn nhau với lễ hội (1 round chỉ 1 biến thể).
    /// </summary>
    public Match ActivateXiDach(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.IsFestivalRound || match.IsXiDachRound)
                throw new InvalidOperationException("Đang trong round đặc biệt rồi.");
            if (match.XiDachScheduledUserId.HasValue)
                throw new InvalidOperationException("Đã có người tổ chức Sát Phạt cho round sau.");
            if (match.FestivalScheduled)
                throw new InvalidOperationException("Round sau đã là lễ hội rồi.");
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.HasUsedXiDach)
                throw new InvalidOperationException("Bạn đã dùng quyền Sát Phạt trong trận này.");

            match.XiDachScheduledUserId = userId;
            player.HasUsedXiDach = true;
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
                // Chi tiết chặt/bị chặt: các combo bị tính pot = chain[0..^1] (mọi nước trước cutter cuối).
                var labels = chain.Take(chain.Count - 1).Select(e => e.Label).ToList();
                AddChopDetails(match, last.PlayerId, isCutter: true, labels);
                AddChopDetails(match, secondLast.PlayerId, isCutter: false, labels);
            }
        }
        chain.Clear();
    }

    /// <summary>Gộp chi tiết chặt heo cho 1 player (cộng dồn qua nhiều trick trong round).</summary>
    private static void AddChopDetails(Match match, Guid playerId, bool isCutter, List<string> labels)
    {
        if (match.RoundChopDetails.TryGetValue(playerId, out var cur))
            cur.Labels.AddRange(labels);
        else
            match.RoundChopDetails[playerId] = (isCutter, new List<string>(labels));
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
            match.TrickChopChain.Add((playerId, value, combo.Kind, TienLenComboEngine.ComboLabel(combo)));
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

    public IEnumerable<Match> AllXiDachPlaying() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.XiDachPlaying);

    public int[] ComputeRoundScores(Match match)
    {
        // Returns score for each player in seat order
        var n = match.Players.Count;
        var scores = new int[n];

        // Sát Phạt (Xì Dách): điểm đã tính sẵn vào XiDachDelta khi chốt từng cặp. Zero-sum (nhà cái gánh tổng).
        if (match.IsXiDachRound)
        {
            for (int i = 0; i < n; i++) scores[i] = match.Players[i].XiDachDelta;
            return scores;
        }

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
            return ApplyStarOfHopeDoubling(match, scores);
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
            return ApplyStarOfHopeDoubling(match, scores);
        }

        // Phán xử path: replaces base rank + chop-pig + 3♠ scoring entirely.
        if (match.JudgeTriggered)
        {
            return ApplyStarOfHopeDoubling(match, ComputeJudgeScores(match));
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

        return ApplyStarOfHopeDoubling(match, scores);
    }

    /// <summary>
    /// Ngôi Sao Hi Vọng: nhân ×2 mọi GIAO DỊCH điểm liên quan tới player star (cả 2 chiều thắng/thua),
    /// các giao dịch không dính star giữ nguyên. Mô hình "đôi đối xứng theo hạng": phân tách điểm ván
    /// thành các giao dịch theo cặp (base rank: Nhất↔Bét, Nhì↔Ba; chop: cutter↔victim; held: chót↔kế trên;
    /// 3♠/về-trắng/phán-xử: theo cặp tương ứng), rồi nhân đôi cặp nào chứa star.
    ///
    /// Cách làm: xây ma trận giao dịch T[from,to] (from trả to amount ≥ 0) sao cho
    /// base[i] = Σ_j (T[j,i] − T[i,j]). Sau đó với mỗi cặp (star, j): cộng thêm chính giao dịch đó 1 lần
    /// nữa (×2). Vì T zero-sum theo cặp nên kết quả vẫn zero-sum. Phần phi-zero-sum (đui 3♠ khi n&lt;4)
    /// xử lý riêng: nhân đôi delta của star trực tiếp.
    ///
    /// Nếu không có star trong round → trả về scores nguyên vẹn (không đổi hành vi cũ).
    /// </summary>
    private static int[] ApplyStarOfHopeDoubling(Match match, int[] scores)
    {
        int n = match.Players.Count;
        int starIdx = -1;
        for (int i = 0; i < n; i++) if (match.Players[i].IsStarOfHope) { starIdx = i; break; }
        if (starIdx < 0) return scores;

        var t = BuildTransactionMatrix(match, scores, out int[] residual);

        // Reconcile: đảm bảo Σ_j(T[j,i]−T[i,j]) + residual[i] == scores[i]. Phần lệch (nếu có do làm
        // tròn / case hiếm) dồn vào residual để không bao giờ sai tổng — residual chỉ ×2 cho star.
        for (int i = 0; i < n; i++)
        {
            int pairNet = 0;
            for (int j = 0; j < n; j++) pairNet += t[j, i] - t[i, j];
            residual[i] = scores[i] - pairNet; // residual hấp thụ toàn bộ phần không theo cặp
        }

        var result = (int[])scores.Clone();
        // Nhân đôi mọi giao dịch theo cặp dính star: cộng thêm 1 lần nữa (net j→star).
        for (int j = 0; j < n; j++)
        {
            if (j == starIdx) continue;
            int extraToStar = t[j, starIdx] - t[starIdx, j]; // dương = j trả star
            result[starIdx] += extraToStar;
            result[j] -= extraToStar;
        }
        // Phần residual (phi-zero-sum, vd đui 3♠ với n<4): nhân đôi phần của star.
        result[starIdx] += residual[starIdx];
        return result;
    }

    /// <summary>
    /// Phân tách scores hiện tại thành ma trận giao dịch theo cặp T[from,to] (from trả to, ≥0).
    /// `residual` được caller tính lại = scores − net(T) để hấp thụ phần phi-cặp (vd đui 3♠ n&lt;4).
    /// Chỉ cần build các cặp dính star cho ĐÚNG; phần còn lại rơi vào residual cũng không sai tổng.
    /// </summary>
    private static int[,] BuildTransactionMatrix(Match match, int[] scores, out int[] residual)
    {
        int n = match.Players.Count;
        var t = new int[n, n];
        residual = new int[n];

        if (match.IsFestivalRound)
        {
            DecomposeWinnersLosers(match, t, p => p.FestivalWinner, isWhiteWin: false);
            return t;
        }
        if (match.Players.Any(p => p.WhiteWinReason != null))
        {
            DecomposeWinnersLosers(match, t, p => p.WhiteWinReason != null, isWhiteWin: true);
            return t;
        }
        if (match.JudgeTriggered)
        {
            DecomposeJudge(match, t);
            return t;
        }
        DecomposeNormalRound(match, t);
        return t;
    }

    /// <summary>Phân tách kiểu winner/loser (về trắng &amp; lễ hội): mỗi loser trả cho từng winner phần tương ứng.</summary>
    private static void DecomposeWinnersLosers(Match match, int[,] t, Func<MatchPlayer, bool> isWinner, bool isWhiteWin)
    {
        int n = match.Players.Count;
        var winners = Enumerable.Range(0, n).Where(i => isWinner(match.Players[i])).ToList();
        var losers = Enumerable.Range(0, n).Where(i => !isWinner(match.Players[i])).ToList();
        if (winners.Count == 0 || losers.Count == 0) return;

        // Mỗi loser đóng tổng |perLoser| chia cho các winner. Về trắng: perLoser = 2×winners (mỗi winner 2).
        // Lễ hội: perLoser = 2, chia đều cho winners (số nguyên, dư rải cho winner đầu). Để khớp CHÍNH XÁC
        // điểm winner đã tính, ta phân bổ theo cùng quy tắc round-robin "dư cho winner đầu".
        foreach (int li in losers)
        {
            int loserPays = isWhiteWin ? 2 * winners.Count : 2;
            int per = loserPays / winners.Count;
            int rem = loserPays % winners.Count;
            for (int w = 0; w < winners.Count; w++)
            {
                int amt = per + (w < rem ? 1 : 0);
                t[li, winners[w]] += amt;
            }
        }
    }

    /// <summary>
    /// Phân tách round thường thành cặp: base rank (đối xứng theo hạng), chop (cutter↔victim từ chain),
    /// 3♠ thắng (winner↔mỗi người), held (chót↔kế trên). Đui 3♠ (phi-zero-sum khi n&lt;4) KHÔNG đưa vào
    /// cặp — để rơi vào residual.
    /// </summary>
    private static void DecomposeNormalRound(Match match, int[,] t)
    {
        int n = match.Players.Count;

        // Base rank: ghép cặp đối xứng theo VỊ TRÍ HẠNG. table đối xứng table[r] = -table[n-1-r].
        // Người hạng r (tốt hơn) nhận |table[r]| từ người hạng n-1-r (đối tiền). Chỉ ghép nửa trên (r < n-1-r).
        int[] table = n switch
        {
            4 => new[] { 2, 1, -1, -2 },
            3 => new[] { 2, 0, -2 },
            2 => new[] { 1, -1 },
            _ => Enumerable.Range(0, n).Select(_ => 0).ToArray()
        };
        // map: rank-position (0-based) → player index
        var byRank = Enumerable.Range(0, n)
            .OrderBy(i => match.Players[i].FinalRank ?? n)
            .ToList();
        for (int r = 0; r < n - 1 - r; r++)
        {
            int better = byRank[r];
            int worse = byRank[n - 1 - r];
            int amt = table[r]; // dương: worse trả better
            if (amt > 0) t[worse, better] += amt;
            else if (amt < 0) t[better, worse] += -amt;
        }

        // Chop-pig: chain đã settle thành cặp (last cutter +pot, second-to-last -pot). RoundChopExtra
        // là net per-player. Vì chỉ có 1 cặp non-zero mỗi settle nhưng cộng dồn nhiều trick, ta ghép cặp
        // theo dấu: tổng dương = nhận, âm = trả. Ghép greedy donor→receiver (zero-sum nên khớp).
        DecomposeNetBySign(match, t, match.RoundChopExtra);

        // 3♠ thắng cuối: Nhất +(n-1), mỗi người khác -1 → cặp winner↔mỗi người (winner nhận 1 từ mỗi người).
        var winner = match.Players.FirstOrDefault(p => p.FinalRank == 1 && p.FinishedWithThreeOfSpades);
        if (winner != null)
        {
            int wi = match.Players.IndexOf(winner);
            for (int i = 0; i < n; i++) if (i != wi) t[i, wi] += 1;
        }

        // Held: chót trả kế trên đúng held (zero-sum cặp).
        var chot = match.Players.FirstOrDefault(p => p.FinalRank == n);
        if (chot != null)
        {
            int held = TienLenComboEngine.ComputeHeldValue(chot.Hand);
            if (held > 0)
            {
                var above = match.Players.FirstOrDefault(p => p.FinalRank == n - 1);
                if (above != null)
                    t[match.Players.IndexOf(chot), match.Players.IndexOf(above)] += held;
            }
        }
        // Đui 3♠ (loser -3, others +1) cố ý KHÔNG ghép cặp ở đây → rơi vào residual (giữ đúng tổng).
    }

    /// <summary>
    /// Phán xử: victim trả winner (4+held) — cặp victim↔winner. Case B pardoned trả winner 1. Case C
    /// pardoned sub-round (ghép theo net sign) + held cuối. Chop + 3♠ stack ghép như round thường.
    /// </summary>
    private static void DecomposeJudge(Match match, int[,] t)
    {
        int n = match.Players.Count;
        var winnerP = match.Players.FirstOrDefault(p => p.JudgeIsWinner);
        if (winnerP == null) return;
        int wi = match.Players.IndexOf(winnerP);

        for (int i = 0; i < n; i++)
        {
            var p = match.Players[i];
            if (p.JudgeIsVictim) t[i, wi] += 4 + p.JudgeHeldValue; // victim trả winner
        }

        var pardoned = match.Players.Where(p => p.JudgeIsPardoned).ToList();
        if (pardoned.Count == 1)
        {
            t[match.Players.IndexOf(pardoned[0]), wi] += 1; // Case B: pardoned trả winner 1
        }
        else if (pardoned.Count >= 2)
        {
            // Sub-round base rank giữa pardoned (ghép cặp đối xứng theo hạng trong nhóm pardoned).
            var ordered = pardoned.OrderBy(p => p.FinalRank ?? int.MaxValue).ToList();
            int m = ordered.Count;
            int[] subTable = m switch
            {
                3 => new[] { 2, 0, -2 },
                2 => new[] { 1, -1 },
                _ => Enumerable.Range(0, m).Select(_ => 0).ToArray()
            };
            for (int r = 0; r < m - 1 - r; r++)
            {
                int better = match.Players.IndexOf(ordered[r]);
                int worse = match.Players.IndexOf(ordered[m - 1 - r]);
                int amt = subTable[r];
                if (amt > 0) t[worse, better] += amt;
                else if (amt < 0) t[better, worse] += -amt;
            }
            // Pardoned chót còn held: trả chia đều cho pardoned khác.
            var lastP = ordered[^1];
            int lastHeld = TienLenComboEngine.ComputeHeldValue(lastP.Hand);
            if (lastHeld > 0)
            {
                int li = match.Players.IndexOf(lastP);
                var others = pardoned.Where(p => p.UserId != lastP.UserId).ToList();
                int share = lastHeld / others.Count;
                int rem = lastHeld % others.Count;
                for (int k = 0; k < others.Count; k++)
                    t[li, match.Players.IndexOf(others[k])] += share + (k < rem ? 1 : 0);
            }
        }

        // Chop-pig (giữa pardoned / mọi entry) ghép theo net sign.
        DecomposeNetBySign(match, t, match.RoundChopExtra);

        // Stack 3♠ khi winner về bằng 3♠: winner nhận 1 từ mỗi người khác.
        if (winnerP.FinishedWithThreeOfSpades)
            for (int i = 0; i < n; i++) if (i != wi) t[i, wi] += 1;
    }

    /// <summary>
    /// Ghép một bản đồ net-delta-per-player (zero-sum) thành cặp giao dịch: người âm (trả) gửi cho
    /// người dương (nhận) theo greedy. Dùng cho chop-pig (đã zero-sum theo cặp nên ghép lại an toàn).
    /// </summary>
    private static void DecomposeNetBySign(Match match, int[,] t, IReadOnlyDictionary<Guid, int> net)
    {
        if (net.Count == 0) return;
        int n = match.Players.Count;
        var debtors = new List<(int idx, int amt)>();   // amt > 0 = phải trả
        var creditors = new List<(int idx, int amt)>(); // amt > 0 = được nhận
        for (int i = 0; i < n; i++)
        {
            if (!net.TryGetValue(match.Players[i].UserId, out var v) || v == 0) continue;
            if (v < 0) debtors.Add((i, -v));
            else creditors.Add((i, v));
        }
        int di = 0, ci = 0;
        while (di < debtors.Count && ci < creditors.Count)
        {
            var (dIdx, dAmt) = debtors[di];
            var (cIdx, cAmt) = creditors[ci];
            int x = Math.Min(dAmt, cAmt);
            t[dIdx, cIdx] += x;
            dAmt -= x; cAmt -= x;
            debtors[di] = (dIdx, dAmt);
            creditors[ci] = (cIdx, cAmt);
            if (dAmt == 0) di++;
            if (cAmt == 0) ci++;
        }
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
                List<Dtos.CardDto>? xdCards = match.IsXiDachRound
                    ? p.Hand.Select(c => new Dtos.CardDto(c.Rank, (int)c.Suit)).ToList()
                    : null;
                string? xdLabel = match.IsXiDachRound ? XiDachEngine.Label(p.Hand) : null;
                int xdTotal = match.IsXiDachRound ? XiDachEngine.Total(p.Hand) : 0;
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
                    festLabel,
                    bd.StarDelta,
                    p.IsStarOfHope,
                    match.RoundChopDetails.TryGetValue(p.UserId, out var cd) ? cd.Labels : null,
                    match.RoundChopDetails.TryGetValue(p.UserId, out var cd2) && cd2.IsCutter,
                    xdCards,
                    xdLabel,
                    p.IsXiDachDealer,
                    xdTotal);
            })
            .ToList();

        var dto = new Dtos.RoundEndDto(match.Id, match.RoundNumber, wasWhiteWin, match.JudgeTriggered, entries, match.IsFestivalRound, match.IsXiDachRound);
        match.RoundHistory.Add(dto);
        return dto;
    }

    public record RoundScoreBreakdown(int BaseRank, int Chop, int ThreeOfSpades, int Judge, int WhiteWin, int HeldPenalty, int Total, int Festival = 0, int StarDelta = 0);

    /// <summary>Per-player breakdown of the round score by component (for UI display). StarDelta = phần
    /// chênh do Ngôi Sao Hi Vọng ×2 (doubled total − base total); các component khác là điểm CƠ BẢN.</summary>
    public RoundScoreBreakdown[] ComputeRoundScoreBreakdowns(Match match)
    {
        int n = match.Players.Count;
        var result = new RoundScoreBreakdown[n];

        // Sát Phạt (Xì Dách): toàn bộ điểm vào Total (UI có component riêng FestivalResultRows tương đương).
        if (match.IsXiDachRound)
        {
            for (int i = 0; i < n; i++)
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, 0, 0, match.Players[i].XiDachDelta);
            return result;
        }

        // Lễ hội (Cào Rùa): toàn bộ điểm cơ bản vào component Festival.
        if (match.IsFestivalRound)
        {
            var fest = ComputeFestivalBaseScores(match);
            for (int i = 0; i < n; i++)
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, 0, 0, fest[i], fest[i]);
            return ApplyStarDeltaToBreakdowns(match, result);
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
            return ApplyStarDeltaToBreakdowns(match, result);
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
            return ApplyStarDeltaToBreakdowns(match, result);
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
        return ApplyStarDeltaToBreakdowns(match, result);
    }

    /// <summary>Điểm CƠ BẢN round lễ hội (chưa ×2) — tách ra để breakdown hiển thị base + StarDelta riêng.</summary>
    private static int[] ComputeFestivalBaseScores(Match match)
    {
        int n = match.Players.Count;
        var scores = new int[n];
        int winnerCnt = match.Players.Count(p => p.FestivalWinner);
        int loserCnt = n - winnerCnt;
        if (winnerCnt > 0 && loserCnt > 0)
        {
            int pot = 2 * loserCnt;
            int perWinner = pot / winnerCnt;
            int rem = pot % winnerCnt;
            int wi = 0;
            for (int i = 0; i < n; i++)
                scores[i] = match.Players[i].FestivalWinner ? perWinner + (wi++ < rem ? 1 : 0) : -2;
        }
        return scores;
    }

    /// <summary>Gắn StarDelta = (điểm đã ×2) − (tổng base) vào mỗi breakdown; Total cập nhật thành điểm ×2.
    /// Không star → StarDelta = 0, Total giữ nguyên.</summary>
    private RoundScoreBreakdown[] ApplyStarDeltaToBreakdowns(Match match, RoundScoreBreakdown[] bases)
    {
        if (!match.Players.Any(p => p.IsStarOfHope)) return bases;
        var doubled = ComputeRoundScores(match);
        for (int i = 0; i < bases.Length; i++)
        {
            int starDelta = doubled[i] - bases[i].Total;
            bases[i] = bases[i] with { StarDelta = starDelta, Total = doubled[i] };
        }
        return bases;
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
