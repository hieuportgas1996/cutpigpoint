using CutPig.Data;
using CutPig.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = ResolveConnectionString(builder.Configuration);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("WARN: No DATABASE_URL or ConnectionStrings:DefaultConnection configured. App will start but DB calls will fail.");
    connectionString = "Host=localhost;Port=5432;Database=placeholder;Username=placeholder;Password=placeholder";
}

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));

builder.Services.AddScoped<TienLenScoringService>();

var allowedOrigins = ResolveAllowedOrigins(builder.Configuration);

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("AllowFrontend", p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
Console.WriteLine($"Binding to http://0.0.0.0:{port}");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        logger.LogInformation("Database ready.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed; starting app anyway. /health will report degraded.");
    }
}

app.UseCors("AllowFrontend");
app.MapControllers();
app.MapGet("/", () => "CutPigPoint API is running.");
app.MapGet("/health", (AppDbContext db) =>
{
    try
    {
        var canConnect = db.Database.CanConnect();
        return canConnect
            ? Results.Ok(new { status = "ok", db = "connected" })
            : Results.Json(new { status = "degraded", db = "unreachable" }, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "degraded", db = "error", message = ex.Message }, statusCode: 503);
    }
});

Console.WriteLine("CutPigPoint API starting...");
app.Run();

static string? ResolveConnectionString(IConfiguration config)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        try
        {
            return BuildNpgsqlConnectionString(databaseUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARN: Failed to parse DATABASE_URL: {ex.Message}");
            return null;
        }
    }
    return config.GetConnectionString("DefaultConnection");
}

static string BuildNpgsqlConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port > 0 ? uri.Port : 5432;
    var sslMode = Environment.GetEnvironmentVariable("PGSSLMODE") ?? "Require";
    return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SSL Mode={sslMode};Trust Server Certificate=true";
}

static string[] ResolveAllowedOrigins(IConfiguration config)
{
    var origins = new List<string>
    {
        "http://localhost:5173",
        "http://localhost:3000"
    };

    var fromEnv = Environment.GetEnvironmentVariable("FRONTEND_ORIGIN");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        origins.AddRange(fromEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    var fromConfig = config.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (fromConfig != null) origins.AddRange(fromConfig);

    return origins.Distinct().ToArray();
}
