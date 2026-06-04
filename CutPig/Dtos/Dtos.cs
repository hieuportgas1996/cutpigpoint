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
    bool HasUsedBreak = false);

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
    RpsStateDto? Rps = null,
    DateTime? RpsChoiceDeadline = null);

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

public record PrivateHandDto(Guid MatchId, List<CardDto> Hand);

public record PlayMoveRequest(List<CardDto> Cards);

public record ChatMessageDto(Guid Id, Guid UserId, string DisplayName, string Text, DateTime CreatedAt);
public record ChatHistoryDto(List<ChatMessageDto> Messages);

public record HeldItemsDto(int BlackPigs, int RedPigs, bool HasFourOfAKind, bool HasThreePairRun, bool HasFourPairRun);
public record HeldDetailDto(string Label, int Value);
public record RoundResultEntryDto(Guid UserId, string DisplayName, int FinalRank, int RoundScore, int TotalScore, string? WhiteWinReason, int ChopBonus, bool WonByThreeOfSpades, bool LostByThreeOfSpades, bool JudgeIsWinner, bool JudgeIsVictim, bool JudgeIsPardoned, int JudgeHeldValue, int BaseRankScore, int ThreeOfSpadesDelta, int JudgeDelta, int WhiteWinDelta, int HeldPenaltyDelta, HeldItemsDto Held, List<HeldDetailDto> HeldDetails, int FestivalDelta = 0, bool FestivalWinner = false, List<CardDto>? FestivalCards = null, string? FestivalLabel = null, int StarDelta = 0, bool IsStar = false, List<string>? ChopLabels = null, bool ChopIsCutter = false, List<CardDto>? XiDachCards = null, string? XiDachLabel = null, bool XiDachIsDealer = false, int XiDachTotal = 0, int GambleDelta = 0, bool IsGamble = false);
public record RoundEndDto(Guid MatchId, int RoundNumber, bool WasWhiteWin, bool WasJudge, List<RoundResultEntryDto> Results, bool WasFestival = false, bool WasXiDach = false, bool WasBreak = false);
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
