namespace CutPig.Dtos;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, Guid UserId, string Username, string DisplayName, bool IsAdmin, bool HasAvatar);
public record MeResponse(Guid UserId, string Username, string DisplayName, bool IsAdmin, bool HasAvatar);
public record OnlineUserDto(Guid UserId, string Username, string DisplayName, bool HasAvatar);

public record AdminUserDto(Guid Id, string Username, string DisplayName, bool IsAdmin, DateTime CreatedAt);
public record AdminCreateUserRequest(string Username, string Password, string? DisplayName, bool IsAdmin);
public record AdminUpdateUserRequest(string? DisplayName, string? Password, bool? IsAdmin);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ChangeDisplayNameRequest(string DisplayName);

public record CreateRoomRequest(int GameType, int MaxSeats, string? Name);
public record RoomSummaryDto(Guid Id, string Code, string? Name, int GameType, int MaxSeats, int Status, int OccupiedSeats, string HostDisplayName, DateTime CreatedAt, DateTime? FinishedAt);
public record RoomFinalScoreEntryDto(Guid UserId, string DisplayName, int TotalScore, bool HasAvatar = false);
public record RoomSponsorEntryDto(Guid FromUserId, Guid ToUserId, int Amount);
public record SaveSponsorPlanRequest(List<RoomSponsorEntryDto> Plan);
public record LuckyWheelDto(int Min, int Max, bool Double, int Result, Guid SpinnerUserId);
public record SaveLuckyWheelRequest(int Min, int Max, bool Double, int Result);
public record WheelSpinStartedDto(List<int> Pool, int ResultIndex, int Min, int Max, bool Double, Guid SpinnerUserId);
public record LuckyWheelPreviewDto(List<int> Pool, int Min, int Max, bool Double, Guid SpinnerUserId);
public record RoomHistoryDto(Guid Id, string Code, string? Name, int MaxSeats, string HostDisplayName, DateTime CreatedAt, DateTime? FinishedAt, List<RoomFinalScoreEntryDto> FinalScores, List<RoomSponsorEntryDto>? SponsorPlan = null, LuckyWheelDto? LuckyWheel = null, List<Guid>? SponsorDecidedDonors = null, LuckyWheelPreviewDto? LuckyWheelPreview = null);
public record RoomSeatDto(int SeatIndex, Guid UserId, string Username, string DisplayName, bool IsHost, bool IsOnline, bool HasAvatar);
public record RoomStateDto(
    Guid Id,
    string Code,
    string? Name,
    int GameType,
    int MaxSeats,
    int Status,
    Guid HostUserId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    List<RoomSeatDto> Seats,
    bool ShowOpponentCardCount = true);

public record CardDto(int Rank, int Suit);

public record MatchPlayerDto(
    Guid UserId,
    string DisplayName,
    int SeatIndex,
    int CardsLeft,
    int? FinalRank,
    bool PassedThisTrick,
    int TotalScore,
    string? WhiteWinReason,
    bool? WhiteWinAccepted,
    bool HasAvatar,
    bool Surrendered = false,
    bool? VoteResetChoice = null,
    bool HasUsedVoteReset = false,
    bool HasUsedFestival = false,
    bool FestivalWinner = false,
    int FestivalRevealed = 0,
    List<CardDto?>? FestivalCardSlots = null,
    bool HasUsedStarOfHope = false,
    bool IsStarOfHope = false,
    bool HasUsedXiDach = false,
    bool IsXiDachDealer = false,
    bool XiDachStood = false,
    bool XiDachSettled = false,
    bool XiDachRevealed = false,
    int XiDachVisibleTotal = 0,
    List<CardDto>? XiDachVisibleCards = null,
    int WinStreak = 0,
    bool IsGambling = false,
    int BreakUsedCount = 0);

