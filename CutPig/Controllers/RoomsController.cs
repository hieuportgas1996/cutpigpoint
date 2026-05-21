using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RoomsController(AppDbContext db)
    {
        _db = db;
    }

    private Guid? CallerId() => (Guid?)HttpContext.Items["UserId"];

    [HttpGet]
    public async Task<ActionResult<List<RoomSummaryDto>>> List()
    {
        var rooms = await _db.Rooms
            .Where(r => r.Status == RoomStatus.Waiting)
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync();

        return rooms.Select(r => new RoomSummaryDto(
            r.Id,
            r.Code,
            r.GameType,
            r.MaxSeats,
            (int)r.Status,
            r.Seats.Count,
            string.IsNullOrWhiteSpace(r.HostUser?.DisplayName) ? (r.HostUser?.Username ?? "") : r.HostUser!.DisplayName,
            r.CreatedAt
        )).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<RoomSummaryDto>> Create([FromBody] CreateRoomRequest req)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        var maxSeats = req.MaxSeats <= 0 ? 4 : Math.Clamp(req.MaxSeats, 2, 4);
        var gameType = req.GameType <= 0 ? 1 : req.GameType;

        string code;
        var attempts = 0;
        do
        {
            code = GenerateCode();
            attempts++;
            if (attempts > 10) return StatusCode(500, "Không tạo được mã phòng. Thử lại.");
        } while (await _db.Rooms.AnyAsync(r => r.Code == code));

        var room = new Room
        {
            Code = code,
            HostUserId = user.Id,
            GameType = gameType,
            MaxSeats = maxSeats,
            Status = RoomStatus.Waiting
        };
        // Host auto-takes seat 0
        room.Seats.Add(new RoomSeat
        {
            RoomId = room.Id,
            SeatIndex = 0,
            UserId = user.Id
        });

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync();

        return new RoomSummaryDto(room.Id, room.Code, room.GameType, room.MaxSeats, (int)room.Status, 1,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName, room.CreatedAt);
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<RoomStateDto>> Get(string code)
    {
        var room = await _db.Rooms
            .Include(r => r.Seats).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant());
        if (room == null) return NotFound();

        var seats = room.Seats
            .OrderBy(s => s.SeatIndex)
            .Select(s => new RoomSeatDto(
                s.SeatIndex,
                s.UserId,
                s.User?.Username ?? "",
                string.IsNullOrWhiteSpace(s.User?.DisplayName) ? (s.User?.Username ?? "") : s.User!.DisplayName,
                s.UserId == room.HostUserId,
                false))
            .ToList();

        return new RoomStateDto(room.Id, room.Code, room.GameType, room.MaxSeats, (int)room.Status,
            room.HostUserId, room.CreatedAt, room.StartedAt, seats);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound();
        if (room.HostUserId != userId) return StatusCode(403, "Chỉ chủ phòng được xoá.");
        if (room.Status == RoomStatus.Playing) return BadRequest("Không thể xoá phòng đang chơi.");

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private static string GenerateCode()
    {
        var rnd = Random.Shared;
        Span<char> chars = stackalloc char[6];
        for (int i = 0; i < 6; i++) chars[i] = CodeAlphabet[rnd.Next(CodeAlphabet.Length)];
        return new string(chars);
    }
}
