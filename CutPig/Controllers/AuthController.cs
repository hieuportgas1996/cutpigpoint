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
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(1);

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

        return new LoginResponse(token.Token, token.ExpiresAt, user.Username);
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
        return new MeResponse(user.Username);
    }

    [HttpPut("account")]
    public async Task<ActionResult<MeResponse>> UpdateAccount([FromBody] UpdateAccountRequest req)
    {
        var userId = (Guid?)HttpContext.Items["UserId"];
        if (userId == null) return Unauthorized();
        var user = await _db.AppUsers.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest("Mật khẩu hiện tại không đúng.");

        var changedUsername = !string.IsNullOrWhiteSpace(req.NewUsername) && req.NewUsername != user.Username;
        var changedPassword = !string.IsNullOrWhiteSpace(req.NewPassword);
        if (!changedUsername && !changedPassword)
            return BadRequest("Không có thay đổi nào.");

        if (changedUsername)
        {
            var trimmed = req.NewUsername!.Trim();
            if (trimmed.Length < 3) return BadRequest("Tên đăng nhập tối thiểu 3 ký tự.");
            if (await _db.AppUsers.AnyAsync(u => u.Id != user.Id && u.Username == trimmed))
                return BadRequest("Tên đăng nhập đã tồn tại.");
            user.Username = trimmed;
        }

        if (changedPassword)
        {
            if (req.NewPassword!.Length < 6) return BadRequest("Mật khẩu mới tối thiểu 6 ký tự.");
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            // Đổi password → revoke tất cả token khác
            var others = await _db.AuthTokens.Where(t => t.UserId == user.Id).ToListAsync();
            _db.AuthTokens.RemoveRange(others);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new MeResponse(user.Username);
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
