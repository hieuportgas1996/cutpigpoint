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

                // Giải Lao: pha chọn game (BreakSelect) hết 30s mà người tổ chức chưa chọn → random rồi sang pha luật.
                foreach (var match in _matches.AllBreakSelect().ToList())
                {
                    if (_matches.TryAutoSelectBreakGame(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                // Giải Lao: pha hiện luật (BreakIntro) hết 30s → tự bắt đầu game đã chọn.
                foreach (var match in _matches.AllBreakIntro().ToList())
                {
                    if (_matches.TryStartBreakGame(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }

                // Giải Lao (Oẳn Tù Xì): hết 20s chọn → auto random rồi chốt ván (vào pha hiện kết quả 2s);
                // hết 2s hiện kết quả → qua ván/giai đoạn kế (hoặc finalize giải → WaitingNextRound).
                foreach (var match in _matches.AllBreakRps().ToList())
                {
                    bool changed = _matches.TryAutoResolveRps(match.RoomId) || _matches.TryFinalizeRpsReveal(match.RoomId);
                    if (changed)
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Tính toán): hết 10s chọn số → sinh câu hỏi; hết 5s trả lời → chốt câu (hiện đáp án);
                // hết pha hiện đáp án → qua câu kế hoặc finalize (xếp hạng → WaitingNextRound).
                foreach (var match in _matches.AllBreakMathPick().ToList())
                {
                    if (_matches.TryAutoStartMathQuiz(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                foreach (var match in _matches.AllBreakMathQuiz().ToList())
                {
                    bool changed = _matches.TryAutoCloseMathQuestion(match.RoomId) || _matches.TryFinalizeMathReveal(match.RoomId);
                    if (changed)
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Trí nhớ): hết 10s xem lưới → vào quiz; hết hạn trả lời → chốt câu (hiện đáp án);
                // hết pha hiện đáp án → câu kế hoặc finalize (xếp hạng → WaitingNextRound).
                foreach (var match in _matches.AllBreakMemoryView().ToList())
                {
                    if (_matches.TryStartMemoryQuiz(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                foreach (var match in _matches.AllBreakMemoryQuiz().ToList())
                {
                    bool changed = _matches.TryAutoCloseMemoryQuestion(match.RoomId) || _matches.TryFinalizeMemoryReveal(match.RoomId);
                    if (changed)
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Phản xạ): hết 3s cooldown → mở pha click; hết hạn click → chốt lượt (hiện đáp án);
                // hết pha hiện đáp án → lượt kế (cooldown) hoặc finalize.
                foreach (var match in _matches.AllBreakReflexCooldown().ToList())
                {
                    if (_matches.TryStartReflexPlay(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                foreach (var match in _matches.AllBreakReflexPlay().ToList())
                {
                    bool changed = _matches.TryAutoCloseReflexRound(match.RoomId) || _matches.TryFinalizeReflexReveal(match.RoomId);
                    if (changed)
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Trí tuệ — Sudoku): hết 60s → finalize (ai chưa xong = sai) → xếp hạng → WaitingNextRound.
                foreach (var match in _matches.AllBreakSudoku().ToList())
                {
                    if (_matches.TryFinalizeSudoku(match.RoomId))
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Cơ hội — Match Pairs): pha quay 20s → auto quay; pha chơi: hết 1.5s lá trật → úp+qua lượt;
                // hết 120s tổng → finalize (xếp hạng theo số cặp) → WaitingNextRound.
                foreach (var match in _matches.AllBreakMatchSpin().ToList())
                {
                    // Auto quay nếu tổ chức chưa bấm; hoặc hết 5s hiện thứ tự → vào pha chơi.
                    if (_matches.TryAutoSpinMatchPairs(match.RoomId) || _matches.TryStartMatchPairsPlay(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                foreach (var match in _matches.AllBreakMatchPlay().ToList())
                {
                    bool changed = _matches.TryResolveMatchPairsMismatch(match.RoomId)
                        || _matches.TryAutoFlipMatchPairsTurn(match.RoomId)
                        || _matches.TryFinalizeMatchPairs(match.RoomId);
                    if (changed)
                    {
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                        if (match.Status == MatchStatus.WaitingNextRound)
                            await EmitRoundEndAsync(match, stoppingToken);
                    }
                }

                // Giải Lao (Caro đồng đội): pha quay 20s → auto quay; hết 5s hiện team → vào chơi;
                // pha chơi: hết 10s/lượt → bỏ lượt qua người kế; hết backstop tổng → hòa → WaitingNextRound.
                foreach (var match in _matches.AllBreakCaroSpin().ToList())
                {
                    if (_matches.TryAutoSpinCaro(match.RoomId) || _matches.TryStartCaroPlay(match.RoomId))
                        await _hub.Clients.Group($"room:{match.RoomId}").SendAsync("MatchState", BuildPublic(match), stoppingToken);
                }
                foreach (var match in _matches.AllBreakCaroPlay().ToList())
                {
                    bool changed = _matches.TryAutoSkipCaroTurn(match.RoomId)
                        || _matches.TryFinalizeCaro(match.RoomId);
                    if (changed)
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

    // Dùng chung builder với RoomHub để tránh lệch field (bug cũ: bản copy riêng ở đây thiếu RPS/gamble).
    private static MatchPublicStateDto BuildPublic(Match m) => Hubs.RoomHub.BuildMatchPublic(m);
}
