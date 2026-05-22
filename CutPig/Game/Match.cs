namespace CutPig.GameEngine;

public enum MatchStatus
{
    InProgress = 0,
    Finished = 1,
    WaitingNextRound = 2,        // round ended, waiting for host to start next
    WhiteWinChoice = 3,          // round just dealt, white-win candidates choosing accept/decline
    PendingTrickCut = 4,         // trick about to reset, but someone has 4-pair-run → giving them chance to cut
}

public class MatchPlayer
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool HasAvatar { get; init; }
    public int SeatIndex { get; init; }
    public List<Card> Hand { get; set; } = new();
    public int? FinalRank { get; set; } // 1..N when player finishes current round
    public int TotalScore { get; set; } // cumulative across rounds
    public bool PassedThisTrick { get; set; }
    public string? WhiteWinReason { get; set; }
    public bool? WhiteWinAccepted { get; set; } // null = chưa chọn, true = về trắng, false = từ chối
    public bool FinishedWithThreeOfSpades { get; set; } // last play that emptied the hand contained 3♠
    public bool StuckWithThreeOfSpades { get; set; }   // round ended while this player still held 3♠
    public bool HasPlayedThisRound { get; set; }        // true once player has played at least 1 card this round

    // Judge ("Phán xử") flags — set when 1st player finishes and ≥1 other has not played yet
    public bool JudgeIsWinner { get; set; }             // true if this player triggered the judge by winning #1
    public bool JudgeIsVictim { get; set; }             // true if this player is being judged (didn't play this round)
    public bool JudgeIsPardoned { get; set; }           // true if Case B: judge fired but this player had already played
    public int JudgeHeldValue { get; set; }             // pig/3-pair/four/4-pair value held when judged (only for victims)
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
    public DateTime? WhiteWinDeadline { get; set; }
    public DateTime? TrickCutDeadline { get; set; }
    public Guid? PendingTrickWinnerId { get; set; } // owner of trick that just won, awaiting possible 4-pair-run cut
    public List<Guid> TrickCutCandidates { get; init; } = new(); // users who hold 4-pair-run and can interrupt

    /// <summary>
    /// Chop-pig chain for the current trick: sequence of (playerId, chopValue) for each play in this trick.
    /// Cleared on trick reset. On settle: if chain.Count >= 2, the second-to-last player pays the sum of
    /// chopValue of chain[0..^1] to the last player; intermediate players net zero.
    /// </summary>
    public List<(Guid PlayerId, int ChopValue)> TrickChopChain { get; init; } = new();

    /// <summary>Accumulated chop-pig deltas per player across all tricks of the current round.</summary>
    public Dictionary<Guid, int> RoundChopExtra { get; init; } = new();

    /// <summary>True if this round was decided by "Phán xử" (judge) — winner finished while ≥1 other player had not played yet.</summary>
    public bool JudgeTriggered { get; set; }
}

// (round history persistence reserved for future phase)
