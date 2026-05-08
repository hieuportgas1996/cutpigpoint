namespace CutPig.Dtos;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, string Username);
public record MeResponse(string Username);
public record UpdateAccountRequest(string CurrentPassword, string? NewUsername, string? NewPassword);

public record CreatePlayerRequest(string Name, string? Nickname);

public record UpdatePlayerRequest(string Name, string? Nickname);

public record PlayerDto(Guid Id, string Name, string? Nickname, bool HasAvatar);

public record UpdateAvatarRequest(string DataUrl);

public record CreateGameRequest(List<Guid> PlayerIds, int? Type);

public record GamePlayerDto(Guid PlayerId, string Name, int Seat, int TotalScore, bool HasAvatar);

public record GameDto(
    Guid Id,
    int Type,
    DateTime StartedAt,
    DateTime? FinishedAt,
    List<GamePlayerDto> Players,
    List<RoundDto> Rounds);

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
    int Score);

public record RoundDto(
    Guid Id,
    int RoundNumber,
    bool ManualScoring,
    DateTime CreatedAt,
    List<RoundResultDto> Results);
