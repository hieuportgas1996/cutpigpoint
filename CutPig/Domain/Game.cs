namespace CutPig.Domain;

public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public GameType Type { get; set; } = GameType.TienLenMienNam;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public List<GamePlayer> Players { get; set; } = new();

    public List<GameRound> Rounds { get; set; } = new();
}

public class GamePlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public int Seat { get; set; }
}