public record MatchPublicStateDto(
    Guid MatchId,
    Guid RoomId,
    int Status,
    int RoundNumber,
    int CurrentTurnSeatIndex,
    Guid? CurrentTrickOwnerId,
    List<CardDto>? CurrentTrick,
    DateTime TurnDeadline,
    DateTime? NextRoundAt,
    Guid HostUserId,
    List<MatchPlayerDto> Players,
    DateTime? WhiteWinDeadline,
    DateTime? TrickCutDeadline,
    Guid? PendingTrickWinnerId,
    List<Guid>? TrickCutCandidates,
    List<CardDto>? LastWonTrick,
    Guid? LastWonTrickWinnerId,
    bool ShowOpponentCardCount = true,
    DateTime? VoteResetDeadline = null,
    Guid? VoteResetInitiatorId = null,
    bool PastFirstTrick = false,
    bool FestivalScheduled = false,
    bool IsFestivalRound = false,
    Guid? FestivalOrganizerId = null,
    DateTime? FestivalRevealDeadline = null,
    DateTime? FestivalAutoFlipDeadline = null,
    Guid? StarOfHopeScheduledUserId = null,
    Guid? XiDachScheduledUserId = null,
    bool IsXiDachRound = false,
    Guid? XiDachDealerId = null,
    Guid? XiDachTurnUserId = null,
    DateTime? XiDachTurnDeadline = null,
    Guid? GambleOfferUserId = null,
    Guid? GambleScheduledUserId = null,
    bool IsGambleRound = false,
    DateTime? GambleOfferDeadline = null,
    bool BreakScheduled = false,
    Guid? BreakOrganizerId = null,
    bool IsBreakRound = false,
    DateTime? BreakSelectDeadline = null,   // pha BreakSelect: hết hạn người tổ chức chọn game (30s → random)
    DateTime? BreakIntroDeadline = null,    // pha BreakIntro: hết hạn hiện luật (30s → tự bắt đầu)
    RpsStateDto? Rps = null,
    DateTime? RpsChoiceDeadline = null,
    DateTime? RpsRevealUntil = null,
    int BreakGame = 0,                 // BreakGameType: 0 none, 1 Rps, 2 Math, 3 Memory, 4 Reflex
    MathQuizStateDto? Math = null,
    DateTime? MathPickDeadline = null,
    DateTime? MathAnswerDeadline = null,
    DateTime? MathRevealUntil = null,
    MemoryGameStateDto? Memory = null,
    DateTime? MemoryViewDeadline = null,
    DateTime? MemoryAnswerDeadline = null,
    DateTime? MemoryRevealUntil = null,
    ReflexGameStateDto? Reflex = null,
    DateTime? ReflexCooldownUntil = null,
    DateTime? ReflexAnswerDeadline = null,
    DateTime? ReflexRevealUntil = null,
    SudokuGameStateDto? Sudoku = null,
    DateTime? SudokuDeadline = null,
    MatchPairsStateDto? MatchPairs = null,
    DateTime? MatchPairsSpinDeadline = null,
    DateTime? MatchPairsDeadline = null,
    DateTime? MatchPairsMismatchUntil = null,
    DateTime? MatchPairsTurnDeadline = null,
    DateTime? MatchPairsRevealUntil = null,
    CaroStateDto? Caro = null,
    DateTime? CaroSpinDeadline = null,
    DateTime? CaroRevealUntil = null,
    DateTime? CaroTurnDeadline = null,
    DateTime? CaroDeadline = null);

// ---- Giải Lao (Oẳn Tù Xì) ----
public record RpsMatchupDto(
    Guid PlayerAId,
    Guid PlayerBId,
    int WinTarget,
    int WinsA,
    int WinsB,
    Guid? WinnerId,
    Guid? LoserId,
    bool AChosen,           // A đã chọn ván hiện tại chưa (KHÔNG lộ chọn gì)
    bool BChosen,
    int LastChoiceA,        // RpsChoice ván vừa chốt (0=none) — để lật cho xem
    int LastChoiceB,
    int LastOutcome,        // RpsOutcome (0 draw,1 A,2 B)
    bool HasLast);
