using CutPig.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) { _db = db; }

    /// <summary>Public avatar fetch (no auth) — used by &lt;img src&gt;.</summary>
    [HttpGet("{id:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid id)
    {
        var u = await _db.AppUsers
            .Where(x => x.Id == id)
            .Select(x => new { x.AvatarData })
            .FirstOrDefaultAsync();
        if (u == null || string.IsNullOrEmpty(u.AvatarData)) return NotFound();

        if (!AvatarHelpers.TryParseDataUrl(u.AvatarData, out var contentType, out var bytes))
            return NotFound();

        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(bytes, contentType);
    }
}

internal static class AvatarHelpers
{
    public const int MaxAvatarBytes = 200_000;

    public static bool TryParseDataUrl(string dataUrl, out string contentType, out byte[] bytes)
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

        try { bytes = Convert.FromBase64String(payload); }
        catch { return false; }

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(contentType)) return false;
        return true;
    }
}
