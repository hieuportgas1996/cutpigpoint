namespace CutPig.Dtos;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, Guid UserId, string Username, string DisplayName, bool IsAdmin);
public record MeResponse(Guid UserId, string Username, string DisplayName, bool IsAdmin);

public record AdminUserDto(Guid Id, string Username, string DisplayName, bool IsAdmin, DateTime CreatedAt);
public record AdminCreateUserRequest(string Username, string Password, string? DisplayName, bool IsAdmin);
public record AdminUpdateUserRequest(string? DisplayName, string? Password, bool? IsAdmin);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record CreateRoomRequest(int GameType, int MaxSeats);
public record RoomSummaryDto(Guid Id, string Code, int GameType, int MaxSeats, int Status, int OccupiedSeats, string HostDisplayName, DateTime CreatedAt);
public record RoomSeatDto(int SeatIndex, Guid UserId, string Username, string DisplayName, bool IsHost, bool IsOnline);
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
