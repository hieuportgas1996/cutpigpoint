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

    // Heo (zero-sum 1-vs-1: cut +N, lost -N)
    public int BlackPigsCut { get; set; }
    public int RedPigsCut { get; set; }
    public int BlackPigsLost { get; set; }
    public int RedPigsLost { get; set; }

    // Bonus 1-vs-1: ai ăn = true, victim chịu N điểm
    public bool ThreePairsStraight { get; set; }
    public Guid? ThreePairsVictimId { get; set; }
    public bool FourOfAKind { get; set; }
    public Guid? FourOfAKindVictimId { get; set; }
    public bool FourPairsStraight { get; set; }
    public Guid? FourPairsVictimId { get; set; }

    // Về trắng (round-ending): +6 cho người này, -2 cho 3 người còn lại
    public bool WhiteWin { get; set; }

    // Phán xét: 1 player Judge=true. Nạn nhân JudgedVictim=true.
    // Có thể có 1, 2, hoặc 3 nạn nhân.
    public bool Judge { get; set; }
    public bool JudgedVictim { get; set; }

    // Bài trên tay khi BỊ phán xét bóc (chỉ dùng cho người có JudgedVictim=true)
    public int BlackPigsHeld { get; set; }
    public int RedPigsHeld { get; set; }
    public bool HasThreePairsHeld { get; set; }
    public bool HasFourOfAKindHeld { get; set; }
    public bool HasFourPairsHeld { get; set; }

    // Bida 9 Ball: phá-chấm flag (1 player được +12, 2 còn lại -6).
    public bool BreakAndCleared { get; set; }

    // Bida 9 Ball: JSON list `[{ "ball": int, "points": int, "victimPlayerId": guid }, ...]`
    // — mỗi entry là 1 lần ăn 1 bi tính điểm trong ván, kèm victim bị trừ.
    public string? BallHitsJson { get; set; }

    public int Score { get; set; }
}
