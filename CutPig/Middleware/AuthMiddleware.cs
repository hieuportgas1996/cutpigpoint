using CutPig.Data;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var token = header.Substring("Bearer ".Length).Trim();
        var record = await db.AuthTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.Token == token);
        if (record == null || record.ExpiresAt < DateTime.UtcNow)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        context.Items["UserId"] = record.UserId;
        context.Items["Username"] = record.User?.Username ?? string.Empty;
        await _next(context);
    }
}
