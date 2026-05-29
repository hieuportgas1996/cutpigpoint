using System.Text.Json;
using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using CutPig.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<RoomHub> _hub;

    public RoomsController(AppDbContext db, IHubContext<RoomHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private Guid? CallerId() => (Guid?)HttpContext.Items["UserId"];

    private static RoomHistoryDto BuildHistoryDto(Room room)
    {
        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        List<RoomSponsorEntryDto>? sponsor = null;
        if (!string.IsNullOrEmpty(room.SponsorPlanJson))
        {
            try { sponsor = JsonSerializer.Deserialize<List<RoomSponsorEntryDto>>(room.SponsorPlanJson); } catch { }
        }
        List<Guid>? decided = null;
        if (!string.IsNullOrEmpty(room.SponsorDecisionsJson))
        {
            try { decided = JsonSerializer.Deserialize<List<Guid>>(room.SponsorDecisionsJson); } catch { }
        }
        LuckyWheelDto? wheel = null;
        if (!string.IsNullOrEmpty(room.LuckyWheelJson))
        {
            try { wheel = JsonSerializer.Deserialize<LuckyWheelDto>(room.LuckyWheelJson); } catch { }
        }
        LuckyWheelPreviewDto? preview = null;
        if (!string.IsNullOrEmpty(room.LuckyWheelPreviewJson))
        {
            try { preview = JsonSerializer.Deserialize<LuckyWheelPreviewDto>(room.LuckyWheelPreviewJson); } catch { }
        }
        return new RoomHistoryDto(
            room.Id,
            room.Code,
            room.Name,
            room.MaxSeats,
            string.IsNullOrWhiteSpace(room.HostUser?.DisplayName) ? (room.HostUser?.Username ?? "") : room.HostUser!.DisplayName,
            room.CreatedAt,
            room.FinishedAt,
            scores,
            sponsor,
            wheel,
            decided,
            preview
        );
    }

    private Task BroadcastHistoryAsync(Room room)
        => _hub.Clients.Group($"room:{room.Id}").SendAsync("HistoryUpdated", BuildHistoryDto(room));

    [HttpGet]
    public async Task<ActionResult<List<RoomSummaryDto>>> List()
    {
        var isAdmin = (bool?)HttpContext.Items["IsAdmin"] == true;
        var query = _db.Rooms.AsQueryable().Where(r => !r.IsHidden);
        if (!isAdmin) query = query.Where(r => r.Status == RoomStatus.Waiting);

        var rooms = await query
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync();

        return rooms.Select(r => new RoomSummaryDto(
            r.Id,
            r.Code,
            r.Name,
            r.GameType,
            r.MaxSeats,
            (int)r.Status,
            r.Seats.Count,
            string.IsNullOrWhiteSpace(r.HostUser?.DisplayName) ? (r.HostUser?.Username ?? "") : r.HostUser!.DisplayName,
            r.CreatedAt,
            r.FinishedAt
        )).ToList();
    }

    [HttpGet("history/{code}")]
    public async Task<ActionResult<RoomHistoryDto>> HistoryDetail(string code)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var isAdmin = (bool?)HttpContext.Items["IsAdmin"] == true;

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == RoomStatus.Finished);
        if (room == null) return NotFound();

        if (!isAdmin && !room.Seats.Any(s => s.UserId == userId.Value))
            return StatusCode(403, "Không có quyền xem phòng này.");

        return BuildHistoryDto(room);
    }

    /// <summary>Lưu sponsor plan: Nhất/Nhì (điểm > 0) chia điểm cho người điểm âm. Chỉ player từng ngồi trong phòng được lưu, và chỉ lưu được 1 lần.</summary>
    [HttpPut("history/{code}/sponsor")]
    public async Task<ActionResult<RoomHistoryDto>> SaveSponsorPlan(string code, [FromBody] SaveSponsorPlanRequest req)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == RoomStatus.Finished);
        if (room == null) return NotFound();
        if (!room.Seats.Any(s => s.UserId == userId.Value)) return StatusCode(403, "Không có quyền chỉnh phòng này.");

        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        if (scores.Count == 0) return BadRequest("Phòng chưa có bảng điểm.");
        var orderedDesc = scores.OrderByDescending(s => s.TotalScore).ToList();
        var top1 = orderedDesc.ElementAtOrDefault(0);
        var top2 = orderedDesc.ElementAtOrDefault(1);
        var allowedDonors = new HashSet<Guid>();
        if (top1 != null && top1.TotalScore > 0) allowedDonors.Add(top1.UserId);
        if (top2 != null && top2.TotalScore > 0) allowedDonors.Add(top2.UserId);
        var allowedRecipients = scores.Where(s => s.TotalScore < 0).Select(s => s.UserId).ToHashSet();

        var plan = req.Plan ?? new();
        // Validate
        var donorTotals = new Dictionary<Guid, int>();
        foreach (var e in plan)
        {
            if (e.Amount <= 0) return BadRequest("Số điểm phải > 0.");
            if (!allowedDonors.Contains(e.FromUserId)) return BadRequest("Chỉ Nhất/Nhì có điểm dương được sponsor.");
            if (!allowedRecipients.Contains(e.ToUserId)) return BadRequest("Chỉ chuyển được cho người điểm âm.");
            if (e.FromUserId == e.ToUserId) return BadRequest("Không thể chuyển cho chính mình.");
            donorTotals.TryGetValue(e.FromUserId, out var cur);
            donorTotals[e.FromUserId] = cur + e.Amount;
        }
        foreach (var (donorId, total) in donorTotals)
        {
            var donor = scores.First(s => s.UserId == donorId);
            if (total > donor.TotalScore) return BadRequest($"{donor.DisplayName} chỉ có {donor.TotalScore} điểm để sponsor.");
        }
        // Chỉ donor được phép tự lưu plan của chính mình. Cộng vào plan hiện có (mỗi donor lưu phần của mình).
        if (!allowedDonors.Contains(userId.Value)) return StatusCode(403, "Chỉ Nhất hoặc Nhì có điểm dương mới được sponsor.");
        if (plan.Any(e => e.FromUserId != userId.Value)) return BadRequest("Chỉ được lưu phần sponsor của chính mình.");

        List<RoomSponsorEntryDto> existing = new();
        if (!string.IsNullOrEmpty(room.SponsorPlanJson))
        {
            try { existing = JsonSerializer.Deserialize<List<RoomSponsorEntryDto>>(room.SponsorPlanJson) ?? new(); } catch { }
        }
        // Replace caller's entries (cho phép sửa lại nếu chưa ai quay vòng — đơn giản: ghi đè phần của mình).
        var merged = existing.Where(e => e.FromUserId != userId.Value).Concat(plan).ToList();
        room.SponsorPlanJson = JsonSerializer.Serialize(merged);

        // Track that this donor has decided (gồm cả khi plan rỗng cho lần sau bấm Skip).
        var decided = new HashSet<Guid>();
        if (!string.IsNullOrEmpty(room.SponsorDecisionsJson))
        {
            try { decided = (JsonSerializer.Deserialize<List<Guid>>(room.SponsorDecisionsJson) ?? new()).ToHashSet(); } catch { }
        }
        decided.Add(userId.Value);
        room.SponsorDecisionsJson = JsonSerializer.Serialize(decided.ToList());

        await _db.SaveChangesAsync();

        await BroadcastHistoryAsync(room);
        return BuildHistoryDto(room);
    }

    /// <summary>Bỏ qua sponsor cho donor hiện tại (xoá plan đã có của họ + đánh dấu đã quyết định).</summary>
    [HttpPost("history/{code}/sponsor/skip")]
    public async Task<ActionResult<RoomHistoryDto>> SkipSponsor(string code)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == RoomStatus.Finished);
        if (room == null) return NotFound();
        if (!room.Seats.Any(s => s.UserId == userId.Value)) return StatusCode(403, "Không có quyền chỉnh phòng này.");

        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        var orderedDesc = scores.OrderByDescending(s => s.TotalScore).ToList();
        var top1 = orderedDesc.ElementAtOrDefault(0);
        var top2 = orderedDesc.ElementAtOrDefault(1);
        var allowedDonors = new HashSet<Guid>();
        if (top1 != null && top1.TotalScore > 0) allowedDonors.Add(top1.UserId);
        if (top2 != null && top2.TotalScore > 0) allowedDonors.Add(top2.UserId);
        if (!allowedDonors.Contains(userId.Value)) return StatusCode(403, "Chỉ Nhất hoặc Nhì có điểm dương mới được bỏ qua sponsor.");

        // Clear caller's plan + mark decided.
        List<RoomSponsorEntryDto> existing = new();
        if (!string.IsNullOrEmpty(room.SponsorPlanJson))
        {
            try { existing = JsonSerializer.Deserialize<List<RoomSponsorEntryDto>>(room.SponsorPlanJson) ?? new(); } catch { }
        }
        room.SponsorPlanJson = JsonSerializer.Serialize(existing.Where(e => e.FromUserId != userId.Value).ToList());

        var decided = new HashSet<Guid>();
        if (!string.IsNullOrEmpty(room.SponsorDecisionsJson))
        {
            try { decided = (JsonSerializer.Deserialize<List<Guid>>(room.SponsorDecisionsJson) ?? new()).ToHashSet(); } catch { }
        }
        decided.Add(userId.Value);
        room.SponsorDecisionsJson = JsonSerializer.Serialize(decided.ToList());

        await _db.SaveChangesAsync();
        await BroadcastHistoryAsync(room);
        return BuildHistoryDto(room);
    }

    /// <summary>Lưu kết quả vòng quay may mắn. Chỉ player hạng bét (điểm gốc thấp nhất) được lưu, và chỉ 1 lần / phòng.</summary>
    [HttpPut("history/{code}/wheel")]
    public async Task<ActionResult<RoomHistoryDto>> SaveLuckyWheel(string code, [FromBody] SaveLuckyWheelRequest req)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();

        var room = await _db.Rooms
            .Include(r => r.HostUser)
            .Include(r => r.Seats)
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant() && r.Status == RoomStatus.Finished);
        if (room == null) return NotFound();
        if (!room.Seats.Any(s => s.UserId == userId.Value)) return StatusCode(403, "Không có quyền chỉnh phòng này.");
        if (!string.IsNullOrEmpty(room.LuckyWheelJson)) return BadRequest("Vòng quay đã có kết quả rồi.");

        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        if (scores.Count == 0) return BadRequest("Phòng chưa có bảng điểm.");

        // Người hạng bét theo điểm gốc (chưa áp sponsor).
        var spinner = scores.OrderBy(s => s.TotalScore).First();
        if (spinner.UserId != userId.Value) return StatusCode(403, "Chỉ người hạng bét được quay vòng.");

        if (req.Min < 1) return BadRequest("Min phải ≥ 1.");
        if (req.Max < req.Min) return BadRequest("Max phải ≥ Min.");
        if (req.Max > 1000) return BadRequest("Max quá lớn (≤ 1000).");
        if (req.Result < req.Min || req.Result > req.Max) return BadRequest("Kết quả ngoài khoảng cho phép.");

        var dto = new LuckyWheelDto(req.Min, req.Max, req.Double, req.Result, userId.Value);
        room.LuckyWheelJson = JsonSerializer.Serialize(dto);
        await _db.SaveChangesAsync();

        await BroadcastHistoryAsync(room);
        return BuildHistoryDto(room);
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<RoomHistoryDto>>> History()
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var isAdmin = (bool?)HttpContext.Items["IsAdmin"] == true;

        var query = _db.Rooms.AsQueryable().Where(r => r.Status == RoomStatus.Finished);
        if (!isAdmin)
        {
            query = query.Where(r => r.Seats.Any(s => s.UserId == userId));
        }

        var rooms = await query
            .Include(r => r.HostUser)
            .OrderByDescending(r => r.FinishedAt ?? r.CreatedAt)
            .Take(50)
            .ToListAsync();

        return rooms.Select(r =>
        {
            List<RoomFinalScoreEntryDto> scores = new();
            if (!string.IsNullOrEmpty(r.FinalScoresJson))
            {
                try
                {
                    scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(r.FinalScoresJson) ?? new();
                }
                catch { }
            }
            List<RoomSponsorEntryDto>? sponsor = null;
            if (!string.IsNullOrEmpty(r.SponsorPlanJson))
            {
                try { sponsor = JsonSerializer.Deserialize<List<RoomSponsorEntryDto>>(r.SponsorPlanJson); } catch { }
            }
            LuckyWheelDto? wheel = null;
            if (!string.IsNullOrEmpty(r.LuckyWheelJson))
            {
                try { wheel = JsonSerializer.Deserialize<LuckyWheelDto>(r.LuckyWheelJson); } catch { }
            }
            return new RoomHistoryDto(
                r.Id,
                r.Code,
                r.Name,
                r.MaxSeats,
                string.IsNullOrWhiteSpace(r.HostUser?.DisplayName) ? (r.HostUser?.Username ?? "") : r.HostUser!.DisplayName,
                r.CreatedAt,
                r.FinishedAt,
                scores,
                sponsor,
                wheel
            );
        }).ToList();
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

        var trimmedName = req.Name?.Trim();
        if (!string.IsNullOrEmpty(trimmedName) && trimmedName.Length > 50)
            trimmedName = trimmedName.Substring(0, 50);

        var room = new Room
        {
            Code = code,
            Name = string.IsNullOrEmpty(trimmedName) ? null : trimmedName,
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

        return new RoomSummaryDto(room.Id, room.Code, room.Name, room.GameType, room.MaxSeats, (int)room.Status, 1,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName, room.CreatedAt, room.FinishedAt);
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
                false,
                !string.IsNullOrEmpty(s.User?.AvatarData)))
            .ToList();

        return new RoomStateDto(room.Id, room.Code, room.Name, room.GameType, room.MaxSeats, (int)room.Status,
            room.HostUserId, room.CreatedAt, room.StartedAt, seats);
    }

    [HttpDelete("history/{id}")]
    public async Task<IActionResult> DeleteHistory(Guid id)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var isAdmin = (bool?)HttpContext.Items["IsAdmin"] == true;
        if (!isAdmin) return StatusCode(403, "Chỉ admin mới được xoá lịch sử.");

        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound();
        if (room.Status != RoomStatus.Finished) return BadRequest("Chỉ xoá được lịch sử phòng đã kết thúc.");

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CallerId();
        if (userId == null) return Unauthorized();
        var isAdmin = (bool?)HttpContext.Items["IsAdmin"] == true;

        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound();

        if (!isAdmin)
        {
            if (room.HostUserId != userId) return StatusCode(403, "Chỉ chủ phòng được xoá.");
            if (room.Status == RoomStatus.Playing) return BadRequest("Không thể xoá phòng đang chơi.");
        }
        // Admin: can delete any room, any status

        // Soft-delete phòng đã kết thúc để giữ lịch sử điểm
        if (room.Status == RoomStatus.Finished)
        {
            room.IsHidden = true;
        }
        else
        {
            _db.Rooms.Remove(room);
        }
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
