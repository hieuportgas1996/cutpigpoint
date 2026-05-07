namespace CutPig.Domain;

public class GameRound
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    public int RoundNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool ManualScoring { get; set; }

    public List<RoundResult> Results { get; set; } = new();
}

public class RoundResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GameRoundId { get; set; }
    public GameRound? GameRound { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public int? Rank { get; set; }

    public int BlackPigsCut { get; set; }
    public int RedPigsCut { get; set; }
    public int BlackPigsLost { get; set; }
    public int RedPigsLost { get; set; }

    public bool ThreePairsStraight { get; set; }
    public bool FourOfAKind { get; set; }
    public bool FourPairsStraight { get; set; }
    public bool WhiteWin { get; set; }

    public int Score { get; set; }
}