public record RpsStateDto(
    int Stage,              // RpsStage
    RpsMatchupDto Round1A,
    RpsMatchupDto Round1B,
    RpsMatchupDto ThirdPlace,
    RpsMatchupDto Final,
    List<Guid> FinalRanking);

// ---- Giải Lao (Tính toán) ----
public record MathPickDto(Guid UserId, int Number);  // số người chơi đã chọn (public realtime)
public record MathPlayerResultDto(
    Guid UserId,
    int ChosenIndex,        // -1 = chưa/không trả lời (CHỈ lộ ở pha reveal hoặc cho chính mình)
    bool Answered,
    bool Correct,           // CHỈ ý nghĩa ở pha reveal
    long ElapsedMs,
    int CorrectCount,       // tổng số câu đúng tới thời điểm này
    long TotalCorrectMs);   // tổng thời gian các câu đúng (để hiển thị tốc độ)
// 1 token hiển thị trong biểu thức: lá bài (IsCard, Rank/Suit) hoặc text thuần (toán tử/ngoặc/"0").
public record MathTokenDto(bool IsCard, string Text, int Rank, int Suit);
public record MathQuestionDto(
    string Expression,                  // text thuần (fallback/log)
    List<int> Options,
    int CorrectIndex,                   // CHỈ gửi (≥0) ở pha reveal; -1 khi đang trả lời
    List<MathTokenDto>? ExprTokens = null); // biểu thức dạng token (số 1-9 → lá bài) — client render
public record MathQuizStateDto(
    int Phase,              // 0 = đang chọn số (BreakMathPick), 1 = đang trả lời, 2 = đang hiện đáp án (reveal)
    List<MathPickDto> Picks,
    int TotalQuestions,
    int CurrentQuestion,    // 0-based
    MathQuestionDto? Question,        // câu hiện tại (null trong pha chọn số)
    List<Guid> AnsweredUserIds,       // ai đã trả lời câu hiện tại (không lộ chọn gì)
    List<MathPlayerResultDto> Results,// kết quả tích lũy + (ở reveal) chi tiết câu hiện tại
    List<Guid> FinalRanking);         // chỉ có khi finalize (WaitingNextRound) — thường rỗng trong state

// ---- Giải Lao (Trí nhớ) ----
public record MemoryQuestionDto(
    int CellIndex,          // ô 0-8 đang hỏi
    List<string> Options,   // 4 slug CLB đáp án
    int CorrectIndex);      // -1 khi đang trả lời, ≥0 ở pha reveal
public record MemoryGameStateDto(
    int Phase,              // 0 = xem lưới (view), 1 = đang trả lời, 2 = hiện đáp án (reveal)
    List<string>? Grid,     // 9 slug CLB (CHỈ gửi ở pha view; null khi đang quiz để ẩn)
    int TotalQuestions,
    int CurrentQuestion,
    MemoryQuestionDto? Question,       // câu hiện tại (null pha view)
    string? AnswerSlug,                // slug đúng (CHỈ ở pha reveal)
    List<Guid> AnsweredUserIds,
    List<MathPlayerResultDto> Results);

// ---- Giải Lao (Phản xạ) — bài 52 lá, lưới 4×4, tìm 3 lá ----
public record ReflexGameStateDto(
    int Phase,                  // 0 = cooldown chuẩn bị (lưới ẩn), 1 = đang click, 2 = hiện đáp án (reveal)
    List<CardDto> Grid,         // 16 lá (RỖNG ở pha cooldown để ẩn)
    int TotalRounds,
    int CurrentRound,
    List<CardDto>? TargetCards, // đề: 3 lá cần tìm (CHỈ pha click + reveal; null pha cooldown)
    List<int>? TargetIndexes,   // 3 ô đúng (CHỈ ở pha reveal; null khi đang click/cooldown)
    List<Guid> AnsweredUserIds, // ai đã chốt (đủ 3 lá) — client tự nhớ lá MÌNH đã chọn
    List<MathPlayerResultDto> Results);

