namespace CutPig.Domain;

public enum RoomStatus
{
    Waiting = 0,
    Playing = 1,
    Finished = 2,
}

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid HostUserId { get; set; }
    public AppUser? HostUser { get; set; }
    public int GameType { get; set; } = 1;
    public int MaxSeats { get; set; } = 4;
    public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? FinalScoresJson { get; set; }
    public bool IsHidden { get; set; }
    public List<RoomSeat> Seats { get; set; } = new();
}

public class RoomSeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public int SeatIndex { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
