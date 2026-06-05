using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests luồng "Giải lao — Tính toán" ở tầng MatchManager: đặt lịch → deal (pha chọn số) →
/// chọn số → sinh câu hỏi (pha quiz) → trả lời → hiện đáp án → câu kế → finalize (xếp hạng → WaitingNextRound).
/// Dùng reflection để đăng ký match vào _matchesByRoom + gọi các private deal/finalize (giống pattern test khác).
/// </summary>
public class MathBreakFlowTests
{
    private static readonly FieldInfo MatchesField =
        typeof(MatchManager).GetField("_matchesByRoom", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static (MatchManager mgr, Match match, Guid roomId, Guid[] ids) Setup()
    {
        var mgr = new MatchManager();
        var roomId = Guid.NewGuid();
        var match = new Match { Id = Guid.NewGuid(), RoomId = roomId, HostUserId = Guid.NewGuid() };
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        for (int i = 0; i < 4; i++)
            match.Players.Add(new MatchPlayer { UserId = ids[i], SeatIndex = i, DisplayName = $"P{i + 1}" });
        var dict = (ConcurrentDictionary<Guid, Match>)MatchesField.GetValue(mgr)!;
        dict[roomId] = match;
        return (mgr, match, roomId, ids);
    }

    private static void Invoke(string name, Match match) =>
        typeof(MatchManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });

    [Fact]
    public void Schedule_PicksRandomGameFromPool_RemovesIt()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.Status = MatchStatus.InProgress;
        int before = match.BreakGamePool.Count; // 4 mặc định
        mgr.ScheduleBreak(roomId, ids[0]); // gameType bỏ qua — server random
        Assert.True(match.BreakScheduled);
        // Chọn 1 game hợp lệ trong pool ban đầu + rút khỏi pool.
        Assert.Contains(match.BreakScheduledType, new[] { BreakGameType.Rps, BreakGameType.Math, BreakGameType.Memory });
        Assert.Equal(before - 1, match.BreakGamePool.Count);
        Assert.Equal(ids[0], match.BreakOrganizerId);
        Assert.True(match.Players[0].HasUsedBreak);

