using CutPig.Dtos;
using CutPig.GameEngine;
using CutPig.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CutPig.Services;

public class MatchTimerService : BackgroundService
{
    private readonly MatchManager _matches;
    private readonly IHubContext<RoomHub> _hub;
    private readonly ILogger<MatchTimerService> _logger;

    public MatchTimerService(MatchManager matches, IHubContext<RoomHub> hub, ILogger<MatchTimerService> logger)
    {
        _matches = matches;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var match in _matches.AllActive())
                {
                    if (match.TurnDeadline > now) continue;

                    var current = match.Players[match.CurrentTurnSeatIndex];
                    try
                    {
                        var result = _matches.Pass(match.RoomId, current.UserId, isAutoPass: true);
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(result.Match), stoppingToken);
                        if (result.MatchEnded)
                        {
                            await FinalizeAsync(match);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-pass failed for room {RoomId}", match.RoomId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MatchTimerService loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task FinalizeAsync(Match match)
    {
        var scores = ComputeBasicRankScores(match.Players.Count, match);
        var results = match.Players
            .OrderBy(p => p.FinalRank ?? int.MaxValue)
            .Select(p => new MatchEndResultDto(p.UserId, p.DisplayName, p.FinalRank ?? 0, scores[p.UserId]))
            .ToList();
        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchEnd", new MatchEndDto(match.Id, results));
        _matches.Remove(match.RoomId);
    }

    private static Dictionary<Guid, int> ComputeBasicRankScores(int playerCount, Match match)
    {
        int[] table = playerCount switch
        {
            4 => new[] { 2, 1, -1, -2 },
            3 => new[] { 2, 0, -2 },
            2 => new[] { 1, -1 },
            _ => Enumerable.Range(0, playerCount).Select(_ => 0).ToArray()
        };
        var dict = new Dictionary<Guid, int>();
        foreach (var p in match.Players)
        {
            var rank = (p.FinalRank ?? playerCount) - 1;
            dict[p.UserId] = table[Math.Clamp(rank, 0, table.Length - 1)];
        }
        return dict;
    }

    private static MatchPublicStateDto BuildPublic(Match m)
    {
        return new MatchPublicStateDto(
            m.Id,
            m.RoomId,
            (int)m.Status,
            m.CurrentTurnSeatIndex,
            m.CurrentTrickOwnerId,
            m.CurrentTrick?.Cards.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.TurnDeadline,
            m.Players.Select(p => new MatchPlayerDto(
                p.UserId,
                p.DisplayName,
                p.SeatIndex,
                p.Hand.Count,
                p.FinalRank)).ToList());
    }
}
