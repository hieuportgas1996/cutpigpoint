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
                        if (result.RoundEnded)
                        {
                            await EmitRoundEndAsync(result.Match, stoppingToken);
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

    private async Task EmitRoundEndAsync(Match match, CancellationToken ct)
    {
        var roundScores = _matches.ComputeRoundScores(match);
        bool wasWhiteWin = match.Players.Any(p => p.WhiteWinReason != null);
        for (int i = 0; i < match.Players.Count; i++)
            match.Players[i].TotalScore += roundScores[i];

        var entries = match.Players
            .OrderBy(p => p.FinalRank ?? int.MaxValue)
            .Select(p =>
            {
                int idx = match.Players.IndexOf(p);
                return new RoundResultEntryDto(
                    p.UserId, p.DisplayName,
                    p.FinalRank ?? 0,
                    roundScores[idx],
                    p.TotalScore,
                    p.WhiteWinReason);
            })
            .ToList();

        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("RoundEnd",
            new RoundEndDto(match.Id, match.RoundNumber, wasWhiteWin, entries), ct);
        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), ct);
    }

    private static MatchPublicStateDto BuildPublic(Match m)
    {
        return new MatchPublicStateDto(
            m.Id,
            m.RoomId,
            (int)m.Status,
            m.RoundNumber,
            m.CurrentTurnSeatIndex,
            m.CurrentTrickOwnerId,
            m.CurrentTrick?.Cards.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.TurnDeadline,
            m.HostUserId,
            m.Players.Select(p => new MatchPlayerDto(
                p.UserId,
                p.DisplayName,
                p.SeatIndex,
                p.Hand.Count,
                p.FinalRank,
                p.PassedThisTrick,
                p.TotalScore,
                p.WhiteWinReason)).ToList());
    }
}
