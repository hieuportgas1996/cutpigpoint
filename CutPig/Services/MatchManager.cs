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
    public const int GambleStreakThreshold = 5; // số ván về Nhất liên tiếp để được mời "Liều Ăn Nhiều"
    public static TimeSpan GambleOfferTimeout { get; } = TimeSpan.FromSeconds(30); // hết hạn lời mời liều → auto từ chối
    public static TimeSpan BreakSelectTimeout { get; } = TimeSpan.FromSeconds(30);  // 30s người tổ chức chọn game giải lao → hết giờ random
    public static TimeSpan BreakIntroTimeout { get; } = TimeSpan.FromSeconds(30);   // 30s hiện luật chơi game đã chọn → tự bắt đầu
    public static TimeSpan RpsChoiceTimeout { get; } = TimeSpan.FromSeconds(20);    // 20s chọn kéo/búa/bao MỖI ván giải lao
    public static TimeSpan RpsRevealTimeout { get; } = TimeSpan.FromSeconds(4);     // 4s xem kết quả ván RPS (lắc ~0.7s + lật + ngắm) trước khi qua ván kế
    public static TimeSpan FestivalRevealViewTimeout { get; } = TimeSpan.FromSeconds(5);  // xem bài sau khi lật hết
    public static TimeSpan FestivalAutoFlipTimeout { get; } = TimeSpan.FromSeconds(60);   // auto-lật nếu treo
    public static TimeSpan XiDachTurnTimeout { get; } = TimeSpan.FromSeconds(30);          // 30s/lượt rút bài xì dách
    public static TimeSpan MathPickTimeout { get; } = TimeSpan.FromSeconds(10);     // 10s mỗi người chọn 1 chữ số 0-9
    public static TimeSpan MathAnswerTimeout { get; } = TimeSpan.FromSeconds(20);   // 20s suy nghĩ + trả lời mỗi câu trắc nghiệm
    public static TimeSpan MathRevealTimeout { get; } = TimeSpan.FromSeconds(3);    // 3s xem đáp án đúng + ai nhanh nhất giữa các câu
    public static TimeSpan MemoryViewTimeout { get; } = TimeSpan.FromSeconds(10);   // 10s xem lưới 3×3 logo CLB để ghi nhớ
    public static TimeSpan MemoryAnswerTimeout { get; } = TimeSpan.FromSeconds(20); // 20s trả lời mỗi câu "ô X là đội nào?"
    public static TimeSpan MemoryRevealTimeout { get; } = TimeSpan.FromSeconds(3);  // 3s hiện đáp án đúng giữa các câu
    public static TimeSpan ReflexCooldownTimeout { get; } = TimeSpan.FromSeconds(3);  // 3s cooldown chuẩn bị mỗi lượt Phản xạ
    public static TimeSpan ReflexAnswerTimeout { get; } = TimeSpan.FromSeconds(15);   // 15s tìm + chọn đúng 3 lá theo đề
    public static TimeSpan ReflexRevealTimeout { get; } = TimeSpan.FromSeconds(3);    // 3s hiện ô đúng giữa các lượt
    public static TimeSpan SudokuTimeout { get; } = TimeSpan.FromSeconds(60);         // 60s giải Sudoku 4×4 (Trí tuệ)
    public static TimeSpan MatchPairsSpinTimeout { get; } = TimeSpan.FromSeconds(20); // 20s pha quay thứ tự (Cơ hội)
    public static TimeSpan MatchPairsTimeout { get; } = TimeSpan.FromSeconds(300);    // 300s tổng ván lật cặp (Cơ hội)
    public static TimeSpan MatchPairsTurnTimeout { get; } = TimeSpan.FromSeconds(10); // 10s/lượt; hết → auto lật trật + qua lượt
    public static TimeSpan MatchPairsRevealTimeout { get; } = TimeSpan.FromSeconds(5); // 5s hiện thứ tự đi sau khi quay rồi mới chơi
    public static TimeSpan MatchPairsMismatchTimeout { get; } = TimeSpan.FromMilliseconds(1500); // 1.5s hiện 2 lá trật rồi úp
    public static TimeSpan CaroSpinTimeout { get; } = TimeSpan.FromSeconds(20);      // 20s pha quay chia team (Caro)
    public static TimeSpan CaroRevealTimeout { get; } = TimeSpan.FromSeconds(10);    // 10s hiện cặp đấu (xem ai đấu ai) trước mỗi ván
    public static TimeSpan CaroWinShowTimeout { get; } = TimeSpan.FromSeconds(4);    // 4s giữ bàn + gạch chuỗi thắng cho mọi người xem
    public static TimeSpan CaroTurnTimeout { get; } = TimeSpan.FromSeconds(10);      // 10s/lượt; hết → bỏ lượt, qua người kế
    public static TimeSpan CaroTimeout { get; } = TimeSpan.FromSeconds(600);         // 600s backstop tổng ván Caro → hòa

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
        match.IsGambleRound = false;
        match.IsBreakRound = false;
        match.BreakGame = BreakGameType.None;
        match.BreakSelectDeadline = null;
        match.BreakIntroDeadline = null;
        match.Rps = null;
        match.RpsChoiceDeadline = null;
        match.RpsRevealUntil = null;
        match.MathPicks.Clear();
        match.MathPickDeadline = null;
        match.MathQuestions = null;
        match.MathCurrentQuestion = 0;
        match.MathQuestionStart = null;
        match.MathAnswerDeadline = null;
        match.MathAnswers.Clear();
        match.MathRevealUntil = null;
        match.MemoryBoard = null;
        match.MemoryViewDeadline = null;
        match.MemoryCurrentQuestion = 0;
        match.MemoryQuestionStart = null;
        match.MemoryAnswerDeadline = null;
        match.MemoryAnswers.Clear();
        match.MemoryRevealUntil = null;
        match.ReflexRounds = null;
        match.ReflexCurrentRound = 0;
        match.ReflexCooldownUntil = null;
        match.ReflexRoundStart = null;
        match.ReflexAnswerDeadline = null;
        match.ReflexPicks.Clear();
        match.ReflexAnswers.Clear();
        match.ReflexRevealUntil = null;
        match.Sudoku = null;
        match.SudokuFills.Clear();
        match.SudokuAnswers.Clear();
        match.SudokuStart = null;
        match.SudokuDeadline = null;
        match.MatchPairsBoard = null;
        match.MatchPairsMatched = Array.Empty<bool>();
        match.MatchPairsFlipped.Clear();
        match.MatchPairsCount.Clear();
        match.MatchPairsTurnOrder.Clear();
        match.MatchPairsTurnIdx = 0;
        match.MatchPairsSpinDeadline = null;
        match.MatchPairsDeadline = null;
        match.MatchPairsTurnDeadline = null;
        match.MatchPairsMismatchUntil = null;
        match.MatchPairsRevealUntil = null;
        match.CaroBoard = null;
        match.CaroTeam.Clear();
        match.CaroPairs.Clear();
        match.CaroPairIndex = 0;
        match.CaroPairWinners.Clear();
        match.CaroTurnOrder.Clear();
        match.CaroTurnIdx = 0;
        match.CaroLastMove = -1;
        match.CaroWinnerTeam = 0;
        match.CaroMatchWinnerTeam = 0;
        match.CaroWinLine.Clear();
        match.CaroSpinDeadline = null;
        match.CaroRevealUntil = null;
        match.CaroTurnDeadline = null;
        match.CaroDeadline = null;
        match.CaroWinShowUntil = null;
        match.CaroDrawVotes.Clear();
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
            p.XiDachBaseDelta = 0;
            p.XiDachRevealed = false;
            p.IsGambling = false;
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

        // Round Giải Lao: tiêu cờ BreakScheduled → round này là giải lao. KHÔNG chọn game ngay:
        // vào pha BreakSelect (người tổ chức chọn game, 30s → random) → BreakIntro (hiện luật, 30s → bắt đầu).
        match.IsBreakRound = match.BreakScheduled;
        if (match.IsBreakRound)
        {
            match.BreakScheduled = false;
            match.BreakGame = BreakGameType.None; // chưa chọn — pha BreakSelect quyết định
            match.Status = MatchStatus.BreakSelect;
            match.BreakSelectDeadline = DateTime.UtcNow + BreakSelectTimeout;
            return;
        }
        match.BreakScheduled = false;
        match.BreakOrganizerId = null; // round thường: xoá người tổ chức giải lao

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

        // Liều Ăn Nhiều: tiêu cờ đã đồng ý liều → round NÀY người đó liều. Đánh đổi: CHỈ KHI người liều
        // chính là người về Nhất ván trước (PreviousRoundWinnerId) thì mới mất quyền đi đầu, ép luật 3♠
        // (ai cầm 3♠ đi đầu). Nếu Nhất ván trước là người khác → người đó đi đầu bình thường (không ép 3♠).
        // Áp TRƯỚC SetupFirstTurn để firstSeat tính đúng.
        if (match.GambleScheduledUserId is Guid gambleId)
        {
            var gambler = match.Players.FirstOrDefault(p => p.UserId == gambleId);
            if (gambler != null)
            {
                gambler.IsGambling = true;
                match.IsGambleRound = true;
                if (match.PreviousRoundWinnerId == gambleId)
                    match.EnforceThreeSpadesOpening = true; // người liều là winner ván trước → mất quyền đi đầu
            }
            match.GambleScheduledUserId = null;
        }

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
    /// Deal round "Giải Lao" Oẳn Tù Xì: cần ĐÚNG 4 người. Xáo trộn ngẫu nhiên 4 userId → bracket
    /// (V1/V2 BO3, V3 hạng-3 BO3, V4 final BO5). Vào status BreakRps, mở ván Oẳn Tù Xì đầu tiên (V1)
    /// với deadline 10s. KHÔNG đụng PreviousRoundWinnerId (giữ người Nhất round trước cho round TLMN kế).
    /// </summary>
    private static void DealBreakRound(Match match)
    {
        var seeds = match.Players.Select(p => p.UserId).ToList();
        // Xáo trộn Fisher-Yates để ghép cặp ngẫu nhiên.
        for (int i = seeds.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (seeds[i], seeds[j]) = (seeds[j], seeds[i]);
        }
        match.Rps = RpsTournament.Create(seeds);
        match.BreakGame = BreakGameType.Rps;
        match.Status = MatchStatus.BreakRps;
        match.RpsChoiceDeadline = DateTime.UtcNow + RpsChoiceTimeout;
    }

    /// <summary>
    /// Deal round "Giải Lao — Tính toán": cần ĐÚNG 4 người. Vào pha BreakMathPick — mỗi người chọn 1 chữ số 0-9
    /// (10s, hết giờ auto random). KHÔNG đụng PreviousRoundWinnerId. Câu hỏi sinh khi pha chọn số kết thúc.
    /// </summary>
    private static void DealBreakMathRound(Match match)
    {
        match.BreakGame = BreakGameType.Math;
        match.MathPicks.Clear();
        match.MathAnswers.Clear();
        match.MathQuestions = null;
        match.MathCurrentQuestion = 0;
        match.MathQuestionStart = null;
        match.MathAnswerDeadline = null;
        match.MathRevealUntil = null;
        match.Status = MatchStatus.BreakMathPick;
        match.MathPickDeadline = DateTime.UtcNow + MathPickTimeout;
    }

    /// <summary>
    /// Player đặt lịch "Giải lao zui zẻ": round KẾ TIẾP là round giải lao. KHÔNG chọn game ở đây — game được
    /// người tổ chức chọn ở pha BreakSelect đầu round (modal option, 30s → random nếu không chọn).
    /// Bất kỳ lúc nào trong round InProgress. Chỉ 1 người/round (BreakScheduled), tối đa 2 lần/TRẬN (BreakUsedCount). CHỈ đủ 4 người.
    /// Loại trừ lẫn nhau với các biến tấu khác.
    /// (Tham số gameType BỎ QUA — giữ chữ ký cho tương thích hub; game chọn ở đầu round.)
    /// </summary>
    public const int MaxBreakPerPlayer = 2;

    public Match ScheduleBreak(Guid roomId, Guid userId, BreakGameType gameType = BreakGameType.None)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            if (match.Players.Count != 4)
                throw new InvalidOperationException("Giải lao cần đúng 4 người.");
            EnsureNoSpecialScheduled(match);
            var player = match.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (player.BreakUsedCount >= MaxBreakPerPlayer)
                throw new InvalidOperationException($"Bạn đã dùng hết {MaxBreakPerPlayer} lượt Giải lao trong trận này.");

            match.BreakScheduled = true;
            match.BreakOrganizerId = userId;
            player.BreakUsedCount++;
            return match;
        }
    }

    /// <summary>
    /// Người tổ chức chọn game ở pha BreakSelect → sang pha BreakIntro (hiện luật, 30s → tự bắt đầu).
    /// Chỉ BreakOrganizerId được chọn; chỉ hợp lệ trong status BreakSelect; gameType phải là 1 trong 4 game.
    /// </summary>
    public Match SelectBreakGame(Guid roomId, Guid userId, BreakGameType gameType)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakSelect)
                throw new InvalidOperationException("Không trong pha chọn game giải lao.");
            if (match.BreakOrganizerId != userId)
                throw new InvalidOperationException("Chỉ người tổ chức được chọn game.");
            if (gameType is not (BreakGameType.Rps or BreakGameType.Math or BreakGameType.Memory or BreakGameType.Reflex or BreakGameType.Sudoku or BreakGameType.MatchPairs or BreakGameType.Caro))
                throw new InvalidOperationException("Game không hợp lệ.");
            EnterBreakIntro(match, gameType);
            return match;
        }
    }

    /// <summary>Vào pha hiện luật cho game đã chọn (BreakIntro). 30s sau timer tự bắt đầu game (StartBreakGame).</summary>
    private static void EnterBreakIntro(Match match, BreakGameType gameType)
    {
        match.BreakGame = gameType;
        match.BreakSelectDeadline = null;
        match.Status = MatchStatus.BreakIntro;
        match.BreakIntroDeadline = DateTime.UtcNow + BreakIntroTimeout;
    }

    /// <summary>Hết 30s pha chọn game mà người tổ chức chưa chọn → random 1 trong 4 game rồi sang pha hiện luật.</summary>
    public bool TryAutoSelectBreakGame(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakSelect) return false;
            if (!match.BreakSelectDeadline.HasValue || match.BreakSelectDeadline.Value > DateTime.UtcNow) return false;
            var games = new[] { BreakGameType.Rps, BreakGameType.Math, BreakGameType.Memory, BreakGameType.Reflex, BreakGameType.Sudoku, BreakGameType.MatchPairs, BreakGameType.Caro };
            EnterBreakIntro(match, games[Random.Shared.Next(games.Length)]);
            return true;
        }
    }

    /// <summary>Người tổ chức bấm "Chơi ngay" ở pha hiện luật → bắt đầu game ngay (skip 30s đếm ngược).</summary>
    public Match StartBreakGameNow(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakIntro)
                throw new InvalidOperationException("Không trong pha giới thiệu luật.");
            if (match.BreakOrganizerId != userId)
                throw new InvalidOperationException("Chỉ người tổ chức được bắt đầu sớm.");
            StartBreakGame(match);
            return match;
        }
    }

    /// <summary>Hết 30s pha hiện luật → bắt đầu game đã chọn (deal game tương ứng, set status gameplay).</summary>
    public bool TryStartBreakGame(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakIntro) return false;
            if (!match.BreakIntroDeadline.HasValue || match.BreakIntroDeadline.Value > DateTime.UtcNow) return false;
            StartBreakGame(match);
            return true;
        }
    }

    /// <summary>Deal game giải lao đã chọn (BreakGame) — chuyển từ pha hiện luật vào gameplay thực.</summary>
    private static void StartBreakGame(Match match)
    {
        match.BreakIntroDeadline = null;
        if (match.BreakGame == BreakGameType.Math) DealBreakMathRound(match);
        else if (match.BreakGame == BreakGameType.Memory) DealBreakMemoryRound(match);
        else if (match.BreakGame == BreakGameType.Reflex) DealBreakReflexRound(match);
        else if (match.BreakGame == BreakGameType.Sudoku) DealBreakSudokuRound(match);
        else if (match.BreakGame == BreakGameType.MatchPairs) DealBreakMatchPairsRound(match);
        else if (match.BreakGame == BreakGameType.Caro) DealBreakCaroRound(match);
        else DealBreakRound(match); // Rps
    }

    public IEnumerable<Match> AllBreakSelect() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakSelect);
    public IEnumerable<Match> AllBreakIntro() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakIntro);

    /// <summary>
    /// Player chọn kéo/búa/bao trong ván Oẳn Tù Xì hiện tại (chỉ 2 người của cặp đang đấu được chọn).
    /// Khi cả 2 đã chọn → chốt ván (ResolveRpsGameAndAdvance). Trả về match đã cập nhật.
    /// </summary>
    public Match SubmitRpsChoice(Guid roomId, Guid userId, RpsChoice choice)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakRps || match.Rps == null)
                throw new InvalidOperationException("Không trong pha Giải lao Oẳn Tù Xì.");
            if (choice == RpsChoice.None)
                throw new InvalidOperationException("Lựa chọn không hợp lệ.");
            var cur = match.Rps.Current;
            if (userId == cur.PlayerAId)
            {
                if (cur.ChoiceA != RpsChoice.None) throw new InvalidOperationException("Bạn đã chọn rồi.");
                cur.ChoiceA = choice;
            }
            else if (userId == cur.PlayerBId)
            {
                if (cur.ChoiceB != RpsChoice.None) throw new InvalidOperationException("Bạn đã chọn rồi.");
                cur.ChoiceB = choice;
            }
            else throw new InvalidOperationException("Chưa tới lượt cặp của bạn.");

            if (cur.BothChosen) ResolveRpsGameAndAdvance(match);
            return match;
        }
    }

    /// <summary>
    /// Chốt ván Oẳn Tù Xì hiện tại (ghi Last* + cập nhật điểm) rồi vào PHA HIỆN KẾT QUẢ 2s (RpsRevealUntil).
    /// KHÔNG advance/mở ván kế ngay — để mọi người xem kết quả; timer FinalizeRpsReveal lo phần sau.
    /// </summary>
    private static void ResolveRpsGameAndAdvance(Match match)
    {
        var cur = match.Rps!.Current;
        cur.ResolveCurrentGame(); // hòa → reset choices không tăng điểm; có người thắng → +1 / set WinnerId
        match.RpsChoiceDeadline = null;                                  // dừng pha chọn
        match.RpsRevealUntil = DateTime.UtcNow + RpsRevealTimeout;       // giữ 2s xem kết quả
    }

    /// <summary>
    /// Hết 2s hiện kết quả: tiến bracket. Cặp xong → AdvanceStage (giải xong → FinalizeBreakRound);
    /// chưa xong (kể cả hòa) → mở ván kế với deadline chọn mới.
    /// </summary>
    private static void FinalizeRpsReveal(Match match)
    {
        var t = match.Rps!;
        var cur = t.Current;
        match.RpsRevealUntil = null;
        if (cur.IsDone)
        {
            if (t.AdvanceStage()) { FinalizeBreakRound(match); return; }
        }
        match.RpsChoiceDeadline = DateTime.UtcNow + RpsChoiceTimeout;    // ván/giai đoạn kế
    }

    /// <summary>Giải lao xong: gán FinalRank theo FinalRanking (1..4) + chuyển WaitingNextRound (scoring ở ComputeRoundScores).</summary>
    private static void FinalizeBreakRound(Match match)
    {
        var ranking = match.Rps!.FinalRanking; // [hạng1, hạng2, hạng3, hạng4]
        for (int rank = 0; rank < ranking.Count; rank++)
        {
            var p = match.Players.FirstOrDefault(x => x.UserId == ranking[rank]);
            if (p != null) p.FinalRank = rank + 1;
        }
        match.RpsChoiceDeadline = null;
        match.RpsRevealUntil = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>
    /// Timer: hết hạn 10s chọn Oẳn Tù Xì → tự random cho ai CHƯA chọn rồi chốt ván. Trả về true nếu vừa xử lý
    /// (caller broadcast lại). Có thể kết thúc giải (status → WaitingNextRound) nếu đây là ván cuối.
    /// </summary>
    public bool TryAutoResolveRps(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakRps || match.Rps == null)
                return false;
            if (!match.RpsChoiceDeadline.HasValue || match.RpsChoiceDeadline.Value > DateTime.UtcNow) return false;
            var cur = match.Rps.Current;
            if (cur.ChoiceA == RpsChoice.None) cur.ChoiceA = RpsEngine.RandomChoice(Random.Shared);
            if (cur.ChoiceB == RpsChoice.None) cur.ChoiceB = RpsEngine.RandomChoice(Random.Shared);
            ResolveRpsGameAndAdvance(match);
            return true;
        }
    }

    /// <summary>
    /// Timer: hết 2s pha hiện kết quả Oẳn Tù Xì → tiến ván/giai đoạn kế (hoặc finalize giải).
    /// Trả về true nếu vừa xử lý. Có thể chuyển status → WaitingNextRound (ván cuối).
    /// </summary>
    public bool TryFinalizeRpsReveal(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakRps || match.Rps == null)
                return false;
            if (!match.RpsRevealUntil.HasValue || match.RpsRevealUntil.Value > DateTime.UtcNow) return false;
            FinalizeRpsReveal(match);
            return true;
        }
    }

    // ==================== Giải Lao — Tính toán ====================

    /// <summary>
    /// Player chọn 1 chữ số 0-9 trong pha BreakMathPick. Mọi người nhìn realtime (broadcast MatchState).
    /// Khi đủ 4 người chọn → sinh câu hỏi + vào pha quiz NGAY. Trả về match đã cập nhật.
    /// </summary>
    public Match SubmitMathNumber(Guid roomId, Guid userId, int number)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMathPick)
                throw new InvalidOperationException("Không trong pha chọn số Tính toán.");
            if (number < 0 || number > 9)
                throw new InvalidOperationException("Chỉ được chọn số từ 0 đến 9.");
            if (!match.Players.Any(p => p.UserId == userId))
                throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (match.MathPicks.ContainsKey(userId))
                throw new InvalidOperationException("Bạn đã chọn số rồi.");

            match.MathPicks[userId] = number;
            if (match.MathPicks.Count >= match.Players.Count)
                StartMathQuiz(match);
            return match;
        }
    }

    /// <summary>Sinh 2 câu hỏi từ 4 số đã chọn (theo seat order) rồi mở câu đầu (pha BreakMathQuiz).</summary>
    private static void StartMathQuiz(Match match)
    {
        // Random cho ai chưa chọn (an toàn, dù caller thường gọi khi đủ).
        foreach (var p in match.Players)
            if (!match.MathPicks.ContainsKey(p.UserId))
                match.MathPicks[p.UserId] = Random.Shared.Next(0, 10);

        var digits = match.Players.OrderBy(p => p.SeatIndex)
            .Select(p => match.MathPicks[p.UserId]).ToList();
        match.MathQuestions = MathQuizEngine.BuildQuestions(digits, Random.Shared);
        match.MathAnswers.Clear();
        foreach (var p in match.Players) match.MathAnswers[p.UserId] = new List<MathAnswer>();
        match.MathCurrentQuestion = 0;
        match.MathPickDeadline = null;
        match.MathRevealUntil = null;
        match.Status = MatchStatus.BreakMathQuiz;
        OpenMathQuestion(match);
    }

    /// <summary>Mở câu hỏi hiện tại: đặt MathQuestionStart + deadline 5s; thêm slot answer rỗng cho mỗi người.</summary>
    private static void OpenMathQuestion(Match match)
    {
        match.MathQuestionStart = DateTime.UtcNow;
        match.MathAnswerDeadline = DateTime.UtcNow + MathAnswerTimeout;
        match.MathRevealUntil = null;
        // Mỗi người 1 slot mới (chưa trả lời).
        foreach (var p in match.Players)
        {
            if (!match.MathAnswers.TryGetValue(p.UserId, out var list)) { list = new(); match.MathAnswers[p.UserId] = list; }
            list.Add(new MathAnswer { ChosenIndex = -1, Correct = false, ElapsedMs = (long)MathAnswerTimeout.TotalMilliseconds });
        }
    }

    /// <summary>
    /// Player chọn 1 đáp án (index 0-3) cho câu hiện tại. Ghi đúng/sai + thời gian (ms từ lúc mở câu).
    /// Chỉ ghi 1 lần/câu. Khi MỌI người đã trả lời → vào pha hiện đáp án (MathRevealUntil) ngay (không chờ hết 5s).
    /// </summary>
    public Match SubmitMathAnswer(Guid roomId, Guid userId, int optionIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMathQuiz || match.MathQuestions == null)
                throw new InvalidOperationException("Không trong pha trả lời Tính toán.");
            if (match.MathRevealUntil.HasValue)
                throw new InvalidOperationException("Câu này đã chốt, chờ câu kế.");
            var q = match.MathQuestions[match.MathCurrentQuestion];
            if (optionIndex < 0 || optionIndex >= q.Options.Count)
                throw new InvalidOperationException("Đáp án không hợp lệ.");
            if (!match.MathAnswers.TryGetValue(userId, out var list) || list.Count <= match.MathCurrentQuestion)
                throw new InvalidOperationException("Bạn không ở trong ván này.");
            var slot = list[match.MathCurrentQuestion];
            if (slot.Answered)
                throw new InvalidOperationException("Bạn đã trả lời câu này rồi.");

            long elapsed = match.MathQuestionStart.HasValue
                ? (long)(DateTime.UtcNow - match.MathQuestionStart.Value).TotalMilliseconds
                : (long)MathAnswerTimeout.TotalMilliseconds;
            slot.ChosenIndex = optionIndex;
            slot.Correct = optionIndex == q.CorrectIndex;
            slot.ElapsedMs = Math.Clamp(elapsed, 0, (long)MathAnswerTimeout.TotalMilliseconds);

            // Mọi người đã trả lời → chốt câu sớm (vào pha hiện đáp án).
            bool allAnswered = match.Players.All(p =>
                match.MathAnswers.TryGetValue(p.UserId, out var l) && l.Count > match.MathCurrentQuestion && l[match.MathCurrentQuestion].Answered);
            if (allAnswered) CloseMathQuestion(match);
            return match;
        }
    }

    /// <summary>Chốt câu hiện tại: dừng deadline trả lời, vào pha hiện đáp án MathRevealTimeout giây.</summary>
    private static void CloseMathQuestion(Match match)
    {
        match.MathAnswerDeadline = null;
        match.MathRevealUntil = DateTime.UtcNow + MathRevealTimeout;
    }

    /// <summary>Hết pha hiện đáp án: qua câu kế (OpenMathQuestion) hoặc finalize (xếp hạng → WaitingNextRound).</summary>
    private static void FinalizeMathReveal(Match match)
    {
        match.MathRevealUntil = null;
        if (match.MathCurrentQuestion + 1 < (match.MathQuestions?.Count ?? 0))
        {
            match.MathCurrentQuestion++;
            OpenMathQuestion(match);
        }
        else
        {
            FinalizeBreakMathRound(match);
        }
    }

    /// <summary>Tính toán xong: xếp hạng theo (đúng desc, thời gian asc) → gán FinalRank 1..4 → WaitingNextRound.</summary>
    private static void FinalizeBreakMathRound(Match match)
    {
        var ids = match.Players.OrderBy(p => p.SeatIndex).Select(p => p.UserId).ToList();
        var ranking = MathQuizEngine.Rank(ids, match.MathAnswers);
        for (int rank = 0; rank < ranking.Count; rank++)
        {
            var p = match.Players.FirstOrDefault(x => x.UserId == ranking[rank]);
            if (p != null) p.FinalRank = rank + 1;
        }
        match.MathAnswerDeadline = null;
        match.MathRevealUntil = null;
        match.MathQuestionStart = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>Timer: hết 10s pha chọn số → random cho ai chưa chọn rồi sinh câu hỏi (vào quiz). Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoStartMathQuiz(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMathPick) return false;
            if (!match.MathPickDeadline.HasValue || match.MathPickDeadline.Value > DateTime.UtcNow) return false;
            StartMathQuiz(match);
            return true;
        }
    }

    /// <summary>Timer: hết 5s trả lời câu hiện tại → chốt câu (ai chưa trả lời = sai, max time). Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoCloseMathQuestion(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMathQuiz) return false;
            if (match.MathRevealUntil.HasValue) return false; // đang ở pha hiện đáp án
            if (!match.MathAnswerDeadline.HasValue || match.MathAnswerDeadline.Value > DateTime.UtcNow) return false;
            CloseMathQuestion(match);
            return true;
        }
    }

    /// <summary>Timer: hết pha hiện đáp án → qua câu kế hoặc finalize. Trả về true nếu vừa xử lý (có thể → WaitingNextRound).</summary>
    public bool TryFinalizeMathReveal(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMathQuiz) return false;
            if (!match.MathRevealUntil.HasValue || match.MathRevealUntil.Value > DateTime.UtcNow) return false;
            FinalizeMathReveal(match);
            return true;
        }
    }

    /// <summary>Mọi match đang ở pha chọn số Tính toán (timer scan auto-start quiz).</summary>
    public IEnumerable<Match> AllBreakMathPick() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMathPick);

    /// <summary>Mọi match đang ở pha quiz Tính toán (timer scan auto-close câu / finalize reveal).</summary>
    public IEnumerable<Match> AllBreakMathQuiz() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMathQuiz);

    // ==================== Giải Lao — Trí nhớ ====================

    /// <summary>
    /// Deal round "Giải Lao — Trí nhớ": random lưới 3×3 (9 logo CLB) + 3 câu hỏi. Vào pha xem lưới
    /// (BreakMemoryView) đếm ngược 10s cho mọi người ghi nhớ. KHÔNG đụng PreviousRoundWinnerId.
    /// </summary>
    private static void DealBreakMemoryRound(Match match)
    {
        match.BreakGame = BreakGameType.Memory;
        match.MemoryBoard = MemoryGameEngine.BuildBoard(Random.Shared);
        match.MemoryAnswers.Clear();
        foreach (var p in match.Players) match.MemoryAnswers[p.UserId] = new List<MathAnswer>();
        match.MemoryCurrentQuestion = 0;
        match.MemoryQuestionStart = null;
        match.MemoryAnswerDeadline = null;
        match.MemoryRevealUntil = null;
        match.Status = MatchStatus.BreakMemoryView;
        match.MemoryViewDeadline = DateTime.UtcNow + MemoryViewTimeout;
    }

    /// <summary>Hết pha xem lưới → ẩn lưới, mở câu hỏi đầu (pha BreakMemoryQuiz).</summary>
    private static void StartMemoryQuiz(Match match)
    {
        match.MemoryViewDeadline = null;
        match.MemoryCurrentQuestion = 0;
        match.MemoryRevealUntil = null;
        match.Status = MatchStatus.BreakMemoryQuiz;
        OpenMemoryQuestion(match);
    }

    /// <summary>Mở câu hỏi hiện tại: đặt start + deadline; thêm slot answer rỗng cho mỗi người.</summary>
    private static void OpenMemoryQuestion(Match match)
    {
        match.MemoryQuestionStart = DateTime.UtcNow;
        match.MemoryAnswerDeadline = DateTime.UtcNow + MemoryAnswerTimeout;
        match.MemoryRevealUntil = null;
        foreach (var p in match.Players)
        {
            if (!match.MemoryAnswers.TryGetValue(p.UserId, out var list)) { list = new(); match.MemoryAnswers[p.UserId] = list; }
            list.Add(new MathAnswer { ChosenIndex = -1, Correct = false, ElapsedMs = (long)MemoryAnswerTimeout.TotalMilliseconds });
        }
    }

    /// <summary>
    /// Player chọn 1 đáp án (index 0-3) cho câu Trí nhớ hiện tại. Ghi đúng/sai + thời gian. 1 lần/câu.
    /// Mọi người trả lời xong → pha hiện đáp án (MemoryRevealUntil) ngay.
    /// </summary>
    public Match SubmitMemoryAnswer(Guid roomId, Guid userId, int optionIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMemoryQuiz || match.MemoryBoard == null)
                throw new InvalidOperationException("Không trong pha trả lời Trí nhớ.");
            if (match.MemoryRevealUntil.HasValue)
                throw new InvalidOperationException("Câu này đã chốt, chờ câu kế.");
            var q = match.MemoryBoard.Questions[match.MemoryCurrentQuestion];
            if (optionIndex < 0 || optionIndex >= q.Options.Count)
                throw new InvalidOperationException("Đáp án không hợp lệ.");
            if (!match.MemoryAnswers.TryGetValue(userId, out var list) || list.Count <= match.MemoryCurrentQuestion)
                throw new InvalidOperationException("Bạn không ở trong ván này.");
            var slot = list[match.MemoryCurrentQuestion];
            if (slot.Answered)
                throw new InvalidOperationException("Bạn đã trả lời câu này rồi.");

            long elapsed = match.MemoryQuestionStart.HasValue
                ? (long)(DateTime.UtcNow - match.MemoryQuestionStart.Value).TotalMilliseconds
                : (long)MemoryAnswerTimeout.TotalMilliseconds;
            slot.ChosenIndex = optionIndex;
            slot.Correct = optionIndex == q.CorrectIndex;
            slot.ElapsedMs = Math.Clamp(elapsed, 0, (long)MemoryAnswerTimeout.TotalMilliseconds);

            bool allAnswered = match.Players.All(p =>
                match.MemoryAnswers.TryGetValue(p.UserId, out var l) && l.Count > match.MemoryCurrentQuestion && l[match.MemoryCurrentQuestion].Answered);
            if (allAnswered) CloseMemoryQuestion(match);
            return match;
        }
    }

    private static void CloseMemoryQuestion(Match match)
    {
        match.MemoryAnswerDeadline = null;
        match.MemoryRevealUntil = DateTime.UtcNow + MemoryRevealTimeout;
    }

    private static void FinalizeMemoryReveal(Match match)
    {
        match.MemoryRevealUntil = null;
        if (match.MemoryCurrentQuestion + 1 < (match.MemoryBoard?.Questions.Count ?? 0))
        {
            match.MemoryCurrentQuestion++;
            OpenMemoryQuestion(match);
        }
        else
        {
            FinalizeBreakMemoryRound(match);
        }
    }

    /// <summary>Trí nhớ xong: xếp hạng (đúng desc, thời gian asc) → FinalRank 1..4 → WaitingNextRound.</summary>
    private static void FinalizeBreakMemoryRound(Match match)
    {
        var ids = match.Players.OrderBy(p => p.SeatIndex).Select(p => p.UserId).ToList();
        var ranking = MemoryGameEngine.Rank(ids, match.MemoryAnswers);
        for (int rank = 0; rank < ranking.Count; rank++)
        {
            var p = match.Players.FirstOrDefault(x => x.UserId == ranking[rank]);
            if (p != null) p.FinalRank = rank + 1;
        }
        match.MemoryAnswerDeadline = null;
        match.MemoryRevealUntil = null;
        match.MemoryQuestionStart = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>Timer: hết 10s pha xem lưới → vào pha trả lời. Trả về true nếu vừa xử lý.</summary>
    public bool TryStartMemoryQuiz(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMemoryView) return false;
            if (!match.MemoryViewDeadline.HasValue || match.MemoryViewDeadline.Value > DateTime.UtcNow) return false;
            StartMemoryQuiz(match);
            return true;
        }
    }

    /// <summary>Timer: hết hạn trả lời câu Trí nhớ → chốt câu (ai chưa trả lời = sai). Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoCloseMemoryQuestion(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMemoryQuiz) return false;
            if (match.MemoryRevealUntil.HasValue) return false;
            if (!match.MemoryAnswerDeadline.HasValue || match.MemoryAnswerDeadline.Value > DateTime.UtcNow) return false;
            CloseMemoryQuestion(match);
            return true;
        }
    }

    /// <summary>Timer: hết pha hiện đáp án Trí nhớ → câu kế hoặc finalize. Trả về true nếu vừa xử lý.</summary>
    public bool TryFinalizeMemoryReveal(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMemoryQuiz) return false;
            if (!match.MemoryRevealUntil.HasValue || match.MemoryRevealUntil.Value > DateTime.UtcNow) return false;
            FinalizeMemoryReveal(match);
            return true;
        }
    }

    /// <summary>Mọi match đang ở pha xem lưới Trí nhớ (timer scan auto-start quiz).</summary>
    public IEnumerable<Match> AllBreakMemoryView() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMemoryView);

    /// <summary>Mọi match đang ở pha quiz Trí nhớ (timer scan auto-close / finalize).</summary>
    public IEnumerable<Match> AllBreakMemoryQuiz() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMemoryQuiz);

    // ==================== Giải Lao — Phản xạ ====================

    /// <summary>
    /// Deal round "Giải Lao — Phản xạ": sinh 3 lượt (mỗi lượt 1 lưới 3×3 + ô target). Vào lượt đầu pha cooldown 3s
    /// (đã hiện lưới, chưa cho click). KHÔNG đụng PreviousRoundWinnerId.
    /// </summary>
    private static void DealBreakReflexRound(Match match)
    {
        match.BreakGame = BreakGameType.Reflex;
        match.ReflexRounds = ReflexGameEngine.BuildRounds(Random.Shared);
        match.ReflexAnswers.Clear();
        match.ReflexPicks.Clear();
        foreach (var p in match.Players) match.ReflexAnswers[p.UserId] = new List<MathAnswer>();
        match.ReflexCurrentRound = 0;
        match.ReflexRoundStart = null;
        match.ReflexAnswerDeadline = null;
        match.ReflexRevealUntil = null;
        OpenReflexCooldown(match);
    }

    /// <summary>Vào pha cooldown 3s của lượt hiện tại (hiện lưới, chưa click).</summary>
    private static void OpenReflexCooldown(Match match)
    {
        match.ReflexRoundStart = null;
        match.ReflexAnswerDeadline = null;
        match.ReflexRevealUntil = null;
        match.Status = MatchStatus.BreakReflexCooldown;
        match.ReflexCooldownUntil = DateTime.UtcNow + ReflexCooldownTimeout;
    }

    /// <summary>Hết cooldown → mở pha click: đặt start + deadline; reset picks + thêm slot answer rỗng cho mỗi người.</summary>
    private static void StartReflexPlay(Match match)
    {
        match.ReflexCooldownUntil = null;
        match.ReflexRoundStart = DateTime.UtcNow;
        match.ReflexAnswerDeadline = DateTime.UtcNow + ReflexAnswerTimeout;
        match.ReflexRevealUntil = null;
        match.Status = MatchStatus.BreakReflexPlay;
        match.ReflexPicks.Clear();
        foreach (var p in match.Players)
        {
            match.ReflexPicks[p.UserId] = new List<int>();
            if (!match.ReflexAnswers.TryGetValue(p.UserId, out var list)) { list = new(); match.ReflexAnswers[p.UserId] = list; }
            list.Add(new MathAnswer { ChosenIndex = -1, Correct = false, ElapsedMs = (long)ReflexAnswerTimeout.TotalMilliseconds });
        }
    }

    /// <summary>
    /// Player click 1 lá (index 0-15) trong pha play → thêm vào tập đã chọn. Chọn ĐỦ 3 lá = CHỐT lượt cho người đó
    /// (Correct = đúng cả 3 lá target, ElapsedMs = lúc chọn lá thứ 3). Click trùng lá đã chọn → bỏ qua. Không bỏ chọn.
    /// Mọi người chốt xong → pha hiện đáp án (ReflexRevealUntil) ngay.
    /// </summary>
    public Match SubmitReflexCell(Guid roomId, Guid userId, int cellIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakReflexPlay || match.ReflexRounds == null)
                throw new InvalidOperationException("Không trong pha chơi Phản xạ.");
            if (match.ReflexRevealUntil.HasValue)
                throw new InvalidOperationException("Lượt này đã chốt, chờ lượt kế.");
            var round = match.ReflexRounds[match.ReflexCurrentRound];
            if (cellIndex < 0 || cellIndex >= round.Grid.Count)
                throw new InvalidOperationException("Ô không hợp lệ.");
            if (!match.ReflexAnswers.TryGetValue(userId, out var list) || list.Count <= match.ReflexCurrentRound)
                throw new InvalidOperationException("Bạn không ở trong ván này.");
            var slot = list[match.ReflexCurrentRound];
            if (slot.Answered)
                throw new InvalidOperationException("Bạn đã chốt lượt này rồi.");

            if (!match.ReflexPicks.TryGetValue(userId, out var picks)) { picks = new(); match.ReflexPicks[userId] = picks; }
            if (picks.Contains(cellIndex)) return match;        // click trùng → bỏ qua (không bỏ chọn)
            if (picks.Count >= ReflexGameEngine.NumTargets) return match;
            picks.Add(cellIndex);

            // Chọn đủ 3 lá → chốt slot cho người này.
            if (picks.Count >= ReflexGameEngine.NumTargets)
            {
                long elapsed = match.ReflexRoundStart.HasValue
                    ? (long)(DateTime.UtcNow - match.ReflexRoundStart.Value).TotalMilliseconds
                    : (long)ReflexAnswerTimeout.TotalMilliseconds;
                slot.ChosenIndex = picks[0];                    // đánh dấu đã trả lời (giá trị không quan trọng, dùng ChosenCells dưới)
                slot.Correct = ReflexGameEngine.IsCorrect(round, picks);
                slot.ElapsedMs = Math.Clamp(elapsed, 0, (long)ReflexAnswerTimeout.TotalMilliseconds);

                bool allAnswered = match.Players.All(p =>
                    match.ReflexAnswers.TryGetValue(p.UserId, out var l) && l.Count > match.ReflexCurrentRound && l[match.ReflexCurrentRound].Answered);
                if (allAnswered) CloseReflexRound(match);
            }
            return match;
        }
    }

    private static void CloseReflexRound(Match match)
    {
        match.ReflexAnswerDeadline = null;
        match.ReflexRevealUntil = DateTime.UtcNow + ReflexRevealTimeout;
    }

    private static void FinalizeReflexReveal(Match match)
    {
        match.ReflexRevealUntil = null;
        if (match.ReflexCurrentRound + 1 < (match.ReflexRounds?.Count ?? 0))
        {
            match.ReflexCurrentRound++;
            OpenReflexCooldown(match);  // lượt kế bắt đầu bằng cooldown 3s
        }
        else
        {
            FinalizeBreakReflexRound(match);
        }
    }

    /// <summary>Phản xạ xong: xếp hạng (đúng desc, thời gian asc) → FinalRank 1..4 → WaitingNextRound.</summary>
    private static void FinalizeBreakReflexRound(Match match)
    {
        var ids = match.Players.OrderBy(p => p.SeatIndex).Select(p => p.UserId).ToList();
        var ranking = ReflexGameEngine.Rank(ids, match.ReflexAnswers);
        for (int rank = 0; rank < ranking.Count; rank++)
        {
            var p = match.Players.FirstOrDefault(x => x.UserId == ranking[rank]);
            if (p != null) p.FinalRank = rank + 1;
        }
        match.ReflexCooldownUntil = null;
        match.ReflexAnswerDeadline = null;
        match.ReflexRevealUntil = null;
        match.ReflexRoundStart = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>Timer: hết 3s cooldown → mở pha click. Trả về true nếu vừa xử lý.</summary>
    public bool TryStartReflexPlay(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakReflexCooldown) return false;
            if (!match.ReflexCooldownUntil.HasValue || match.ReflexCooldownUntil.Value > DateTime.UtcNow) return false;
            StartReflexPlay(match);
            return true;
        }
    }

    /// <summary>Timer: hết hạn click → chốt lượt (ai chưa click = sai). Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoCloseReflexRound(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakReflexPlay) return false;
            if (match.ReflexRevealUntil.HasValue) return false;
            if (!match.ReflexAnswerDeadline.HasValue || match.ReflexAnswerDeadline.Value > DateTime.UtcNow) return false;
            CloseReflexRound(match);
            return true;
        }
    }

    /// <summary>Timer: hết pha hiện đáp án → lượt kế (cooldown) hoặc finalize. Trả về true nếu vừa xử lý.</summary>
    public bool TryFinalizeReflexReveal(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakReflexPlay) return false;
            if (!match.ReflexRevealUntil.HasValue || match.ReflexRevealUntil.Value > DateTime.UtcNow) return false;
            FinalizeReflexReveal(match);
            return true;
        }
    }

    /// <summary>Mọi match đang ở pha cooldown Phản xạ (timer scan → mở pha click).</summary>
    public IEnumerable<Match> AllBreakReflexCooldown() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakReflexCooldown);

    /// <summary>Mọi match đang ở pha click Phản xạ (timer scan auto-close / finalize).</summary>
    public IEnumerable<Match> AllBreakReflexPlay() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakReflexPlay);

    // ---- Giải Lao — Trí tuệ (Sudoku 4×4) ----
    /// <summary>
    /// Deal round "Giải Lao — Trí tuệ": sinh 1 đề Sudoku 4×4 (CHUNG cả 4 người), khởi tạo bài điền = ô cho sẵn,
    /// vào pha giải 60s. KHÔNG đụng PreviousRoundWinnerId.
    /// </summary>
    private static void DealBreakSudokuRound(Match match)
    {
        match.BreakGame = BreakGameType.Sudoku;
        var puzzle = SudokuGameEngine.Build(Random.Shared);
        match.Sudoku = puzzle;
        match.SudokuFills.Clear();
        match.SudokuAnswers.Clear();
        foreach (var p in match.Players)
        {
            // Bài điền ban đầu = các ô cho sẵn (ô trống = 0).
            var fills = new int[SudokuGameEngine.Cells];
            for (int i = 0; i < SudokuGameEngine.Cells; i++)
                fills[i] = puzzle.Given[i] ? puzzle.Solution[i] : 0;
            match.SudokuFills[p.UserId] = fills;
            // 1 slot answer (cả puzzle): mặc định chưa giải xong = sai + max time.
            match.SudokuAnswers[p.UserId] = new List<MathAnswer>
            {
                new() { ChosenIndex = -1, Correct = false, ElapsedMs = (long)SudokuTimeout.TotalMilliseconds }
            };
        }
        match.SudokuStart = DateTime.UtcNow;
        match.SudokuDeadline = DateTime.UtcNow + SudokuTimeout;
        match.Status = MatchStatus.BreakSudoku;
    }

    /// <summary>
    /// Player điền 1 ô Sudoku (cellIndex 0-15, value 1-4 hoặc 0=xoá). Không sửa ô cho sẵn / sau khi đã giải xong /
    /// sau khi hết giờ. Nếu điền xong khớp lời giải → chốt slot (Correct=true, ElapsedMs). Mọi người xong → finalize ngay.
    /// </summary>
    public Match SubmitSudokuCell(Guid roomId, Guid userId, int cellIndex, int value)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakSudoku || match.Sudoku == null)
                throw new InvalidOperationException("Không trong pha giải Sudoku.");
            if (cellIndex < 0 || cellIndex >= SudokuGameEngine.Cells)
                throw new InvalidOperationException("Ô không hợp lệ.");
            if (value < 0 || value > SudokuGameEngine.N)
                throw new InvalidOperationException("Giá trị không hợp lệ.");
            if (match.Sudoku.Given[cellIndex])
                throw new InvalidOperationException("Ô này cho sẵn, không sửa được.");
            if (!match.SudokuFills.TryGetValue(userId, out var fills) || !match.SudokuAnswers.TryGetValue(userId, out var ans) || ans.Count == 0)
                throw new InvalidOperationException("Bạn không ở trong ván này.");
            if (ans[0].Correct) throw new InvalidOperationException("Bạn đã giải xong rồi.");

            fills[cellIndex] = value;

            // Điền đủ + khớp lời giải → chốt.
            if (SudokuGameEngine.IsSolved(match.Sudoku, fills))
            {
                long elapsed = match.SudokuStart.HasValue
                    ? (long)(DateTime.UtcNow - match.SudokuStart.Value).TotalMilliseconds
                    : (long)SudokuTimeout.TotalMilliseconds;
                ans[0].Correct = true;
                ans[0].ChosenIndex = 0; // đánh dấu đã trả lời
                ans[0].ElapsedMs = Math.Clamp(elapsed, 0, (long)SudokuTimeout.TotalMilliseconds);

                bool allDone = match.Players.All(p =>
                    match.SudokuAnswers.TryGetValue(p.UserId, out var l) && l.Count > 0 && l[0].Correct);
                if (allDone) FinalizeBreakSudokuRound(match);
            }
            return match;
        }
    }

    /// <summary>Trí tuệ xong: xếp hạng (đúng desc, thời gian asc) → FinalRank 1..4 → WaitingNextRound.</summary>
    private static void FinalizeBreakSudokuRound(Match match)
    {
        var ids = match.Players.OrderBy(p => p.SeatIndex).Select(p => p.UserId).ToList();
        var ranking = SudokuGameEngine.Rank(ids, match.SudokuAnswers);
        for (int rank = 0; rank < ranking.Count; rank++)
        {
            var p = match.Players.FirstOrDefault(x => x.UserId == ranking[rank]);
            if (p != null) p.FinalRank = rank + 1;
        }
        match.SudokuStart = null;
        match.SudokuDeadline = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    /// <summary>Timer: hết 60s giải Sudoku → ai chưa xong tính sai (đã set sẵn) rồi finalize. Trả về true nếu vừa xử lý.</summary>
    public bool TryFinalizeSudoku(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakSudoku) return false;
            if (!match.SudokuDeadline.HasValue || match.SudokuDeadline.Value > DateTime.UtcNow) return false;
            FinalizeBreakSudokuRound(match);
            return true;
        }
    }

    public IEnumerable<Match> AllBreakSudoku() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakSudoku);

    // ---- Giải Lao — Cơ hội (Match Pairs: lật cặp lá bài giống nhau) ----
    /// <summary>
    /// Deal round "Giải Lao — Cơ hội": sinh lưới 4×4 = 8 cặp, vào pha QUAY thứ tự (20s; tổ chức bấm hoặc auto).
    /// KHÔNG đụng PreviousRoundWinnerId.
    /// </summary>
    private static void DealBreakMatchPairsRound(Match match)
    {
        match.BreakGame = BreakGameType.MatchPairs;
        match.MatchPairsBoard = MatchPairsGameEngine.BuildBoard(Random.Shared);
        match.MatchPairsMatched = new bool[MatchPairsGameEngine.GridSize];
        match.MatchPairsFlipped.Clear();
        match.MatchPairsCount.Clear();
        foreach (var p in match.Players) match.MatchPairsCount[p.UserId] = 0;
        match.MatchPairsTurnOrder.Clear();
        match.MatchPairsTurnIdx = 0;
        match.MatchPairsMismatchUntil = null;
        match.MatchPairsDeadline = null;
        match.Status = MatchStatus.BreakMatchSpin;
        match.MatchPairsSpinDeadline = DateTime.UtcNow + MatchPairsSpinTimeout;
    }

    /// <summary>Người tổ chức bấm "Quay" → random thứ tự lượt cho 4 người → vào pha chơi (120s tổng).</summary>
    public Match SpinMatchPairsOrder(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchSpin)
                throw new InvalidOperationException("Không trong pha quay thứ tự.");
            if (match.BreakOrganizerId != userId)
                throw new InvalidOperationException("Chỉ người tổ chức được quay.");
            SpinMatchPairsOrderInternal(match);
            return match;
        }
    }

    /// <summary>Quay random thứ tự → vào pha HIỆN KẾT QUẢ 5s (vẫn BreakMatchSpin, đã có order). Timer StartPlay sau.</summary>
    private static void SpinMatchPairsOrderInternal(Match match)
    {
        var order = match.Players.Select(p => p.UserId).ToList();
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        match.MatchPairsTurnOrder = order;
        match.MatchPairsTurnIdx = 0;
        match.MatchPairsSpinDeadline = null;
        match.MatchPairsMismatchUntil = null;
        match.MatchPairsRevealUntil = DateTime.UtcNow + MatchPairsRevealTimeout; // hiện thứ tự 5s
    }

    /// <summary>Hết 5s hiện thứ tự → vào pha chơi thật (đếm tổng + đồng hồ lượt). Trả về true nếu vừa xử lý.</summary>
    public bool TryStartMatchPairsPlay(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchSpin) return false;
            if (!match.MatchPairsRevealUntil.HasValue || match.MatchPairsRevealUntil.Value > DateTime.UtcNow) return false;
            match.MatchPairsRevealUntil = null;
            match.Status = MatchStatus.BreakMatchPlay;
            match.MatchPairsDeadline = DateTime.UtcNow + MatchPairsTimeout;
            match.MatchPairsTurnDeadline = DateTime.UtcNow + MatchPairsTurnTimeout;
            return true;
        }
    }

    /// <summary>Bắt đầu lượt mới: reset đồng hồ 10s/lượt (gọi sau quay / sau trúng giữ lượt / sau qua lượt).</summary>
    private static void StartMatchPairsTurn(Match match)
        => match.MatchPairsTurnDeadline = DateTime.UtcNow + MatchPairsTurnTimeout;

    /// <summary>UserId người đang tới lượt lật (theo MatchPairsTurnOrder + Idx). Null nếu chưa quay.</summary>
    private static Guid? MatchPairsCurrentTurn(Match match)
        => match.MatchPairsTurnOrder.Count > 0 ? match.MatchPairsTurnOrder[match.MatchPairsTurnIdx % match.MatchPairsTurnOrder.Count] : null;

    /// <summary>
    /// Player lật 1 ô (0-15) trong lượt mình. Ô đầu → lật ngửa chờ ô thứ 2. Ô thứ 2:
    /// trúng cặp → cố định lộ + +1 cặp + ĐƯỢC ĐI TIẾP; trật → để ngửa 1.5s (MismatchUntil) rồi timer úp lại + qua lượt.
    /// Hết 8 cặp → finalize ngay. Không lật khi đang chờ úp / ô đã lộ / ô đang ngửa.
    /// </summary>
    public Match FlipMatchPairsCell(Guid roomId, Guid userId, int cellIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchPlay || match.MatchPairsBoard == null)
                throw new InvalidOperationException("Không trong pha chơi Cơ hội.");
            if (match.MatchPairsMismatchUntil.HasValue)
                throw new InvalidOperationException("Đang chờ úp lá, chờ chút.");
            if (MatchPairsCurrentTurn(match) != userId)
                throw new InvalidOperationException("Chưa tới lượt bạn.");
            if (cellIndex < 0 || cellIndex >= MatchPairsGameEngine.GridSize)
                throw new InvalidOperationException("Ô không hợp lệ.");
            if (match.MatchPairsMatched[cellIndex])
                throw new InvalidOperationException("Ô này đã lật rồi.");
            if (match.MatchPairsFlipped.Contains(cellIndex))
                throw new InvalidOperationException("Ô này đang ngửa rồi.");
            if (match.MatchPairsFlipped.Count >= 2)
                throw new InvalidOperationException("Đã lật đủ 2 lá.");

            match.MatchPairsFlipped.Add(cellIndex);
            if (match.MatchPairsFlipped.Count < 2) return match; // chờ lá thứ 2

            int a = match.MatchPairsFlipped[0], b = match.MatchPairsFlipped[1];
            if (MatchPairsGameEngine.IsMatch(match.MatchPairsBoard, a, b))
            {
                // Trúng cặp: cố định lộ, +1 cặp, GIỮ lượt (được đi tiếp) → reset 10s/lượt.
                match.MatchPairsMatched[a] = true;
                match.MatchPairsMatched[b] = true;
                match.MatchPairsFlipped.Clear();
                match.MatchPairsCount[userId] = match.MatchPairsCount.GetValueOrDefault(userId) + 1;
                if (match.MatchPairsMatched.All(x => x)) FinalizeBreakMatchPairsRound(match); // hết 8 cặp
                else StartMatchPairsTurn(match);
            }
            else
            {
                // Trật: để ngửa 1.5s rồi úp lại + qua lượt (timer ResolveMatchPairsMismatch). Tắt đồng hồ lượt.
                match.MatchPairsMismatchUntil = DateTime.UtcNow + MatchPairsMismatchTimeout;
                match.MatchPairsTurnDeadline = null;
            }
            return match;
        }
    }

    /// <summary>Timer: hết 1.5s hiện 2 lá trật → úp lại + qua lượt người kế. Trả về true nếu vừa xử lý.</summary>
    public bool TryResolveMatchPairsMismatch(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchPlay) return false;
            if (!match.MatchPairsMismatchUntil.HasValue || match.MatchPairsMismatchUntil.Value > DateTime.UtcNow) return false;
            match.MatchPairsFlipped.Clear();
            match.MatchPairsMismatchUntil = null;
            match.MatchPairsTurnIdx++; // qua lượt người kế
            StartMatchPairsTurn(match); // reset 10s cho lượt mới
            return true;
        }
    }

    /// <summary>
    /// Timer: hết 10s lượt hiện tại (chưa lật đủ 2 lá) → auto lật ngẫu nhiên cho ĐỦ 2 lá TRẬT với nhau
    /// (nếu chỉ còn 1 cặp cuối thì buộc lật trúng), rồi vào pha hiện 1.5s như lật trật. Trả về true nếu vừa xử lý.
    /// </summary>
    public bool TryAutoFlipMatchPairsTurn(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchPlay || match.MatchPairsBoard == null) return false;
            if (match.MatchPairsMismatchUntil.HasValue) return false; // đang chờ úp, không phải lượt sống
            if (!match.MatchPairsTurnDeadline.HasValue || match.MatchPairsTurnDeadline.Value > DateTime.UtcNow) return false;

            // Các ô úp còn lại (chưa match, chưa đang ngửa).
            var avail = Enumerable.Range(0, MatchPairsGameEngine.GridSize)
                .Where(i => !match.MatchPairsMatched[i] && !match.MatchPairsFlipped.Contains(i))
                .ToList();

            // Lật ngẫu nhiên cho đủ 2 lá, ƯU TIÊN tạo cặp TRẬT (không khớp với lá đã ngửa).
            while (match.MatchPairsFlipped.Count < 2 && avail.Count > 0)
            {
                int pick;
                if (match.MatchPairsFlipped.Count == 1)
                {
                    int first = match.MatchPairsFlipped[0];
                    var nonMatch = avail.Where(i => !MatchPairsGameEngine.IsMatch(match.MatchPairsBoard, first, i)).ToList();
                    var pool = nonMatch.Count > 0 ? nonMatch : avail; // chỉ còn cặp cuối → buộc trúng
                    pick = pool[Random.Shared.Next(pool.Count)];
                }
                else
                {
                    pick = avail[Random.Shared.Next(avail.Count)];
                }
                match.MatchPairsFlipped.Add(pick);
                avail.Remove(pick);
            }

            // Chốt như FlipMatchPairsCell lá thứ 2.
            match.MatchPairsTurnDeadline = null;
            if (match.MatchPairsFlipped.Count == 2)
            {
                int a = match.MatchPairsFlipped[0], b = match.MatchPairsFlipped[1];
                if (MatchPairsGameEngine.IsMatch(match.MatchPairsBoard, a, b))
                {
                    // Buộc trúng (cặp cuối): cố định + +1 cho người đang tới lượt, giữ lượt.
                    var cur = MatchPairsCurrentTurn(match);
                    match.MatchPairsMatched[a] = true;
                    match.MatchPairsMatched[b] = true;
                    match.MatchPairsFlipped.Clear();
                    if (cur is Guid g) match.MatchPairsCount[g] = match.MatchPairsCount.GetValueOrDefault(g) + 1;
                    if (match.MatchPairsMatched.All(x => x)) FinalizeBreakMatchPairsRound(match);
                    else StartMatchPairsTurn(match);
                }
                else
                {
                    // Trật → hiện 1.5s rồi úp + qua lượt.
                    match.MatchPairsMismatchUntil = DateTime.UtcNow + MatchPairsMismatchTimeout;
                }
            }
            else
            {
                // Không còn ô để lật (cực hiếm) → qua lượt luôn.
                match.MatchPairsTurnIdx++;
                StartMatchPairsTurn(match);
            }
            return true;
        }
    }

    /// <summary>Timer: hết 120s tổng ván → kết thúc (xếp hạng theo số cặp). Trả về true nếu vừa xử lý.</summary>
    public bool TryFinalizeMatchPairs(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchPlay) return false;
            if (!match.MatchPairsDeadline.HasValue || match.MatchPairsDeadline.Value > DateTime.UtcNow) return false;
            FinalizeBreakMatchPairsRound(match);
            return true;
        }
    }

    /// <summary>Timer: hết 20s pha quay mà chưa bấm → server tự quay. Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoSpinMatchPairs(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakMatchSpin) return false;
            if (!match.MatchPairsSpinDeadline.HasValue || match.MatchPairsSpinDeadline.Value > DateTime.UtcNow) return false;
            SpinMatchPairsOrderInternal(match);
            return true;
        }
    }

    /// <summary>Cơ hội xong: xếp hạng theo SỐ CẶP (desc) → FinalRank 1..4 → WaitingNextRound. Điểm tính ở ComputeMatchPairsScores.</summary>
    private static void FinalizeBreakMatchPairsRound(Match match)
    {
        var ranking = match.Players
            .OrderByDescending(p => match.MatchPairsCount.GetValueOrDefault(p.UserId))
            .ThenBy(p => p.SeatIndex)
            .ToList();
        for (int rank = 0; rank < ranking.Count; rank++)
            ranking[rank].FinalRank = rank + 1;
        match.MatchPairsFlipped.Clear();
        match.MatchPairsMismatchUntil = null;
        match.MatchPairsDeadline = null;
        match.MatchPairsTurnDeadline = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    public IEnumerable<Match> AllBreakMatchSpin() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMatchSpin);
    public IEnumerable<Match> AllBreakMatchPlay() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakMatchPlay);

    // ============================== Caro đồng đội ==============================
    // Luật: chia team → chia 2 CẶP ĐẤU 1v1 (Xa vs Oc, Xb vs Od) → chơi TUẦN TỰ 2 ván caro.
    // Mỗi cặp 1 ván (bàn 10×10 riêng); ai 5 liên tiếp → team đó thắng cặp. Team thắng nhiều cặp hơn → thắng chung cuộc.

    public const int CaroPairCount = 2;

    /// <summary>
    /// Deal round "Giải Lao — Caro đồng đội": vào pha QUAY chia team + cặp đấu (20s; tổ chức bấm hoặc auto).
    /// KHÔNG đụng PreviousRoundWinnerId.
    /// </summary>
    private static void DealBreakCaroRound(Match match)
    {
        match.BreakGame = BreakGameType.Caro;
        match.CaroBoard = null;
        match.CaroTeam.Clear();
        match.CaroPairs.Clear();
        match.CaroPairIndex = 0;
        match.CaroPairWinners.Clear();
        match.CaroTurnOrder.Clear();
        match.CaroTurnIdx = 0;
        match.CaroLastMove = -1;
        match.CaroWinnerTeam = 0;
        match.CaroMatchWinnerTeam = 0;
        match.CaroWinLine.Clear();
        match.CaroDrawVotes.Clear();
        match.CaroRevealUntil = null;
        match.CaroTurnDeadline = null;
        match.CaroDeadline = null;
        match.CaroWinShowUntil = null;
        match.Status = MatchStatus.BreakCaroSpin;
        match.CaroSpinDeadline = DateTime.UtcNow + CaroSpinTimeout;
    }

    /// <summary>Người tổ chức bấm "Quay" → random chia 2 team + 2 cặp đấu → hiện 5s rồi vào ván cặp 1.</summary>
    public Match SpinCaroOrder(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroSpin)
                throw new InvalidOperationException("Không trong pha quay chia team.");
            if (match.BreakOrganizerId != userId)
                throw new InvalidOperationException("Chỉ người tổ chức được quay.");
            SpinCaroOrderInternal(match);
            return match;
        }
    }

    /// <summary>
    /// Quay random: xáo 4 người → 2 đầu = team X (1), 2 sau = team O (2). Ghép 2 cặp đấu 1v1:
    /// cặp 0 = [Xa, Oc], cặp 1 = [Xb, Od]. Vào pha HIỆN 5s (xem team + cặp) rồi chơi cặp 1.
    /// </summary>
    private static void SpinCaroOrderInternal(Match match)
    {
        var shuffled = match.Players.Select(p => p.UserId).ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var xa = shuffled[0]; var xb = shuffled[2];
        var oc = shuffled[1]; var od = shuffled[3];
        match.CaroTeam.Clear();
        match.CaroTeam[xa] = 1; match.CaroTeam[xb] = 1;
        match.CaroTeam[oc] = 2; match.CaroTeam[od] = 2;
        match.CaroPairs = new List<Guid[]>
        {
            new[] { xa, oc }, // cặp 1: X đi trước
            new[] { xb, od }, // cặp 2
        };
        match.CaroPairIndex = 0;
        match.CaroPairWinners.Clear();
        match.CaroSpinDeadline = null;
        match.CaroRevealUntil = DateTime.UtcNow + CaroRevealTimeout; // hiện team + cặp 5s
    }

    /// <summary>Timer: hết 20s pha quay mà chưa bấm → server tự quay. Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoSpinCaro(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroSpin) return false;
            if (!match.CaroSpinDeadline.HasValue || match.CaroSpinDeadline.Value > DateTime.UtcNow) return false;
            SpinCaroOrderInternal(match);
            return true;
        }
    }

    /// <summary>Hết 5s hiện team/cặp → vào ván của cặp hiện tại (deal bàn mới). Trả về true nếu vừa xử lý.</summary>
    public bool TryStartCaroPlay(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroSpin) return false;
            if (!match.CaroRevealUntil.HasValue || match.CaroRevealUntil.Value > DateTime.UtcNow) return false;
            StartCaroPair(match);
            return true;
        }
    }

    /// <summary>Bắt đầu ván cho cặp hiện tại: bàn 10×10 mới, thứ tự [X, O] của cặp, status BreakCaroPlay + đồng hồ.</summary>
    private static void StartCaroPair(Match match)
    {
        var pair = match.CaroPairs[match.CaroPairIndex];
        match.CaroBoard = CaroGameEngine.BuildBoard();
        match.CaroTurnOrder = new List<Guid> { pair[0], pair[1] }; // X đi trước
        match.CaroTurnIdx = 0;
        match.CaroLastMove = -1;
        match.CaroWinnerTeam = 0;
        match.CaroWinLine.Clear();
        match.CaroDrawVotes.Clear();
        match.CaroRevealUntil = null;
        match.CaroWinShowUntil = null;
        match.Status = MatchStatus.BreakCaroPlay;
        match.CaroDeadline = DateTime.UtcNow + CaroTimeout;
        match.CaroTurnDeadline = DateTime.UtcNow + CaroTurnTimeout;
    }

    /// <summary>Bắt đầu lượt mới: reset đồng hồ 10s/lượt.</summary>
    private static void StartCaroTurn(Match match)
        => match.CaroTurnDeadline = DateTime.UtcNow + CaroTurnTimeout;

    /// <summary>UserId người đang tới lượt đặt quân (theo CaroTurnOrder + Idx). Null nếu chưa vào cặp.</summary>
    private static Guid? CaroCurrentTurn(Match match)
        => match.CaroTurnOrder.Count > 0 ? match.CaroTurnOrder[match.CaroTurnIdx % match.CaroTurnOrder.Count] : null;

    /// <summary>
    /// Player (1 trong 2 người của cặp hiện tại) đặt 1 quân vào ô trống (0-99) trong lượt mình.
    /// ≥5 liên tiếp → team đó thắng CẶP. Bàn đầy → cặp hòa. Còn lại → qua lượt đối thủ trong cặp.
    /// </summary>
    public Match PlaceCaroStone(Guid roomId, Guid userId, int cellIndex)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroPlay || match.CaroBoard == null)
                throw new InvalidOperationException("Không trong pha chơi Caro.");
            if (CaroCurrentTurn(match) != userId)
                throw new InvalidOperationException("Chưa tới lượt bạn.");
            if (cellIndex < 0 || cellIndex >= CaroGameEngine.CellCount)
                throw new InvalidOperationException("Ô không hợp lệ.");
            if (match.CaroBoard[cellIndex] != 0)
                throw new InvalidOperationException("Ô này đã có quân.");

            int team = match.CaroTeam.GetValueOrDefault(userId);
            if (team is not (1 or 2)) throw new InvalidOperationException("Bạn chưa có team.");

            match.CaroBoard[cellIndex] = team;
            match.CaroLastMove = cellIndex;

            var winLine = CaroGameEngine.CheckWin(match.CaroBoard, cellIndex, team);
            if (winLine != null)
            {
                // Thắng: giữ bàn + gạch chuỗi thắng vài giây cho MỌI NGƯỜI xem rồi mới EndCaroPair (timer).
                match.CaroWinnerTeam = team;
                match.CaroWinLine = winLine;
                match.CaroTurnDeadline = null;
                match.CaroWinShowUntil = DateTime.UtcNow + CaroWinShowTimeout;
            }
            else if (CaroGameEngine.IsBoardFull(match.CaroBoard))
            {
                match.CaroWinnerTeam = 0; // cặp hòa
                EndCaroPair(match);
            }
            else
            {
                match.CaroTurnIdx++; // qua lượt đối thủ trong cặp
                StartCaroTurn(match);
            }
            return match;
        }
    }

    /// <summary>Timer: hết 10s lượt hiện tại mà chưa đặt → BỎ LƯỢT (không đặt quân), qua người kế. Trả về true nếu vừa xử lý.</summary>
    public bool TryAutoSkipCaroTurn(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroPlay || match.CaroBoard == null) return false;
            if (match.CaroWinShowUntil.HasValue) return false; // đang hiện thắng
            if (!match.CaroTurnDeadline.HasValue || match.CaroTurnDeadline.Value > DateTime.UtcNow) return false;
            if (CaroGameEngine.IsBoardFull(match.CaroBoard))
            {
                match.CaroWinnerTeam = 0;
                EndCaroPair(match);
                return true;
            }
            match.CaroTurnIdx++; // bỏ lượt, qua người kế
            StartCaroTurn(match);
            return true;
        }
    }

    /// <summary>Timer: hết tổng thời gian backstop của cặp → cặp đó hòa. Trả về true nếu vừa xử lý.</summary>
    public bool TryFinalizeCaro(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroPlay) return false;
            if (match.CaroWinShowUntil.HasValue) return false; // đang hiện thắng, để TryEndCaroWinShow lo
            if (!match.CaroDeadline.HasValue || match.CaroDeadline.Value > DateTime.UtcNow) return false;
            match.CaroWinnerTeam = 0; // cặp hòa
            EndCaroPair(match);
            return true;
        }
    }

    /// <summary>Timer: hết pha hiện gạch chuỗi thắng (CaroWinShowUntil) → EndCaroPair (qua cặp kế / finalize). Trả về true nếu vừa xử lý.</summary>
    public bool TryEndCaroWinShow(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroPlay) return false;
            if (!match.CaroWinShowUntil.HasValue || match.CaroWinShowUntil.Value > DateTime.UtcNow) return false;
            match.CaroWinShowUntil = null;
            EndCaroPair(match);
            return true;
        }
    }

    /// <summary>
    /// Player bấm "Xin hòa" CẶP hiện tại. Lưu phiếu. Khi CẢ 2 người của cặp đồng ý → cặp đó hòa.
    /// </summary>
    public Match VoteCaroDraw(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.BreakCaroPlay)
                throw new InvalidOperationException("Không trong pha chơi Caro.");
            if (match.CaroWinShowUntil.HasValue)
                throw new InvalidOperationException("Ván đã có kết quả.");
            if (match.CaroTurnOrder.Count == 0 || !match.CaroTurnOrder.Contains(userId))
                throw new InvalidOperationException("Bạn không trong cặp đang đấu.");
            match.CaroDrawVotes[userId] = true;
            // Cả 2 người của cặp đồng ý → cặp hòa.
            if (match.CaroTurnOrder.All(u => match.CaroDrawVotes.GetValueOrDefault(u)))
            {
                match.CaroWinnerTeam = 0;
                EndCaroPair(match);
            }
            return match;
        }
    }

    /// <summary>
    /// Kết thúc ván của cặp hiện tại: ghi team thắng cặp (CaroWinnerTeam, 0=hòa) vào CaroPairWinners.
    /// Nếu còn cặp → hiện kết quả 5s rồi vào cặp kế (qua TryStartCaroPlay). Hết cặp → finalize chung cuộc.
    /// </summary>
    private static void EndCaroPair(Match match)
    {
        match.CaroPairWinners.Add(match.CaroWinnerTeam);
        match.CaroTurnDeadline = null;
        match.CaroDeadline = null;
        match.CaroWinShowUntil = null;

        if (match.CaroPairIndex + 1 < match.CaroPairs.Count)
        {
            // Còn cặp → quay lại pha hiện 5s (status BreakCaroSpin) cho mọi người xem kết quả cặp + cặp kế.
            match.CaroPairIndex++;
            match.Status = MatchStatus.BreakCaroSpin;
            match.CaroBoard = null;
            match.CaroTurnOrder.Clear();
            match.CaroWinLine.Clear();
            match.CaroRevealUntil = DateTime.UtcNow + CaroRevealTimeout;
        }
        else
        {
            FinalizeBreakCaroMatch(match);
        }
    }

    /// <summary>
    /// Cả 2 cặp xong: team thắng nhiều cặp hơn → thắng chung cuộc (CaroMatchWinnerTeam). Bằng nhau → hòa.
    /// Gán FinalRank (thắng=1, thua=3, hòa=1) → WaitingNextRound. Điểm ở ComputeCaroScores.
    /// </summary>
    private static void FinalizeBreakCaroMatch(Match match)
    {
        int xWins = match.CaroPairWinners.Count(w => w == 1);
        int oWins = match.CaroPairWinners.Count(w => w == 2);
        match.CaroMatchWinnerTeam = xWins > oWins ? 1 : oWins > xWins ? 2 : 0;

        foreach (var p in match.Players)
        {
            int team = match.CaroTeam.GetValueOrDefault(p.UserId);
            if (match.CaroMatchWinnerTeam == 0) p.FinalRank = 1;                  // hòa
            else p.FinalRank = team == match.CaroMatchWinnerTeam ? 1 : 3;         // thắng / thua
        }
        match.CaroTurnDeadline = null;
        match.CaroDeadline = null;
        match.CaroRevealUntil = null;
        match.Status = MatchStatus.WaitingNextRound;
        match.NextRoundAt = DateTime.UtcNow + NextRoundDelay;
    }

    public IEnumerable<Match> AllBreakCaroSpin() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakCaroSpin);
    public IEnumerable<Match> AllBreakCaroPlay() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakCaroPlay);

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
            // Nhà cái đặc biệt sớm → chốt mọi cặp theo tay nhà cái HIỆN TẠI, kết thúc round.
            foreach (var p in match.Players.Where(p => !p.IsXiDachDealer)) LockXiDachPair(match, p);
            EndXiDachRound(match);
            return;
        }

        // Players đặc biệt sớm (Xì Dách / Xì Vàng) → chốt cặp NGAY (theo tay nhà cái lúc này), không rút.
        foreach (var p in match.Players.Where(p => !p.IsXiDachDealer))
        {
            var k = XiDachEngine.Classify(p.Hand);
            if (k is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang)
                LockXiDachPair(match, p);
        }

        // Bắt đầu lượt rút theo thứ tự bóc (bên phải nhà cái, ngược kim đồng hồ = seat kế tiếp).
        AdvanceXiDachTurn(match, startFromBeginning: true);
    }

    /// <summary>
    /// Chốt cặp player↔nhà cái TẠI THỜI ĐIỂM GỌI (dùng tay nhà cái hiện tại): lưu XiDachBaseDelta + đánh dấu
    /// đã xét/lật. Idempotent (đã chốt thì bỏ qua). Nhà cái rút thêm sau KHÔNG đổi delta đã chốt này.
    /// </summary>
    private static void LockXiDachPair(Match match, MatchPlayer player)
    {
        if (player.IsXiDachDealer || player.XiDachSettled) return;
        var dealer = match.Players.First(p => p.IsXiDachDealer);
        player.XiDachBaseDelta = XiDachEngine.ComparePlayerDelta(dealer.Hand, player.Hand);
        player.XiDachSettled = true;
        player.XiDachRevealed = true;
    }

    /// <summary>Thứ tự bóc bài: players (không phải nhà cái) bắt đầu từ seat NGAY SAU nhà cái, vòng tròn.</summary>
    private static List<MatchPlayer> XiDachDrawOrder(Match match)
    {
        int n = match.Players.Count;
        var dealer = match.Players.First(p => p.IsXiDachDealer);
        int ds = match.Players.IndexOf(dealer);
        var order = new List<MatchPlayer>();
        for (int k = 1; k < n; k++)
            order.Add(match.Players[(ds + k) % n]);
        return order; // đã loại nhà cái (k chạy 1..n-1)
    }

    /// <summary>
    /// Chuyển lượt rút xì dách sang người kế tiếp CHƯA dừng/chốt theo THỨ TỰ BÓC, nhà cái sau cùng.
    /// Khi mọi người xong → sang pha so điểm (XiDachCompare).
    /// </summary>
    private static void AdvanceXiDachTurn(Match match, bool startFromBeginning)
    {
        // Người cần rút = chưa chốt cặp (player), chưa dừng, chưa quắc/đền, chưa đặc biệt 2 lá.
        bool NeedsTurn(MatchPlayer p)
        {
            if (p.XiDachSettled) return false;            // đã chốt (đặc biệt sớm)
            if (p.XiDachStood) return false;              // đã dừng
            var k = XiDachEngine.Classify(p.Hand);
            if (k is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang) return false;
            // Player ≥28 (đền) → chốt ngay, không giữ lượt. Quắc ≤28 (chưa đền) → vẫn giữ lượt để "diễn".
            if (!p.IsXiDachDealer && XiDachEngine.IsDen(p.Hand)) return false;
            if (XiDachEngine.IsBust(p.Hand)) return false; // quắc → không buộc rút nữa (nhưng giữ lượt nếu là current, xử ở caller)
            return true;
        }

        // Players theo THỨ TỰ BÓC (từ phải nhà cái).
        var players = XiDachDrawOrder(match);
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

    /// <summary>
    /// Kết thúc round xì dách: chốt nốt cặp chưa xét (theo tay nhà cái CUỐI), rồi áp luật đền trên các
    /// XiDachBaseDelta đã chốt (mỗi cặp đã cố định tại lúc xét). Lật hết, sang WaitingNextRound.
    /// </summary>
    private static void EndXiDachRound(Match match)
    {
        var dealer = match.Players.First(p => p.IsXiDachDealer);
        // Chốt nốt player chưa xét theo tay nhà cái hiện tại (cuối round).
        foreach (var p in match.Players.Where(p => !p.IsXiDachDealer && !p.XiDachSettled))
            LockXiDachPair(match, p);

        var order = XiDachDrawOrder(match); // thứ tự bóc — quyết định ai là người đền gánh
        // Áp đền redirect trên base delta đã chốt.
        var (dealerDelta, playerDeltas) = XiDachEngine.RedirectDenDeltas(
            order.Select(p => p.XiDachBaseDelta).ToArray(),
            order.Select(p => XiDachEngine.IsDen(p.Hand)).ToArray());

        foreach (var p in match.Players) { p.XiDachDelta = 0; p.XiDachRevealed = true; p.XiDachSettled = true; }
        dealer.XiDachDelta = dealerDelta;
        for (int i = 0; i < order.Count; i++) order[i].XiDachDelta = playerDeltas[i];

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

            // Sau khi rút: chỉ ĐỀN (≥28) hoặc đặc biệt (xì dách/vàng) mới tự chốt sang lượt.
            // Quắc ≤28 VÀ đủ 5 lá → KHÔNG tự sang lượt: giữ lượt cho player tự bấm "Dừng" ("diễn").
            bool denNow = !p.IsXiDachDealer && XiDachEngine.IsDen(p.Hand);
            if (denNow
                || XiDachEngine.Classify(p.Hand) is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang)
            {
                if (denNow) p.XiDachStood = true; // đền → coi như đã chốt lượt
                AdvanceXiDachTurn(match, startFromBeginning: false);
            }
            else
            {
                // Còn giữ lượt (đủ 5 lá / quắc ≤28 / chưa muốn dừng) → reset deadline cho cùng người.
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
            // Đã quắc → luôn được "Dừng" (qua lượt). Chưa quắc → phải đạt ngưỡng.
            if (!XiDachEngine.IsBust(p.Hand) && !XiDachEngine.CanStand(p.Hand, p.IsXiDachDealer))
                throw new InvalidOperationException(p.IsXiDachDealer
                    ? "Nhà cái phải đạt 15 điểm mới được dừng."
                    : "Phải đạt 16 điểm mới được dừng.");
            p.XiDachStood = true;
            AdvanceXiDachTurn(match, startFromBeginning: false);
            return match;
        }
    }

    /// <summary>
    /// Nhà cái "Xét bài" 1 player (hoặc tất cả nếu targetUserId == null/Empty). Cho phép xét SỚM trong pha
    /// rút (XiDachPlaying) MIỄN LÀ nhà cái đã đạt ≥15 điểm. Xét = lật + đánh dấu đã chốt.
    /// Hết người chưa xét → kết thúc round (delta tính 1 lần ở EndXiDachRound, có luật đền).
    /// </summary>
    public Match CompareXiDachPlayer(Guid roomId, Guid dealerUserId, Guid? targetUserId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)
                || (match.Status != MatchStatus.XiDachCompare && match.Status != MatchStatus.XiDachPlaying))
                throw new InvalidOperationException("Không trong round xì dách.");
            if (match.XiDachDealerId != dealerUserId)
                throw new InvalidOperationException("Chỉ Nhà Cái được xét bài.");
            var dealer = match.Players.First(p => p.IsXiDachDealer);
            // Xét sớm (đang trong pha rút): nhà cái phải đạt ≥15 điểm.
            if (match.Status == MatchStatus.XiDachPlaying
                && !XiDachEngine.CanStand(dealer.Hand, isDealer: true) && !XiDachEngine.IsBust(dealer.Hand))
                throw new InvalidOperationException("Nhà cái phải đạt 15 điểm mới được xét bài.");

            // Trong pha rút (xét sớm) chỉ được xét player ĐÃ XONG (dừng / đặc biệt / đền / quắc) — không xét người đang rút dở.
            bool playerDone(MatchPlayer p)
            {
                if (match.Status == MatchStatus.XiDachCompare) return true; // pha so: ai cũng đã xong
                if (p.XiDachStood) return true;
                if (XiDachEngine.IsDen(p.Hand)) return true;
                if (XiDachEngine.Classify(p.Hand) is XiDachEngine.HandKind.XiDach or XiDachEngine.HandKind.XiVang) return true;
                return false;
            }

            if (targetUserId is Guid tid && tid != Guid.Empty)
            {
                var target = match.Players.FirstOrDefault(p => p.UserId == tid && !p.IsXiDachDealer)
                    ?? throw new InvalidOperationException("Không tìm thấy người chơi để xét.");
                if (target.XiDachSettled)
                    throw new InvalidOperationException("Đã xét người này rồi.");
                if (!playerDone(target))
                    throw new InvalidOperationException("Người này chưa dừng rút bài.");
                // Chốt cặp NGAY theo tay nhà cái HIỆN TẠI (xét sớm 17đ → so với 17, nhà cái rút sau không đổi).
                LockXiDachPair(match, target);
            }
            else
            {
                // Xét hết: chốt mọi player ĐÃ XONG chưa xét theo tay nhà cái hiện tại.
                foreach (var p in match.Players.Where(p => !p.IsXiDachDealer && !p.XiDachSettled && playerDone(p)).ToList())
                    LockXiDachPair(match, p);
            }
            // Chỉ chốt tay nhà cái khi đang ở PHA SO (nhà cái đã dừng rút). Xét SỚM → nhà cái vẫn rút tiếp được.
            if (match.Status == MatchStatus.XiDachCompare) dealer.XiDachStood = true;

            // Hết người chưa xét → kết thúc round.
            if (!match.Players.Any(p => !p.IsXiDachDealer && !p.XiDachSettled))
                EndXiDachRound(match);
            return match;
        }
    }

    /// <summary>Timer: hết giờ lượt rút → tự xử (quắc/đạt ngưỡng → dừng; buộc rút → rút 1 lá).</summary>
    public Match? AutoAdvanceXiDach(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.XiDachPlaying) return null;
            if (match.XiDachTurnUserId is not Guid uid) return null;
            var p = match.Players.First(x => x.UserId == uid);
            if (XiDachEngine.IsBust(p.Hand) || XiDachEngine.CanStand(p.Hand, p.IsXiDachDealer))
                p.XiDachStood = true;                       // quắc / được dừng → auto dừng
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
                    // Nếu người này về Nhất (vd mọi người khác đầu hàng) → set winner ván để ván sau họ đi
                    // đầu. Bug cũ: nhánh đầu hàng không set → PreviousRoundWinnerId stale, người khác đi đầu sai.
                    if (match.FinishedCount == 1) match.PreviousRoundWinnerId = p.UserId;
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
                    // Nếu người này về Nhất (vd mọi người khác đầu hàng) → set winner ván để ván sau họ đi
                    // đầu. Bug cũ: nhánh đầu hàng không set → PreviousRoundWinnerId stale, người khác đi đầu sai.
                    if (match.FinishedCount == 1) match.PreviousRoundWinnerId = p.UserId;
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
                    // Nếu người này về Nhất (vd mọi người khác đầu hàng) → set winner ván để ván sau họ đi
                    // đầu. Bug cũ: nhánh đầu hàng không set → PreviousRoundWinnerId stale, người khác đi đầu sai.
                    if (match.FinishedCount == 1) match.PreviousRoundWinnerId = p.UserId;
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
    /// <summary>
    /// Guard chung cho 3 chế độ đặc biệt (Lễ hội / Sát Phạt / Ngôi Sao): mỗi round chỉ ĐƯỢC ĐẶT 1 cái.
    /// Ai đặt trước thì người khác mất CẢ 3 option cho round đó. Throw nếu đã có cái nào được đặt
    /// hoặc round hiện tại đang là round đặc biệt.
    /// </summary>
    private static void EnsureNoSpecialScheduled(Match match)
    {
        if (match.IsFestivalRound || match.IsXiDachRound || match.IsBreakRound)
            throw new InvalidOperationException("Đang trong round đặc biệt rồi.");
        if (match.FestivalScheduled)
            throw new InvalidOperationException("Round sau đã là Lễ hội rồi.");
        if (match.XiDachScheduledUserId.HasValue)
            throw new InvalidOperationException("Round sau đã là Sát Phạt rồi.");
        if (match.StarOfHopeScheduledUserId.HasValue)
            throw new InvalidOperationException("Round sau đã có Ngôi Sao Hi Vọng rồi.");
        if (match.GambleScheduledUserId.HasValue)
            throw new InvalidOperationException("Round sau đã có người Liều Ăn Nhiều rồi.");
        if (match.BreakScheduled)
            throw new InvalidOperationException("Round sau đã là Giải lao rồi.");
    }

    public Match ScheduleFestival(Guid roomId, Guid userId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match) || match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Ván chưa bắt đầu.");
            EnsureNoSpecialScheduled(match);
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
            EnsureNoSpecialScheduled(match);
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
    /// Người đang được mời "Liều Ăn Nhiều" (GambleOfferUserId) chọn Đồng ý/Từ chối. Lời mời hiện trong
    /// ván n+1 (ván ngay sau khi đạt streak) — ván n+1 chơi BÌNH THƯỜNG; accept=true → ván n+2 mới là ván
    /// liều (GambleScheduledUserId). accept=false → bỏ lời mời. Trả lời bất kỳ lúc nào (InProgress/WaitingNextRound).
    /// </summary>
    public Match RespondGamble(Guid roomId, Guid userId, bool accept)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match))
                throw new InvalidOperationException("Trận không tồn tại.");
            if (match.GambleOfferUserId != userId)
                throw new InvalidOperationException("Bạn không có lời mời liều nào.");

            match.GambleOfferUserId = null;
            match.GambleOfferDeadline = null;
            if (accept)
            {
                // Không cho liều nếu round sau đã là biến tấu (an toàn — UpdateWinStreaks đã hoãn lời mời
                // trong trường hợp này, nhưng chặn lần nữa phòng race).
                if (match.FestivalScheduled || match.XiDachScheduledUserId.HasValue || match.StarOfHopeScheduledUserId.HasValue)
                    throw new InvalidOperationException("Round sau đã là round đặc biệt, không thể liều.");
                match.GambleScheduledUserId = userId; // ván KẾ TIẾP (n+2) sẽ là ván liều
            }
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
            EnsureNoSpecialScheduled(match);
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

    /// <summary>Mọi match đang có lời mời Liều Ăn Nhiều treo (để timer scan hết hạn). Offer có thể treo ở ván n+1 (InProgress) hoặc lúc chờ ván mới.</summary>
    public IEnumerable<Match> AllWithGambleOffer() => _matchesByRoom.Values.Where(m => m.GambleOfferUserId.HasValue);

    /// <summary>
    /// Timer: hết hạn lời mời liều (GambleOfferDeadline qua) mà chưa trả lời → auto TỪ CHỐI (clear offer).
    /// Trả về true nếu vừa expire (caller broadcast lại MatchState). Không đụng deal — ván n+1 vẫn chạy bình thường.
    /// </summary>
    public bool TryExpireGambleOffer(Guid roomId)
    {
        lock (LockFor(roomId))
        {
            if (!_matchesByRoom.TryGetValue(roomId, out var match)) return false;
            if (!match.GambleOfferUserId.HasValue || !match.GambleOfferDeadline.HasValue) return false;
            if (match.GambleOfferDeadline.Value > DateTime.UtcNow) return false;
            match.GambleOfferUserId = null;
            match.GambleOfferDeadline = null;
            return true;
        }
    }

    public IEnumerable<Match> AllWhiteWinChoice() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.WhiteWinChoice);

    public IEnumerable<Match> AllPendingTrickCut() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.PendingTrickCut);

    public IEnumerable<Match> AllVoteReset() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.VoteReset);

    public IEnumerable<Match> AllFestivalReveal() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.FestivalReveal);

    public IEnumerable<Match> AllXiDachPlaying() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.XiDachPlaying);

    public IEnumerable<Match> AllBreakRps() => _matchesByRoom.Values.Where(m => m.Status == MatchStatus.BreakRps);

    /// <summary>
    /// Điểm 3 game trắc nghiệm Giải lao (Tính toán/Trí nhớ/Phản xạ), 4 người. Phân nhóm:
    /// W = người đúng ≥1 câu (xếp theo số câu đúng desc, tổng thời gian câu đúng asc), L = 0 câu đúng.
    /// - 0 W → hoà 0 hết.
    /// - 4 W (không có L) → bảng hạng chuẩn +2/+1/-1/-2.
    /// - 1 W → W +6, mỗi L -2.
    /// - 2 W → bậc +3/+1 (tie thời gian → +2/+2), mỗi L -2.
    /// - 3 W → bậc +3/+2/+1 (tie thời gian → chia đều phần các bậc đó), L -6.
    /// Người tie (cùng số câu đúng VÀ cùng tổng thời-gian-đúng) chia đều tổng các bậc họ chiếm. Zero-sum.
    /// </summary>
    private int[] ComputeQuizBreakScores(Match match)
    {
        var n = match.Players.Count;
        var scores = new int[n];
        var answers = match.BreakGame == BreakGameType.Memory ? match.MemoryAnswers
            : match.BreakGame == BreakGameType.Reflex ? match.ReflexAnswers
            : match.BreakGame == BreakGameType.Sudoku ? match.SudokuAnswers
            : match.MathAnswers;

        // (seatIndex, correctCount, totalCorrectMs) cho từng người.
        var stats = new (int idx, int correct, long ms)[n];
        for (int i = 0; i < n; i++)
        {
            answers.TryGetValue(match.Players[i].UserId, out var list);
            list ??= new();
            int correct = list.Count(a => a.Correct);
            long ms = list.Where(a => a.Correct).Sum(a => a.ElapsedMs);
            stats[i] = (i, correct, ms);
        }

        var winners = stats.Where(s => s.correct > 0).ToList();
        int w = winners.Count;
        if (w == 0) return scores;                       // không ai đúng → hoà 0 hết

        // Xếp nhóm thắng: nhiều câu đúng hơn → trước; bằng → tổng thời gian câu đúng ít hơn → trước.
        var sortedW = winners.OrderByDescending(s => s.correct).ThenBy(s => s.ms).ToList();

        if (w == 4)
        {
            // Không có người thua trắng → bảng hạng chuẩn +2/+1/-1/-2 theo thứ tự đã xếp.
            int[] table = { 2, 1, -1, -2 };
            for (int rank = 0; rank < 4; rank++) scores[sortedW[rank].idx] = table[rank];
            return scores;
        }

        // Bậc điểm cho nhóm thắng theo số người thắng (khớp các ví dụ; tổng = -(điểm nhóm thua)).
        int[] tiers = w switch
        {
            1 => new[] { 6 },
            2 => new[] { 3, 1 },
            _ => new[] { 3, 2, 1 },   // w == 3
        };
        int loserScore = w == 3 ? -6 : -2;               // 3W → 1 loser -6; còn lại mỗi loser -2

        // Gán bậc cho winner; người TIE (cùng correct & cùng ms) chia đều tổng các bậc họ chiếm.
        int gi = 0;
        while (gi < sortedW.Count)
        {
            int gj = gi;
            while (gj + 1 < sortedW.Count
                   && sortedW[gj + 1].correct == sortedW[gi].correct
                   && sortedW[gj + 1].ms == sortedW[gi].ms)
                gj++;
            // Nhóm tie [gi..gj] chiếm các bậc tiers[gi..gj] → chia đều (làm tròn, dư cho người xếp trước).
            int sum = 0;
            for (int k = gi; k <= gj; k++) sum += tiers[k];
            int cnt = gj - gi + 1;
            int per = sum / cnt, rem = sum % cnt;
            for (int k = gi; k <= gj; k++)
                scores[sortedW[k].idx] = per + (k - gi < rem ? 1 : 0);
            gi = gj + 1;
        }
        // Người thua (0 đúng).
        for (int i = 0; i < n; i++)
            if (stats[i].correct == 0) scores[i] = loserScore;
        return scores;
    }

    /// <summary>
    /// Điểm game "Cơ hội" (Match Pairs), 4 người, theo SỐ CẶP match. Xếp giảm dần rồi gom thành các TIER bằng nhau.
    /// Bảng điểm theo cấu trúc tier (đều zero-sum; người cùng tier điểm bằng nhau):
    ///  [1,1,1,1] → +2/+1/-1/-2 · [4] (cả 4 bằng) → 0/0/0/0 · [1,3] → +6 / -2 mỗi
    ///  [2,2] → +2/+2 / -2/-2 · [2,1,1] → +2/+2 / -1 / -3 · [3,1] → +2/+2/+2 / -6
    ///  [1,2,1] → +4 / +1/+1 / -6 · [1,1,2] → +3 / +1 / -2/-2.
    /// </summary>
    private int[] ComputeMatchPairsScores(Match match)
    {
        int n = match.Players.Count;
        var scores = new int[n];

        // Sắp xếp người theo số cặp giảm dần (tie-break seat để ổn định), giữ index gốc (theo seat order Players).
        var ordered = Enumerable.Range(0, n)
            .OrderByDescending(i => match.MatchPairsCount.GetValueOrDefault(match.Players[i].UserId))
            .ThenBy(i => match.Players[i].SeatIndex)
            .ToList();
        var counts = ordered.Select(i => match.MatchPairsCount.GetValueOrDefault(match.Players[i].UserId)).ToList();

        // Gom tier theo số cặp bằng nhau (đã giảm dần).
        var tierSizes = new List<int>();
        int k = 0;
        while (k < n)
        {
            int j = k;
            while (j + 1 < n && counts[j + 1] == counts[k]) j++;
            tierSizes.Add(j - k + 1);
            k = j + 1;
        }

        // Điểm cho TỪNG NGƯỜI (theo thứ tự đã xếp hạng) ứng với mỗi mẫu tier.
        string pattern = string.Join(",", tierSizes);
        int[] perRank = pattern switch
        {
            "4" => new[] { 0, 0, 0, 0 },
            "1,1,1,1" => new[] { 2, 1, -1, -2 },
            "1,3" => new[] { 6, -2, -2, -2 },
            "2,2" => new[] { 2, 2, -2, -2 },
            "2,1,1" => new[] { 2, 2, -1, -3 },
            "3,1" => new[] { 2, 2, 2, -6 },
            "1,2,1" => new[] { 4, 1, 1, -6 },
            "1,1,2" => new[] { 3, 1, -2, -2 },
            _ => new[] { 2, 1, -1, -2 }, // fallback an toàn (không nên tới)
        };

        for (int rank = 0; rank < n; rank++)
            scores[ordered[rank]] = perRank[rank];
        return scores;
    }

    /// <summary>
    /// Điểm game "Caro đồng đội": team thắng nhiều cặp hơn (CaroMatchWinnerTeam) mỗi người +2; team thua mỗi người -2.
    /// Hòa (CaroMatchWinnerTeam == 0, mỗi team thắng 1 cặp hoặc 0-0) → tất cả 0. Zero-sum.
    /// </summary>
    private int[] ComputeCaroScores(Match match)
    {
        int n = match.Players.Count;
        var scores = new int[n];
        if (match.CaroMatchWinnerTeam == 0) return scores; // hòa
        for (int i = 0; i < n; i++)
        {
            int team = match.CaroTeam.GetValueOrDefault(match.Players[i].UserId);
            scores[i] = team == match.CaroMatchWinnerTeam ? 2 : -2;
        }
        return scores;
    }

    public int[] ComputeRoundScores(Match match)
    {
        // Returns score for each player in seat order
        var n = match.Players.Count;
        var scores = new int[n];

        // Giải Lao. KHÔNG áp star/liều.
        if (match.IsBreakRound)
        {
            // 4 game đếm "câu đúng" (Tính toán/Trí nhớ/Phản xạ/Trí tuệ): tính theo nhóm THẮNG (đúng ≥1) / THUA (0 đúng).
            // Không ai đúng → hoà 0 hết. Cả 4 đúng → bảng hạng chuẩn +2/+1/-1/-2. Còn lại: bảng đặc biệt theo VD.
            if (match.BreakGame is BreakGameType.Math or BreakGameType.Memory or BreakGameType.Reflex or BreakGameType.Sudoku)
                return ComputeQuizBreakScores(match);

            // Cơ hội (Match Pairs): tính theo SỐ CẶP match từng người (bảng hạng nhóm riêng).
            if (match.BreakGame == BreakGameType.MatchPairs)
                return ComputeMatchPairsScores(match);

            // Caro đồng đội: team thắng +2/người, team thua -2/người; hòa = 0 hết. Zero-sum.
            if (match.BreakGame == BreakGameType.Caro)
                return ComputeCaroScores(match);

            // Oẳn Tù Xì (May mắn): theo hạng bracket 1..4 → +2/+1/-1/-2. Zero-sum.
            int[] breakTable = { 2, 1, -1, -2 };
            for (int i = 0; i < n; i++)
            {
                int rank = (match.Players[i].FinalRank ?? n) - 1;
                scores[i] = breakTable[Math.Clamp(rank, 0, breakTable.Length - 1)];
            }
            return scores;
        }

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
    /// Ngôi Sao Hi Vọng (×2) và Liều Ăn Nhiều (×3) dùng CHUNG mô hình: nhân hệ số mọi GIAO DỊCH điểm
    /// dính 1 player đặc biệt (cả 2 chiều thắng/thua), các giao dịch không dính giữ nguyên. Star ×2,
    /// Liều ×3. Hai cái loại trừ lẫn nhau (1 round chỉ 1). Xem <see cref="ApplyPairwiseMultiplier"/>.
    /// </summary>
    private static int[] ApplyStarOfHopeDoubling(Match match, int[] scores)
    {
        int n = match.Players.Count;
        // Liều Ăn Nhiều: ×3 cho người liều (ưu tiên, loại trừ với star).
        for (int i = 0; i < n; i++) if (match.Players[i].IsGambling) return ApplyPairwiseMultiplier(match, scores, i, 3);
        // Ngôi Sao Hi Vọng: ×2 cho star.
        for (int i = 0; i < n; i++) if (match.Players[i].IsStarOfHope) return ApplyPairwiseMultiplier(match, scores, i, 2);
        return scores;
    }

    /// <summary>
    /// Nhân hệ số <paramref name="multiplier"/> mọi giao dịch điểm dính player <paramref name="specialIdx"/>.
    /// Mô hình "đối tiền theo cặp": phân tách điểm ván thành ma trận giao dịch T[from,to] (from trả to ≥0)
    /// sao cho base[i] = Σ_j (T[j,i] − T[i,j]) (base rank Nhất↔Bét / Nhì↔Ba; chop cutter↔victim; held
    /// chót↔kế trên; 3♠ / về trắng / phán xử theo cặp tương ứng). Mỗi cặp (special, j) được nhân lên
    /// multiplier lần (cộng thêm (multiplier−1) lần chính nó). Vì T zero-sum theo cặp nên kết quả vẫn
    /// zero-sum. Phần phi-zero-sum (residual, vd đui 3♠ khi n&lt;4) chỉ nhân hệ số phần của special.
    /// </summary>
    private static int[] ApplyPairwiseMultiplier(Match match, int[] scores, int specialIdx, int multiplier)
    {
        int n = match.Players.Count;
        if (specialIdx < 0 || multiplier <= 1) return scores;

        var t = BuildTransactionMatrix(match, scores, out int[] residual);

        // Reconcile: residual hấp thụ toàn bộ phần không theo cặp để không bao giờ sai tổng.
        for (int i = 0; i < n; i++)
        {
            int pairNet = 0;
            for (int j = 0; j < n; j++) pairNet += t[j, i] - t[i, j];
            residual[i] = scores[i] - pairNet;
        }

        int extraFactor = multiplier - 1; // ×2 → +1 lần; ×3 → +2 lần.
        var result = (int[])scores.Clone();
        // Nhân hệ số mọi giao dịch theo cặp dính special: cộng thêm extraFactor lần (net j→special).
        for (int j = 0; j < n; j++)
        {
            if (j == specialIdx) continue;
            int netToSpecial = t[j, specialIdx] - t[specialIdx, j]; // dương = j trả special
            result[specialIdx] += extraFactor * netToSpecial;
            result[j] -= extraFactor * netToSpecial;
        }
        // Phần residual (phi-zero-sum, vd đui 3♠ với n<4): nhân hệ số phần của special.
        result[specialIdx] += extraFactor * residual[specialIdx];
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

        UpdateWinStreaks(match);

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
                // Giải lao Tính toán / Trí nhớ: gắn chi tiết từng câu (đúng/sai + thời gian) cho modal tổng kết.
                // Cả 2 game dùng chung kiểu MathAnswer + cùng cột MathResults trong DTO.
                int mathCorrect = 0; long mathTotalMs = 0;
                List<Dtos.MathQuestionResultDto>? mathResults = null;
                var quizAnswers = match.BreakGame == BreakGameType.Memory ? match.MemoryAnswers
                    : match.BreakGame == BreakGameType.Math ? match.MathAnswers
                    : match.BreakGame == BreakGameType.Reflex ? match.ReflexAnswers
                    : match.BreakGame == BreakGameType.Sudoku ? match.SudokuAnswers
                    : null;
                if (match.IsBreakRound && quizAnswers != null && quizAnswers.TryGetValue(p.UserId, out var mAns))
                {
                    mathResults = mAns.Select(a => new Dtos.MathQuestionResultDto(a.Correct, a.Answered, a.ElapsedMs)).ToList();
                    mathCorrect = mAns.Count(a => a.Correct);
                    mathTotalMs = mAns.Where(a => a.Correct).Sum(a => a.ElapsedMs);
                }
                // Cơ hội: tái dùng MathCorrectCount để mang SỐ CẶP match (hiển thị ở modal).
                else if (match.IsBreakRound && match.BreakGame == BreakGameType.MatchPairs)
                {
                    mathCorrect = match.MatchPairsCount.GetValueOrDefault(p.UserId);
                }
                // Caro: tái dùng MathCorrectCount để mang TEAM của người này (1 = X, 2 = O) cho modal hiển thị.
                else if (match.IsBreakRound && match.BreakGame == BreakGameType.Caro)
                {
                    mathCorrect = match.CaroTeam.GetValueOrDefault(p.UserId);
                }
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
                    xdTotal,
                    bd.GambleDelta,
                    p.IsGambling,
                    mathCorrect,
                    mathTotalMs,
                    mathResults);
            })
            .ToList();

        var dto = new Dtos.RoundEndDto(match.Id, match.RoundNumber, wasWhiteWin, match.JudgeTriggered, entries, match.IsFestivalRound, match.IsXiDachRound, match.IsBreakRound, (int)match.BreakGame);
        match.RoundHistory.Add(dto);
        return dto;
    }

    /// <summary>
    /// Cập nhật streak về Nhất sau khi round kết thúc + tự đặt lời mời "Liều Ăn Nhiều" tại MỖI mốc bội-5.
    /// - Round biến tấu (lễ hội / xì dách / giải lao RPS): KHÔNG đụng streak (không tăng, không reset) — KHÔNG tính vào chuỗi.
    /// - Round TLMN thường / về trắng / phán xử: về Nhất (FinalRank==1) → streak++ (KHÔNG cap, đếm 6,7,8…), ngược lại → 0 (reset luôn GambleOfferedAtStreak).
    /// - Player có WinStreak là bội số của 5 (5/10/15…) VÀ chưa mời ở mốc đó (GambleOfferedAtStreak khác) → set GambleOfferUserId.
    ///   KHÔNG reset WinStreak — chuỗi tiếp tục đếm; lần đạt mốc kế (10,15…) lại mời tiếp.
    ///   Nếu round KẾ là biến tấu / đã có lời mời / lịch liều → HOÃN (chưa set GambleOfferedAtStreak) → mời ở round thường kế (streak giữ nguyên qua biến tấu).
    /// </summary>
    private static void UpdateWinStreaks(Match match)
    {
        // Round biến tấu KHÔNG đụng streak (không tăng, không reset) — nhưng VẪN re-check lời mời bên dưới
        // để nếu có streak treo qua round biến tấu thì mời lại ở round thường kế.
        if (!match.IsFestivalRound && !match.IsXiDachRound && !match.IsBreakRound)
        {
            foreach (var p in match.Players)
            {
                if (p.FinalRank == 1) p.WinStreak++;           // KHÔNG cap — đếm vô hạn
                else { p.WinStreak = 0; p.GambleOfferedAtStreak = 0; }
            }
        }

        // Chỉ mời khi hiện không có lời mời / lịch liều / biến tấu nào đang treo (1 lời mời/lúc).
        // Nếu round KẾ đã là biến tấu (festival/xì dách/star đã đặt) → HOÃN: chưa set GambleOfferedAtStreak;
        // lần round-end sau (sau khi biến tấu resolve) sẽ mời lại vì streak vẫn còn mốc bội-5.
        if (match.GambleOfferUserId.HasValue || match.GambleScheduledUserId.HasValue) return;
        if (match.FestivalScheduled || match.XiDachScheduledUserId.HasValue || match.StarOfHopeScheduledUserId.HasValue) return;

        // Mời người vừa đạt mốc bội-5 (5/10/15…) mà chưa từng mời ở mốc đó. Lời mời hiện ở ván KẾ (n+1) — ván n+1
        // vẫn chơi BÌNH THƯỜNG (không chặn deal); đồng ý → ván n+2 mới là ván liều. Lời mời sống tối đa GambleOfferTimeout.
        var hot = match.Players.FirstOrDefault(p =>
            p.WinStreak >= GambleStreakThreshold
            && p.WinStreak % GambleStreakThreshold == 0
            && p.GambleOfferedAtStreak != p.WinStreak);
        if (hot != null)
        {
            match.GambleOfferUserId = hot.UserId;
            match.GambleOfferDeadline = DateTime.UtcNow + GambleOfferTimeout;
            hot.GambleOfferedAtStreak = hot.WinStreak;  // đánh dấu mốc này đã mời (không reset chuỗi — đếm tiếp tới mốc kế)
        }
    }

    public record RoundScoreBreakdown(int BaseRank, int Chop, int ThreeOfSpades, int Judge, int WhiteWin, int HeldPenalty, int Total, int Festival = 0, int StarDelta = 0, int GambleDelta = 0);

    /// <summary>Per-player breakdown of the round score by component (for UI display). StarDelta = phần
    /// chênh do Ngôi Sao Hi Vọng ×2 (doubled total − base total); các component khác là điểm CƠ BẢN.</summary>
    public RoundScoreBreakdown[] ComputeRoundScoreBreakdowns(Match match)
    {
        int n = match.Players.Count;
        var result = new RoundScoreBreakdown[n];

        // Giải Lao (Oẳn Tù Xì): toàn bộ điểm hạng vào Total (UI hiện bảng xếp hạng riêng).
        if (match.IsBreakRound)
        {
            var breakScores = ComputeRoundScores(match);
            for (int i = 0; i < n; i++)
                result[i] = new RoundScoreBreakdown(0, 0, 0, 0, 0, 0, breakScores[i]);
            return result;
        }

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
    /// Không star → StarDelta = 0, Total giữ nguyên. Star &amp; Liều loại trừ lẫn nhau nên xử lý riêng.</summary>
    private RoundScoreBreakdown[] ApplyStarDeltaToBreakdowns(Match match, RoundScoreBreakdown[] bases)
    {
        if (match.IsGambleRound)
            return ApplyGambleDeltaToBreakdowns(match, bases);
        if (!match.Players.Any(p => p.IsStarOfHope)) return bases;
        var doubled = ComputeRoundScores(match);
        for (int i = 0; i < bases.Length; i++)
        {
            int starDelta = doubled[i] - bases[i].Total;
            bases[i] = bases[i] with { StarDelta = starDelta, Total = doubled[i] };
        }
        return bases;
    }

    /// <summary>Gắn GambleDelta = (điểm sau liều ×3) − (tổng base) vào mỗi breakdown; Total = điểm sau liều.
    /// Người liều: GambleDelta = phần thay đổi do ×3; người khác: GambleDelta = phần bù họ gánh/nhận.</summary>
    private RoundScoreBreakdown[] ApplyGambleDeltaToBreakdowns(Match match, RoundScoreBreakdown[] bases)
    {
        var final = ComputeRoundScores(match);
        for (int i = 0; i < bases.Length; i++)
        {
            int gambleDelta = final[i] - bases[i].Total;
            bases[i] = bases[i] with { GambleDelta = gambleDelta, Total = final[i] };
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
