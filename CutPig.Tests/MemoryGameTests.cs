using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho "Giải lao — Trí nhớ": engine sinh lưới 3×3 (9 logo khác nhau) + 3 câu hỏi 3 ô khác nhau,
/// 4 đáp án (1 đúng + 3 nhiễu từ 8 đội còn lại trong lưới); + luồng MatchManager (xem lưới → quiz → finalize).
/// </summary>
public class MemoryGameTests
{
    // ---- Engine ----

    [Fact]
    public void BuildBoard_NineDistinctClubs_ThreeDistinctQuestions()
    {
        var rng = new Random(2024);
        for (int trial = 0; trial < 300; trial++)
        {
            var board = MemoryGameEngine.BuildBoard(rng);
            Assert.Equal(MemoryGameEngine.GridSize, board.Grid.Count);
            Assert.Equal(MemoryGameEngine.GridSize, board.Grid.Distinct().Count()); // 9 đội khác nhau
            Assert.All(board.Grid, slug => Assert.Contains(slug, MemoryGameEngine.Clubs.Select(c => c.Slug)));

            Assert.Equal(MemoryGameEngine.NumQuestions, board.Questions.Count);
            // 3 ô hỏi khác nhau.
            Assert.Equal(MemoryGameEngine.NumQuestions, board.Questions.Select(q => q.CellIndex).Distinct().Count());
            foreach (var q in board.Questions)
            {
                Assert.InRange(q.CellIndex, 0, 8);
                Assert.Equal(board.Grid[q.CellIndex], q.AnswerSlug);           // đáp án đúng = logo ở ô đó
                Assert.Equal(MemoryGameEngine.NumOptions, q.Options.Count);
                Assert.Equal(MemoryGameEngine.NumOptions, q.Options.Distinct().Count()); // 4 đáp án phân biệt
                Assert.Contains(q.AnswerSlug, q.Options);
                Assert.Equal(q.AnswerSlug, q.Options[q.CorrectIndex]);
                // 3 nhiễu phải nằm trong lưới (8 đội còn lại).
                Assert.All(q.Options, o => Assert.Contains(o, board.Grid));
            }
        }
    }

    // ---- Flow (MatchManager) ----

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
    public void Deal_EntersViewPhase_ThenQuizAfterTimeout()
    {
        var (mgr, match, roomId, _) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Memory;
        Invoke("DealBreakMemoryRound", match);
        Assert.Equal(MatchStatus.BreakMemoryView, match.Status);
        Assert.NotNull(match.MemoryBoard);
        Assert.NotNull(match.MemoryViewDeadline);

        match.MemoryViewDeadline = DateTime.UtcNow.AddSeconds(-1); // hết 10s xem
        Assert.True(mgr.TryStartMemoryQuiz(roomId));
        Assert.Equal(MatchStatus.BreakMemoryQuiz, match.Status);
        Assert.Equal(0, match.MemoryCurrentQuestion);
        Assert.NotNull(match.MemoryAnswerDeadline);
    }

    [Fact]
    public void Answer_AllCorrect_FastestRanksFirst()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Memory;
        Invoke("DealBreakMemoryRound", match);
        match.MemoryViewDeadline = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartMemoryQuiz(roomId);

        for (int qi = 0; qi < MemoryGameEngine.NumQuestions; qi++)
        {
            int correct = match.MemoryBoard!.Questions[qi].CorrectIndex;
            // Thứ tự gọi = thứ tự thời gian → P0 nhanh nhất.
            for (int i = 0; i < 4; i++) mgr.SubmitMemoryAnswer(roomId, ids[i], correct);
            Assert.NotNull(match.MemoryRevealUntil);
            Invoke("FinalizeMemoryReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank); // cùng số đúng → nhanh nhất hạng 1
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
        Assert.Equal(2, scores[0]);
    }

    [Fact]
    public void Answer_WrongAndTimeout_CountAsIncorrect()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Memory;
        Invoke("DealBreakMemoryRound", match);
        match.MemoryViewDeadline = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartMemoryQuiz(roomId);

        for (int qi = 0; qi < MemoryGameEngine.NumQuestions; qi++)
        {
            int correct = match.MemoryBoard!.Questions[qi].CorrectIndex;
            mgr.SubmitMemoryAnswer(roomId, ids[0], correct);             // P0 đúng
            mgr.SubmitMemoryAnswer(roomId, ids[1], (correct + 1) % 4);  // P1 sai
            match.MemoryAnswerDeadline = DateTime.UtcNow.AddSeconds(-1); // P2,P3 timeout
            Assert.True(mgr.TryAutoCloseMemoryQuestion(roomId));
            Invoke("FinalizeMemoryReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank); // 3 đúng → hạng 1
    }
}
