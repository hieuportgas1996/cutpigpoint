namespace CutPig.GameEngine;

public enum MatchStatus
{
    InProgress = 0,
    Finished = 1,
    WaitingNextRound = 2, // round ended, waiting for host to start next
}

public class MatchPlayer
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int SeatIndex { get; init; }
    public List<Card> Hand { get; set; } = new();
    public int? FinalRank { get; set; } // 1..N when player finishes current round
    public int TotalScore { get; set; } // cumulative across rounds
    public bool PassedThisTrick { get; set; }
    public string? WhiteWinReason { get; set; }
}

public class Match
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RoomId { get; init; }
    public Guid HostUserId { get; init; }
    public List<MatchPlayer> Players { get; init; } = new();
    public int CurrentTurnSeatIndex { get; set; }
    public Combo? CurrentTrick { get; set; }
    public Guid? CurrentTrickOwnerId { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.InProgress;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime TurnDeadline { get; set; }
    public int FinishedCount { get; set; }
    public List<Guid> FinishOrder { get; init; } = new();
    public int RoundNumber { get; set; } = 1;
    public Guid? PreviousRoundWinnerId { get; set; }
    public DateTime? NextRoundAt { get; set; }
}

// (round history persistence reserved for future phase)
