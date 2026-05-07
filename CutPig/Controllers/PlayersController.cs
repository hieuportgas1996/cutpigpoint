using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    private const int MaxAvatarBytes = 200_000;

    private readonly AppDbContext _db;
    public PlayersController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlayerDto>>> List()
    {
        var players = await _db.Players
            .OrderBy(p => p.Name)
            .Select(p => new PlayerDto(p.Id, p.Name, p.Nickname, p.AvatarData != null))
            .ToListAsync();
        return Ok(players);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlayerDto>> Get(Guid id)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        return new PlayerDto(p.Id, p.Name, p.Nickname, p.AvatarData != null);
    }

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create([FromBody] CreatePlayerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required");
        var p = new Player { Name = req.Name.Trim(), Nickname = req.Nickname?.Trim() };
        _db.Players.Add(p);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id }, new PlayerDto(p.Id, p.Name, p.Nickname, false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlayerDto>> Update(Guid id, [FromBody] UpdatePlayerRequest req)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required");
        p.Name = req.Name.Trim();
        p.Nickname = req.Nickname?.Trim();
        await _db.SaveChangesAsync();
        return new PlayerDto(p.Id, p.Name, p.Nickname, p.AvatarData != null);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        _db.Players.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid id)
    {
        var p = await _db.Players
            .Where(x => x.Id == id)
            .Select(x => new { x.AvatarData })
            .FirstOrDefaultAsync();
        if (p == null || string.IsNullOrEmpty(p.AvatarData)) return NotFound();

        if (!TryParseDataUrl(p.AvatarData, out var contentType, out var bytes))
            return NotFound();

        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(bytes, contentType);
    }

    [HttpPut("{id:guid}/avatar")]
    public async Task<IActionResult> SetAvatar(Guid id, [FromBody] UpdateAvatarRequest req)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.DataUrl)) return BadRequest("DataUrl is required");
        if (!TryParseDataUrl(req.DataUrl, out _, out var bytes))
            return BadRequest("Invalid data URL");
        if (bytes.Length > MaxAvatarBytes)
            return BadRequest($"Avatar must be smaller than {MaxAvatarBytes / 1024} KB");

        p.AvatarData = req.DataUrl;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}/avatar")]
    public async Task<IActionResult> DeleteAvatar(Guid id)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        p.AvatarData = null;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static bool TryParseDataUrl(string dataUrl, out string contentType, out byte[] bytes)
    {
        contentType = "application/octet-stream";
        bytes = Array.Empty<byte>();

        const string prefix = "data:";
        if (!dataUrl.StartsWith(prefix)) return false;
        var commaIdx = dataUrl.IndexOf(',');
        if (commaIdx < 0) return false;
        var meta = dataUrl.Substring(prefix.Length, commaIdx - prefix.Length);
        var payload = dataUrl.Substring(commaIdx + 1);

        var parts = meta.Split(';');
        if (parts.Length < 1) return false;
        contentType = string.IsNullOrEmpty(parts[0]) ? "image/png" : parts[0];
        var isBase64 = parts.Skip(1).Any(p => p.Equals("base64", StringComparison.OrdinalIgnoreCase));
        if (!isBase64) return false;

        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch
        {
            return false;
        }

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(contentType)) return false;

        return true;
    }
}
