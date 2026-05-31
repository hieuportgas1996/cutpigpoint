using System.Security.Cryptography;
using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using CutPig.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(4);

    private readonly AppDbContext _db;

    public AuthController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Cần nhập tên đăng nhập và mật khẩu.");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Sai tên đăng nhập hoặc mật khẩu.");

        // Cleanup expired tokens for this user opportunistically
        var now = DateTime.UtcNow;
        var expired = await _db.AuthTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now).ToListAsync();
        if (expired.Count > 0) _db.AuthTokens.RemoveRange(expired);

        var token = new AuthToken
        {
            UserId = user.Id,
            Token = GenerateToken(),
            ExpiresAt = now.Add(TokenLifetime)
        };
        _db.AuthTokens.Add(token);
        await _db.SaveChangesAsync();

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        return new LoginResponse(token.Token, token.ExpiresAt, user.Id, user.Username, displayName, user.IsAdmin, !string.IsNullOrEmpty(user.AvatarData));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 4)
            return BadRequest("Mật khẩu mới phải có ít nhất 4 ký tự.");

        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();
        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest("Mật khẩu hiện tại không đúng.");

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Invalidate all other tokens for this user (force re-login on other devices)
        var existingToken = ExtractToken();
        var others = await _db.AuthTokens.Where(t => t.UserId == user.Id && t.Token != existingToken).ToListAsync();
        if (others.Count > 0) _db.AuthTokens.RemoveRange(others);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("change-display-name")]
    public async Task<ActionResult<MeResponse>> ChangeDisplayName([FromBody] ChangeDisplayNameRequest req)
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();

        var displayName = req?.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
            return BadRequest("Tên hiển thị không được để trống.");
        if (displayName.Length > 40)
            return BadRequest("Tên hiển thị tối đa 40 ký tự.");

        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        user.DisplayName = displayName;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new MeResponse(user.Id, user.Username, user.DisplayName, user.IsAdmin, !string.IsNullOrEmpty(user.AvatarData));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = ExtractToken();
        if (token != null)
        {
            var record = await _db.AuthTokens.FirstOrDefaultAsync(t => t.Token == token);
            if (record != null)
            {
                _db.AuthTokens.Remove(record);
                await _db.SaveChangesAsync();
            }
        }
        return NoContent();
    }

    [HttpGet("online-users")]
    public async Task<ActionResult<List<OnlineUserDto>>> OnlineUsers()
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();

        var now = DateTime.UtcNow;
        var users = await _db.AuthTokens
            .Where(t => t.ExpiresAt > now)
            .Select(t => t.UserId)
            .Distinct()
            .Join(_db.AppUsers, id => id, u => u.Id, (id, u) => u)
            .OrderBy(u => u.DisplayName)
            .Select(u => new OnlineUserDto(
                u.Id,
                u.Username,
                string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName,
                !string.IsNullOrEmpty(u.AvatarData)))
            .ToListAsync();

        return users;
    }

    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        return new MeResponse(user.Id, user.Username, displayName, user.IsAdmin, !string.IsNullOrEmpty(user.AvatarData));
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> SetAvatar([FromBody] UpdateAvatarRequest req)
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req?.DataUrl)) return BadRequest("Avatar không hợp lệ.");
        if (!AvatarHelpers.TryParseDataUrl(req.DataUrl, out _, out var bytes))
            return BadRequest("Định dạng ảnh không hỗ trợ (chỉ JPEG/PNG/WEBP).");
        if (bytes.Length > AvatarHelpers.MaxAvatarBytes)
            return BadRequest($"Avatar phải nhỏ hơn {AvatarHelpers.MaxAvatarBytes / 1024} KB.");

        user.AvatarData = req.DataUrl;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();
        user.AvatarData = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private string? ExtractToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return header.Substring("Bearer ".Length).Trim();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
