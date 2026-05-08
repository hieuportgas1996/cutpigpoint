using CutPig.Data;
using CutPig.Domain;
using CutPig.Dtos;
using CutPig.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TienLenScoringService _scoring;

    public GamesController(AppDbContext db, TienLenScoringService scoring)
    {
        _db = db;
        _scoring = scoring;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List()
    {
        var games = await _db.Games
            .Include(g => g.Players).ThenInclude(p => p.Player)
            .OrderByDescending(g => g.StartedAt)
            .ToListAsync();

        return Ok(games.Select(g => new
        {
            g.Id,
            Type = (int)g.Type,
            g.StartedAt,
            g.FinishedAt,
            Players = g.Players.OrderBy(p => p.Seat).Select(p => new { p.PlayerId, Name = p.Player!.Name, p.Seat, HasAvatar = p.Player!.AvatarData != null })
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GameDto>> Get(Guid id)
    {
        var game = await LoadGame(id);
        if (game == null) return NotFound();
        return BuildDto(game);
    }

    [HttpPost]
    public async Task<ActionResult<GameDto>> Create([FromBody] CreateGameRequest req)
    {
        var type = req.Type.HasValue ? (GameType)req.Type.Value : GameType.TienLenMienNam;
        if (!Enum.IsDefined(typeof(GameType), type))
            return BadRequest("Loại ván không hợp lệ.");

        if (req.PlayerIds == null || req.PlayerIds.Count == 0)
            return BadRequest("Cần chọn người chơi.");

        if (type == GameType.TienLenMienNam)
        {
            if (req.PlayerIds.Count != 4)
                return BadRequest("Tiến Lên Miền Nam cần đúng 4 người chơi.");
        }
        else if (type == GameType.Manual)
        {
            if (req.PlayerIds.Count < 2)
                return BadRequest("Ván tự do cần ít nhất 2 người chơi.");
        }
        else
        {
            return BadRequest("Loại ván chưa được hỗ trợ.");
        }

        if (req.PlayerIds.Distinct().Count() != req.PlayerIds.Count)
            return BadRequest("Người chơi không được trùng nhau.");

        var existing = await _db.Players.Where(p => req.PlayerIds.Contains(p.Id)).ToListAsync();
        if (existing.Count != req.PlayerIds.Count) return BadRequest("Một hoặc nhiều người chơi không tồn tại.");

        var game = new Game { Type = type };
        for (int i = 0; i < req.PlayerIds.Count; i++)
        {
            game.Players.Add(new GamePlayer { PlayerId = req.PlayerIds[i], Seat = i + 1 });
        }
        _db.Games.Add(game);
        await _db.SaveChangesAsync();

        var loaded = await LoadGame(game.Id);
        return CreatedAtAction(nameof(Get), new { id = game.Id }, BuildDto(loaded!));
    }

    [HttpPost("{id:guid}/finish")]
    public async Task<ActionResult<GameDto>> Finish(Guid id)
    {
        var game = await _db.Games.FindAsync(id);
        if (game == null) return NotFound();
        game.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var loaded = await LoadGame(id);
        return BuildDto(loaded!);
    }

    [HttpPost("{id:guid}/rounds")]
    public async Task<ActionResult<RoundDto>> AddRound(Guid id, [FromBody] CreateRoundRequest req)
    {
        var game = await _db.Games
            .Include(g => g.Players)
            .Include(g => g.Rounds)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (game == null) return NotFound();
        if (game.FinishedAt != null) return BadRequest("Ván đã kết thúc.");

        var playerIds = game.Players.Select(p => p.PlayerId).ToHashSet();
        if (req.Players.Count != playerIds.Count || req.Players.Any(p => !playerIds.Contains(p.PlayerId)))
            return BadRequest("Người chơi của round phải khớp với người chơi của ván.");

        List<RoundResult> results;
        bool manualScoring = req.ManualScoring;

        if (game.Type == GameType.Manual)
        {
            manualScoring = true;
            results = req.Players.Select(p => new RoundResult
            {
                PlayerId = p.PlayerId,
                Score = p.ManualScore ?? 0
            }).ToList();
        }
        else
        {
            try
            {
                results = _scoring.Compute(req.Players, manualScoring);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        var round = new GameRound
        {
            GameId = game.Id,
            RoundNumber = (game.Rounds.Count == 0 ? 0 : game.Rounds.Max(r => r.RoundNumber)) + 1,
            ManualScoring = manualScoring,
            Results = results
        };
        _db.GameRounds.Add(round);
        await _db.SaveChangesAsync();

        return new RoundDto(
            round.Id,
            round.RoundNumber,
            round.ManualScoring,
            round.CreatedAt,
            round.Results.Select(MapRoundResult).ToList());
    }

    [HttpDelete("{id:guid}/rounds/{roundId:guid}")]
    public async Task<IActionResult> DeleteRound(Guid id, Guid roundId)
    {
        var round = await _db.GameRounds.FirstOrDefaultAsync(r => r.Id == roundId && r.GameId == id);
        if (round == null) return NotFound();
        _db.GameRounds.Remove(round);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var game = await _db.Games.FindAsync(id);
        if (game == null) return NotFound();
        if (game.FinishedAt == null) return BadRequest("Chỉ xoá được ván đã kết thúc.");
        _db.Games.Remove(game);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Game?> LoadGame(Guid id)
    {
        return await _db.Games
            .Include(g => g.Players).ThenInclude(p => p.Player)
            .Include(g => g.Rounds).ThenInclude(r => r.Results)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    private static GameDto BuildDto(Game g)
    {
        var totals = g.Players.ToDictionary(p => p.PlayerId, _ => 0);
        foreach (var round in g.Rounds)
        {
            foreach (var r in round.Results)
            {
                if (totals.ContainsKey(r.PlayerId))
                    totals[r.PlayerId] += r.Score;
            }
        }

        return new GameDto(
            g.Id,
            (int)g.Type,
            g.StartedAt,
            g.FinishedAt,
            g.Players
                .OrderBy(p => p.Seat)
                .Select(p => new GamePlayerDto(p.PlayerId, p.Player!.Name, p.Seat, totals[p.PlayerId], p.Player!.AvatarData != null))
                .ToList(),
            g.Rounds
                .OrderBy(r => r.RoundNumber)
                .Select(r => new RoundDto(
                    r.Id, r.RoundNumber, r.ManualScoring, r.CreatedAt,
                    r.Results.Select(MapRoundResult).ToList()))
                .ToList());
    }

    private static RoundResultDto MapRoundResult(RoundResult r) => new(
        r.PlayerId,
        r.Rank,
        r.BlackPigsCut, r.RedPigsCut, r.BlackPigsLost, r.RedPigsLost,
        r.ThreePairsStraight, r.ThreePairsVictimId,
        r.FourOfAKind, r.FourOfAKindVictimId,
        r.FourPairsStraight, r.FourPairsVictimId,
        r.WhiteWin,
        r.Judge,
        r.JudgedVictim,
        r.BlackPigsHeld, r.RedPigsHeld,
        r.HasThreePairsHeld, r.HasFourOfAKindHeld, r.HasFourPairsHeld,
        r.Score);
}
