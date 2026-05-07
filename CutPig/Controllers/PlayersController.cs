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
    private readonly AppDbContext _db;
    public PlayersController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlayerDto>>> List()
    {
        var players = await _db.Players
            .OrderBy(p => p.Name)
            .Select(p => new PlayerDto(p.Id, p.Name, p.Nickname))
            .ToListAsync();
        return Ok(players);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlayerDto>> Get(Guid id)
    {
        var p = await _db.Players.FindAsync(id);
        if (p == null) return NotFound();
        return new PlayerDto(p.Id, p.Name, p.Nickname);
    }

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create([FromBody] CreatePlayerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required");
        var p = new Player { Name = req.Name.Trim(), Nickname = req.Nickname?.Trim() };
        _db.Players.Add(p);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id }, new PlayerDto(p.Id, p.Name, p.Nickname));
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
        return new PlayerDto(p.Id, p.Name, p.Nickname);
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
}
