namespace CutPig.Dtos;

public record CreatePlayerRequest(string Name, string? Nickname);

public record UpdatePlayerRequest(string Name, string? Nickname);

public record PlayerDto(Guid Id, string Name, string? Nickname);

public record CreateGameRequest(List<Guid> PlayerIds);

public record GamePlayerDto(Guid PlayerId, string Name, int Seat, int TotalScore);

public record GameDto(
    Guid Id,
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
    bool FourOfAKind,
    bool FourPairsStraight,
    bool WhiteWin,
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
    bool FourOfAKind,
    bool FourPairsStraight,
    bool WhiteWin,
    int Score);

public record RoundDto(
    Guid Id,
    int RoundNumber,
    bool ManualScoring,
    DateTime CreatedAt,
    List<RoundResultDto> Results);