// ---- Giải Lao (Trí tuệ) — Sudoku 4×4, chung 1 đề ----
public record SudokuPlayerProgressDto(
    Guid UserId,
    int Filled,              // số ô đã điền (kể cả cho sẵn) — hiển thị tiến độ, KHÔNG lộ giá trị
    bool Solved,             // đã giải xong chưa
    long ElapsedMs);         // thời gian giải (CHỈ ý nghĩa khi Solved)
public record SudokuGameStateDto(
    List<int> Given,         // 16 ô: giá trị 1-4 nếu cho sẵn, 0 nếu ô trống (KHÔNG lộ lời giải)
    int Blanks,              // số ô trống cần điền
    List<SudokuPlayerProgressDto> Progress);  // tiến độ + kết quả từng người

// ---- Giải Lao (Cơ hội) — lật cặp lá bài giống nhau, theo lượt ----
public record MatchPairsPlayerDto(Guid UserId, int Pairs, int TurnOrder);  // số cặp + thứ tự lượt (1-4; 0 nếu chưa quay)
public record MatchPairsStateDto(
    int Phase,                       // 0 = quay thứ tự, 1 = đang chơi
    List<CardDto?> Cells,            // 16 ô: card nếu ĐÃ match hoặc ĐANG lật ngửa; null = còn úp (ẩn)
    List<bool> Matched,              // 16 ô: đã match cố định
    List<int> Flipped,               // các ô đang lật ngửa lượt này (0-2)
    Guid? TurnUserId,                // người đang tới lượt (pha chơi)
    bool Spun,                       // đã quay thứ tự chưa
    List<MatchPairsPlayerDto> Players);  // số cặp + thứ tự lượt từng người

// ---- Giải Lao (Caro đồng đội) — cờ caro 10×10, 4 người chia 2 team, theo lượt ----
public record CaroPlayerDto(Guid UserId, int Team, int TurnOrder, bool DrawVote);  // team 1=X/2=O, thứ tự lượt 1-4 (0 nếu chưa quay), đã xin hòa chưa
public record CaroStateDto(
    int Phase,                       // 0 = quay chia team, 1 = đang chơi
    List<int> Board,                 // 100 ô (10×10): 0 trống, 1 team X, 2 team O
    int LastMove,                    // index ô vừa đặt (-1 nếu chưa)
    Guid? TurnUserId,                // người đang tới lượt (pha chơi)
    int WinnerTeam,                  // 0 = chưa/hòa, 1 = X thắng, 2 = O thắng
    List<int> WinLine,               // index các ô chuỗi thắng (để tô sáng)
    bool Spun,                       // đã quay chia team chưa
    List<CaroPlayerDto> Players);    // team + thứ tự + phiếu hòa từng người

public record PrivateHandDto(Guid MatchId, List<CardDto> Hand);

public record PlayMoveRequest(List<CardDto> Cards);

public record ChatMessageDto(Guid Id, Guid UserId, string DisplayName, string Text, DateTime CreatedAt);
public record ChatHistoryDto(List<ChatMessageDto> Messages);

