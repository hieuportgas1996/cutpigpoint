namespace CutPig.Dtos;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, Guid UserId, string Username, string DisplayName, bool IsAdmin, bool HasAvatar);
public record MeResponse(Guid UserId, string Username, string DisplayName, bool IsAdmin, bool HasAvatar);

public record AdminUserDto(Guid Id, string Username, string DisplayName, bool IsAdmin, DateTime CreatedAt);
public record AdminCreateUserRequest(string Username, string Password, string? DisplayName, bool IsAdmin);
public record AdminUpdateUserRequest(string? DisplayName, string? Password, bool? IsAdmin);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record CreateRoomRequest(int GameType, int MaxSeats);
public record RoomSummaryDto(Guid Id, string Code, int GameType, int MaxSeats, int Status, int OccupiedSeats, string HostDisplayName, DateTime CreatedAt);
public record RoomSeatDto(int SeatIndex, Guid UserId, string Username, string DisplayName, bool IsHost, bool IsOnline, bool HasAvatar);
public record RoomStateDto(
    Guid Id,
    string Code,
    int GameType,
    int MaxSeats,
    int Status,
    Guid HostUserId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    List<RoomSeatDto> Seats);

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
    bool HasAvatar);

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
    List<Guid>? TrickCutCandidates);

public record PrivateHandDto(Guid MatchId, List<CardDto> Hand);

public record PlayMoveRequest(List<CardDto> Cards);

public record ChatMessageDto(Guid Id, Guid UserId, string DisplayName, string Text, DateTime CreatedAt);
public record ChatHistoryDto(List<ChatMessageDto> Messages);

public record HeldItemsDto(int BlackPigs, int RedPigs, bool HasFourOfAKind, bool HasThreePairRun, bool HasFourPairRun);
public record RoundResultEntryDto(Guid UserId, string DisplayName, int FinalRank, int RoundScore, int TotalScore, string? WhiteWinReason, int ChopBonus, bool WonByThreeOfSpades, bool LostByThreeOfSpades, bool JudgeIsWinner, bool JudgeIsVictim, bool JudgeIsPardoned, int JudgeHeldValue, int BaseRankScore, int ThreeOfSpadesDelta, int JudgeDelta, int WhiteWinDelta, HeldItemsDto Held);
public record RoundEndDto(Guid MatchId, int RoundNumber, bool WasWhiteWin, bool WasJudge, List<RoundResultEntryDto> Results);
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
