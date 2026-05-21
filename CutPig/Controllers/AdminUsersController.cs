using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using CutPig.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminUsersController(AppDbContext db)
    {
        _db = db;
    }

    private bool IsCallerAdmin() => (bool?)HttpContext.Items["IsAdmin"] == true;
    private Guid? CallerId() => (Guid?)HttpContext.Items["UserId"];

    [HttpGet]
    public async Task<ActionResult<List<AdminUserDto>>> List()
    {
        if (!IsCallerAdmin()) return StatusCode(403, "Chỉ admin được phép.");
        var users = await _db.AppUsers
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminUserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin, u.CreatedAt))
            .ToListAsync();
        return users;
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> Create([FromBody] AdminCreateUserRequest req)
    {
        if (!IsCallerAdmin()) return StatusCode(403, "Chỉ admin được phép.");
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Cần nhập tên đăng nhập và mật khẩu.");
        if (req.Password.Length < 4)
            return BadRequest("Mật khẩu phải có ít nhất 4 ký tự.");

        var username = req.Username.Trim();
        var exists = await _db.AppUsers.AnyAsync(u => u.Username == username);
        if (exists) return BadRequest("Tên đăng nhập đã tồn tại.");

        var user = new AppUser
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(req.Password),
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName.Trim(),
            IsAdmin = req.IsAdmin
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return new AdminUserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.CreatedAt);
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<AdminUserDto>> Update(Guid id, [FromBody] AdminUpdateUserRequest req)
    {
        if (!IsCallerAdmin()) return StatusCode(403, "Chỉ admin được phép.");
        var user = await _db.AppUsers.FindAsync(id);
        if (user == null) return NotFound();

        if (req.DisplayName != null)
        {
            var name = req.DisplayName.Trim();
            user.DisplayName = string.IsNullOrEmpty(name) ? user.Username : name;
        }

        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            if (req.Password.Length < 4) return BadRequest("Mật khẩu phải có ít nhất 4 ký tự.");
            user.PasswordHash = PasswordHasher.Hash(req.Password);
            // Invalidate sessions of the target user
            var tokens = await _db.AuthTokens.Where(t => t.UserId == user.Id).ToListAsync();
            if (tokens.Count > 0) _db.AuthTokens.RemoveRange(tokens);
        }

        if (req.IsAdmin.HasValue && req.IsAdmin.Value != user.IsAdmin)
        {
            // Prevent demoting the last admin
            if (!req.IsAdmin.Value && user.IsAdmin)
            {
                var otherAdmins = await _db.AppUsers.CountAsync(u => u.IsAdmin && u.Id != user.Id);
                if (otherAdmins == 0) return BadRequest("Không thể bỏ quyền admin của admin cuối cùng.");
            }
            user.IsAdmin = req.IsAdmin.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new AdminUserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.CreatedAt);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsCallerAdmin()) return StatusCode(403, "Chỉ admin được phép.");
        var caller = CallerId();
        if (caller == id) return BadRequest("Không thể xoá chính tài khoản đang đăng nhập.");

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null) return NotFound();

        if (user.IsAdmin)
        {
            var otherAdmins = await _db.AppUsers.CountAsync(u => u.IsAdmin && u.Id != user.Id);
            if (otherAdmins == 0) return BadRequest("Không thể xoá admin cuối cùng.");
        }

        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