        // Giả lập DealRound tiêu cờ → deal game tương ứng (test math path).
        match.IsBreakRound = true;
        match.BreakGame = BreakGameType.Math;
        Invoke("DealBreakMathRound", match);
        Assert.Equal(MatchStatus.BreakMathPick, match.Status);
        Assert.NotNull(match.MathPickDeadline);
    }

    [Fact]
    public void Schedule_PoolDefaultsToFourDistinctGames()
    {
        var (_, match, _, _) = Setup();
        Assert.Equal(4, match.BreakGamePool.Count);
        Assert.Equal(1, match.BreakGamePool.Count(g => g == BreakGameType.Rps));
        Assert.Equal(1, match.BreakGamePool.Count(g => g == BreakGameType.Math));
        Assert.Equal(1, match.BreakGamePool.Count(g => g == BreakGameType.Memory));
        Assert.Equal(1, match.BreakGamePool.Count(g => g == BreakGameType.Reflex));
    }

    [Fact]
    public void Schedule_FourTimes_DrainsPoolAllDistinct()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.Status = MatchStatus.InProgress;
        var drawn = new List<BreakGameType>();
        for (int i = 0; i < 4; i++)
        {
            // Reset cờ schedule giữa các lần (giả lập đã deal xong round trước).
            match.BreakScheduled = false; match.BreakScheduledType = BreakGameType.None;
            match.IsBreakRound = false; match.BreakGame = BreakGameType.None;
            mgr.ScheduleBreak(roomId, ids[i]);
            drawn.Add(match.BreakScheduledType);
        }
        Assert.Empty(match.BreakGamePool); // hết pool sau 4 lần
        // 4 lần rút = đúng 4 game KHÁC nhau.
        Assert.Equal(4, drawn.Distinct().Count());
        foreach (var g in new[] { BreakGameType.Rps, BreakGameType.Math, BreakGameType.Memory, BreakGameType.Reflex })
            Assert.Contains(g, drawn);
    }

    [Fact]
    public void Pick_AllFour_StartsQuiz_WithTwoQuestions()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Math;
        Invoke("DealBreakMathRound", match);

        for (int i = 0; i < 3; i++)
        {
            mgr.SubmitMathNumber(roomId, ids[i], i + 1);
            Assert.Equal(MatchStatus.BreakMathPick, match.Status); // chưa đủ 4
        }
        mgr.SubmitMathNumber(roomId, ids[3], 4);
        Assert.Equal(MatchStatus.BreakMathQuiz, match.Status);
        Assert.NotNull(match.MathQuestions);
        Assert.Equal(MathQuizEngine.NumQuestions, match.MathQuestions!.Count);
        Assert.Equal(0, match.MathCurrentQuestion);
        Assert.NotNull(match.MathAnswerDeadline);
    }

    [Fact]
    public void Answer_Correct_FastestRanksFirst_AcrossTwoQuestions()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Math;
        Invoke("DealBreakMathRound", match);
        for (int i = 0; i < 4; i++) mgr.SubmitMathNumber(roomId, ids[i], i + 1);
        Assert.Equal(MatchStatus.BreakMathQuiz, match.Status);

        // Câu 1: mọi người trả lời ĐÚNG (đáp án tại CorrectIndex). Thứ tự gọi = thứ tự thời gian → P0 nhanh nhất.
        int correct1 = match.MathQuestions![0].CorrectIndex;
        for (int i = 0; i < 4; i++) mgr.SubmitMathAnswer(roomId, ids[i], correct1);
        // Mọi người trả lời → vào pha reveal (MathRevealUntil set).
        Assert.NotNull(match.MathRevealUntil);
        // Hết reveal → câu 2.
        Invoke("FinalizeMathReveal", match);
        Assert.Equal(MatchStatus.BreakMathQuiz, match.Status);
        Assert.Equal(1, match.MathCurrentQuestion);

        // Câu 2: tất cả đúng. Finalize → xếp hạng + WaitingNextRound.
        int correct2 = match.MathQuestions![1].CorrectIndex;
        for (int i = 0; i < 4; i++) mgr.SubmitMathAnswer(roomId, ids[i], correct2);
        Invoke("FinalizeMathReveal", match);

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        // Mọi người cùng số câu đúng (2) → xếp theo tổng thời gian (P0 gọi trước = nhanh nhất → hạng 1).
        Assert.Equal(1, match.Players[0].FinalRank);
        // Điểm theo hạng (IsBreakRound): +2/+1/-1/-2 zero-sum.
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
        Assert.Equal(2, scores[0]);
    }

    [Fact]
    public void Answer_WrongOrTimeout_CountsAsIncorrect()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Math;
        Invoke("DealBreakMathRound", match);
        for (int i = 0; i < 4; i++) mgr.SubmitMathNumber(roomId, ids[i], i + 2);

        // Câu 1: P0 đúng; P1 sai; P2,P3 không trả lời (auto-close hết giờ → sai, max time).
        int c1 = match.MathQuestions![0].CorrectIndex;
        mgr.SubmitMathAnswer(roomId, ids[0], c1);
        mgr.SubmitMathAnswer(roomId, ids[1], (c1 + 1) % 4);
        match.MathAnswerDeadline = DateTime.UtcNow.AddSeconds(-1); // ép hết giờ
        Assert.True(mgr.TryAutoCloseMathQuestion(roomId));
        Assert.NotNull(match.MathRevealUntil);
        Invoke("FinalizeMathReveal", match);

        // Câu 2: P0 đúng lần nữa; còn lại sai/không.
        int c2 = match.MathQuestions![1].CorrectIndex;
        mgr.SubmitMathAnswer(roomId, ids[0], c2);
        match.MathAnswerDeadline = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryAutoCloseMathQuestion(roomId));
        Invoke("FinalizeMathReveal", match);

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        // P0: 2 đúng → hạng 1. P1: 0 đúng (đã trả lời sai). P2,P3: 0 đúng (timeout).
        Assert.Equal(1, match.Players[0].FinalRank);
    }

    [Fact]
    public void AutoStartQuiz_RandomsMissingPicks_WhenPickTimeout()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Math;
        Invoke("DealBreakMathRound", match);
        mgr.SubmitMathNumber(roomId, ids[0], 5); // chỉ 1 người chọn

        match.MathPickDeadline = DateTime.UtcNow.AddSeconds(-1); // hết giờ chọn
        Assert.True(mgr.TryAutoStartMathQuiz(roomId));
        Assert.Equal(MatchStatus.BreakMathQuiz, match.Status);
        Assert.Equal(4, match.MathPicks.Count); // 3 người còn lại được random
    }
}