public record HeldItemsDto(int BlackPigs, int RedPigs, bool HasFourOfAKind, bool HasThreePairRun, bool HasFourPairRun);
public record HeldDetailDto(string Label, int Value);
// MathQuestionResultDto: kết quả 1 câu của 1 người (đúng/sai + thời gian ms) — cho modal tổng kết Tính toán.
public record MathQuestionResultDto(bool Correct, bool Answered, long ElapsedMs);
public record RoundResultEntryDto(Guid UserId, string DisplayName, int FinalRank, int RoundScore, int TotalScore, string? WhiteWinReason, int ChopBonus, bool WonByThreeOfSpades, bool LostByThreeOfSpades, bool JudgeIsWinner, bool JudgeIsVictim, bool JudgeIsPardoned, int JudgeHeldValue, int BaseRankScore, int ThreeOfSpadesDelta, int JudgeDelta, int WhiteWinDelta, int HeldPenaltyDelta, HeldItemsDto Held, List<HeldDetailDto> HeldDetails, int FestivalDelta = 0, bool FestivalWinner = false, List<CardDto>? FestivalCards = null, string? FestivalLabel = null, int StarDelta = 0, bool IsStar = false, List<string>? ChopLabels = null, bool ChopIsCutter = false, List<CardDto>? XiDachCards = null, string? XiDachLabel = null, bool XiDachIsDealer = false, int XiDachTotal = 0, int GambleDelta = 0, bool IsGamble = false, int MathCorrectCount = 0, long MathTotalCorrectMs = 0, List<MathQuestionResultDto>? MathResults = null);
public record RoundEndDto(Guid MatchId, int RoundNumber, bool WasWhiteWin, bool WasJudge, List<RoundResultEntryDto> Results, bool WasFestival = false, bool WasXiDach = false, bool WasBreak = false, int BreakGame = 0);
public record RoundHistoryDto(Guid MatchId, List<RoundEndDto> Rounds);
public record MatchEndDto(Guid MatchId, List<RoundResultEntryDto> FinalScores);

public record CreatePlayerRequest(string Name, string? Nickname);

public record UpdatePlayerRequest(string Name, string? Nickname);

public record PlayerDto(Guid Id, string Name, string? Nickname, bool HasAvatar);

public record UpdateAvatarRequest(string DataUrl);

public record BallConfigDto(int Ball, int Points);

public record CreateGameRequest(List<Guid> PlayerIds, int? Type, List<BallConfigDto>? BallConfig);

public record GamePlayerDto(Guid PlayerId, string Name, int Seat, int TotalScore, bool HasAvatar);

public record GameDto(
    Guid Id,
    int Type,
    DateTime StartedAt,
    DateTime? FinishedAt,
    List<BallConfigDto>? BallConfig,
    List<GamePlayerDto> Players,
    List<RoundDto> Rounds);

public record BallHitDto(int Ball, int Points, Guid VictimPlayerId);

public record PlayerRoundInputDto(
    Guid PlayerId,
    int? Rank,
    int BlackPigsCut,
    int RedPigsCut,
    int BlackPigsLost,
    int RedPigsLost,
    bool ThreePairsStraight,
    Guid? ThreePairsVictimId,
    bool FourOfAKind,
    Guid? FourOfAKindVictimId,
    bool FourPairsStraight,
    Guid? FourPairsVictimId,
    bool WhiteWin,
    bool Judge,
    bool JudgedVictim,
    int BlackPigsHeld,
    int RedPigsHeld,
    bool HasThreePairsHeld,
    bool HasFourOfAKindHeld,
    bool HasFourPairsHeld,
    bool WonByThreeOfSpades,
    bool LostByThreeOfSpades,
    bool BreakAndCleared,
    List<BallHitDto>? BallHits,
    int? ManualScore);

public record CreateRoundRequest(bool ManualScoring, List<PlayerRoundInputDto> Players);

public record RoundResultDto(
    Guid PlayerId,
    int? Rank,
    int BlackPigsCut,
    int RedPigsCut,
    int BlackPigsLost,
    int RedPigsLost,
    bool ThreePairsStraight,
    Guid? ThreePairsVictimId,
    bool FourOfAKind,
    Guid? FourOfAKindVictimId,
    bool FourPairsStraight,
    Guid? FourPairsVictimId,
    bool WhiteWin,
    bool Judge,
    bool JudgedVictim,
    int BlackPigsHeld,
    int RedPigsHeld,
    bool HasThreePairsHeld,
    bool HasFourOfAKindHeld,
    bool HasFourPairsHeld,
    bool WonByThreeOfSpades,
    bool LostByThreeOfSpades,
    bool BreakAndCleared,
    List<BallHitDto>? BallHits,
    int Score);

public record RoundDto(
    Guid Id,
    int RoundNumber,
    bool ManualScoring,
    DateTime CreatedAt,
    List<RoundResultDto> Results);
