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
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

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
        return new LoginResponse(token.Token, token.ExpiresAt, user.Id, user.Username, displayName, user.IsAdmin);
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

    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        return new MeResponse(user.Id, user.Username, displayName, user.IsAdmin);
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
