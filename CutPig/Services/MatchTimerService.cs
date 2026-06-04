using CutPig.Dtos;
using CutPig.GameEngine;
using CutPig.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CutPig.Services;

public class MatchTimerService : BackgroundService
{
    private readonly MatchManager _matches;
    private readonly IHubContext<RoomHub> _hub;
    private readonly RoomPresenceTracker _presence;
    private readonly ILogger<MatchTimerService> _logger;

    public MatchTimerService(MatchManager matches, IHubContext<RoomHub> hub, RoomPresenceTracker presence, ILogger<MatchTimerService> logger)
    {
        _matches = matches;
        _hub = hub;
        _presence = presence;
        _logger = logger;
    }

    private async Task SendPrivateHandsAsync(Match match, CancellationToken ct)
    {
        foreach (var player in match.Players)
        {
            var conns = _presence.ConnectionsFor(match.RoomId, player.UserId);
            if (conns.Count == 0) continue;
            var dto = new PrivateHandDto(
                match.RoomId,
                player.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList());
            await _hub.Clients.Clients(conns).SendAsync("PrivateHand", dto, ct);
        }
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
                        // Auto-pass khi mở nước = server tự đánh lá nhỏ nhất → tay người đó giảm 1 lá;
                        // gửi lại PrivateHand để client không giữ tay cũ và click vào lá đã rời tay.
                        await SendPrivateHandsAsync(result.Match, stoppingToken);
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

                // Hết 60s cửa sổ về trắng (trong trick 1) mà chưa ai chốt → đóng cửa sổ, chơi tiếp.
                foreach (var match in _matches.AllActive())
                {
                    if (!match.WhiteWinDeadline.HasValue || match.WhiteWinDeadline.Value > now) continue;
                    try
                    {
                        var resolved = _matches.ExpireWhiteWinWindow(match.RoomId);
                        if (resolved == null) continue;
                        await _hub.Clients.Group($"room:{resolved.RoomId}").SendAsync("MatchState", BuildPublic(resolved), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "WhiteWin window expire failed for room {RoomId}", match.RoomId);
                    }
                }

                // Trick-cut timeout → finalize trick reset
                foreach (var match in _matches.AllPendingTrickCut())
                {
                    if (!match.TrickCutDeadline.HasValue || match.TrickCutDeadline.Value > now) continue;
                    try
                    {
                        var resolved = _matches.ResolveTrickCutTimeout(match.RoomId);
                        if (resolved == null) continue;
                        await _hub.Clients.Group($"room:{resolved.RoomId}").SendAsync("MatchState", BuildPublic(resolved), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "TrickCut timeout resolve failed for room {RoomId}", match.RoomId);
                    }
                }

                // Vote-reset timeout → treat unset as "Bỏ", resolve (deal lại nếu đủ phiếu)
                foreach (var match in _matches.AllVoteReset())
                {
                    if (!match.VoteResetDeadline.HasValue || match.VoteResetDeadline.Value > now) continue;
                    try
                    {
                        var resolved = _matches.ResolveVoteResetTimeout(match.RoomId);
                        if (resolved == null) continue;
                        await _hub.Clients.Group($"room:{resolved.Match.RoomId}").SendAsync("MatchState", BuildPublic(resolved.Match), stoppingToken);
                        if (resolved.Dealt)
                        {
                            await SendPrivateHandsAsync(resolved.Match, stoppingToken);
                            if (resolved.Match.Status == MatchStatus.WaitingNextRound)
                                await EmitRoundEndAsync(resolved.Match, stoppingToken); // bài mới về trắng
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "VoteReset timeout resolve failed for room {RoomId}", match.RoomId);
                    }
                }

                // Festival reveal: auto-lật toàn bộ sau 60s, hoặc finalize 5s sau khi lật hết.
                foreach (var match in _matches.AllFestivalReveal())
                {
                    try
                    {
                        if (match.FestivalRevealDeadline.HasValue && match.FestivalRevealDeadline.Value <= now)
                        {
                            var resolved = _matches.FinalizeFestival(match.RoomId);
                            if (resolved == null) continue;
                            await _hub.Clients.Group($"room:{resolved.RoomId}").SendAsync("MatchState", BuildPublic(resolved), stoppingToken);
                            if (resolved.Status == MatchStatus.WaitingNextRound)
                                await EmitRoundEndAsync(resolved, stoppingToken);
                        }
                        else if (match.FestivalAutoFlipDeadline.HasValue && match.FestivalAutoFlipDeadline.Value <= now)
                        {
                            var flipped = _matches.AutoFlipFestival(match.RoomId);
                            if (flipped == null) continue;
                            await _hub.Clients.Group($"room:{flipped.RoomId}").SendAsync("MatchState", BuildPublic(flipped), stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Festival reveal resolve failed for room {RoomId}", match.RoomId);
                    }
                }

                // Xì Dách (Sát Phạt): hết 60s lượt rút → auto rút/dừng cho người đang tới lượt.
                foreach (var match in _matches.AllXiDachPlaying())
                {
                    if (!match.XiDachTurnDeadline.HasValue || match.XiDachTurnDeadline.Value > now) continue;
                    try
                    {
                        var resolved = _matches.AutoAdvanceXiDach(match.RoomId);
                        if (resolved == null) continue;
                        await _hub.Clients.Group($"room:{resolved.RoomId}").SendAsync("MatchState", BuildPublic(resolved), stoppingToken);
                        await SendPrivateHandsAsync(resolved, stoppingToken); // tay vừa rút thêm lá → cập nhật private
                        if (resolved.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(resolved, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "XiDach auto-advance failed for room {RoomId}", match.RoomId);
                    }
                }

                // Hết hạn lời mời Liều Ăn Nhiều (offer có thể treo ở ván n+1 đang chơi HOẶC lúc chờ ván mới)
                // → auto từ chối. Không chặn deal: ván n+1 vẫn chạy bình thường.
                foreach (var match in _matches.AllWithGambleOffer().ToList())
                {
                    if (_matches.TryExpireGambleOffer(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }

                // Giải Lao (Oẳn Tù Xì): hết 10s chọn → auto random cho ai chưa chọn rồi chốt ván.
                foreach (var match in _matches.AllBreakRps().ToList())
                {
                    if (_matches.TryAutoResolveRps(match.RoomId))
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Auto-start next round after 5s when match is WaitingNextRound
                foreach (var match in _matches.AllWaitingNextRound())
                {
                    if (!match.NextRoundAt.HasValue || match.NextRoundAt.Value > now) continue;
                    try
                    {
                        var nextMatch = _matches.StartNextRound(match.RoomId, null); // system-triggered
                        await _hub.Clients.Group($"room:{nextMatch.RoomId}").SendAsync("MatchState", BuildPublic(nextMatch), stoppingToken);
                        await SendPrivateHandsAsync(nextMatch, stoppingToken);
                        if (nextMatch.Status == MatchStatus.WaitingNextRound)
                        {
                            // White-win on the new deal — emit round-end again
                            await EmitRoundEndAsync(nextMatch, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-next-round failed for room {RoomId}", match.RoomId);
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
        var dto = _matches.BuildRoundEndDto(match);
        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("RoundEnd", dto, ct);
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
            m.NextRoundAt,
            m.HostUserId,
            m.Players.Select(p => new MatchPlayerDto(
                p.UserId,
                p.DisplayName,
                p.SeatIndex,
                p.Hand.Count,
                p.FinalRank,
                p.PassedThisTrick,
                p.TotalScore,
                p.WhiteWinReason,
                p.WhiteWinAccepted,
                p.HasAvatar,
                p.Surrendered,
                p.VoteResetChoice,
                p.HasUsedVoteReset,
                p.HasUsedFestival,
                p.FestivalWinner,
                p.FestivalRevealedIdx.Count,
                m.IsFestivalRound
                    ? p.Hand.Select((c, i) => p.FestivalRevealedIdx.Contains(i) ? new CardDto(c.Rank, (int)c.Suit) : (CardDto?)null).ToList()
                    : null,
                p.HasUsedStarOfHope,
                p.IsStarOfHope,
                p.HasUsedXiDach,
                p.IsXiDachDealer,
                p.XiDachStood,
                p.XiDachSettled,
                p.XiDachRevealed,
                (m.IsXiDachRound && p.XiDachRevealed) ? XiDachEngine.Total(p.Hand) : 0,
                (m.IsXiDachRound && p.XiDachRevealed) ? p.Hand.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList() : null)).ToList(),
            m.WhiteWinDeadline,
            m.TrickCutDeadline,
            m.PendingTrickWinnerId,
            m.TrickCutCandidates.Count > 0 ? new List<Guid>(m.TrickCutCandidates) : null,
            m.LastWonTrickCards?.Select(c => new CardDto(c.Rank, (int)c.Suit)).ToList(),
            m.LastWonTrickWinnerId,
            m.ShowOpponentCardCount,
            m.VoteResetDeadline,
            m.VoteResetInitiatorId,
            m.PastFirstTrick,
            m.FestivalScheduled,
            m.IsFestivalRound,
            m.FestivalOrganizerId,
            m.FestivalRevealDeadline,
            m.FestivalAutoFlipDeadline,
            m.StarOfHopeScheduledUserId,
            m.XiDachScheduledUserId,
            m.IsXiDachRound,
            m.XiDachDealerId,
            m.XiDachTurnUserId,
            m.XiDachTurnDeadline);
    }
}
