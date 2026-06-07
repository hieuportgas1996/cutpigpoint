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
/// Tests "Giải lao — Trí tuệ" (Sudoku 4×4): engine sinh đề hợp lệ + nghiệm duy nhất, và luồng giải ở MatchManager
/// (điền ô → giải xong → finalize → xếp hạng → điểm theo nhóm thắng/thua).
/// </summary>
public class SudokuGameTests
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

    private static bool ValidSolution(int[] g)
    {
        int N = 4;
        bool grp(IEnumerable<int> xs) => xs.OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3, 4 });
        for (int r = 0; r < N; r++)
            if (!grp(Enumerable.Range(0, N).Select(c => g[r * N + c]))) return false;
        for (int c = 0; c < N; c++)
            if (!grp(Enumerable.Range(0, N).Select(r => g[r * N + c]))) return false;
        for (int br = 0; br < N; br += 2)
            for (int bc = 0; bc < N; bc += 2)
                if (!grp(new[] { g[br * N + bc], g[br * N + bc + 1], g[(br + 1) * N + bc], g[(br + 1) * N + bc + 1] }))
                    return false;
        return true;
    }

    [Fact]
    public void Build_ProducesValidSolution_AndSomeBlanks()
    {
        var rng = new Random(2026);
        for (int t = 0; t < 100; t++)
        {
            var p = SudokuGameEngine.Build(rng);
            Assert.True(ValidSolution(p.Solution), "Lời giải phải hợp lệ Sudoku 4×4");
            int blanks = p.Given.Count(g => !g);
            Assert.InRange(blanks, 1, SudokuGameEngine.Cells - 1); // có ô trống, không trống hết
        }
    }

    [Fact]
    public void IsSolved_OnlyTrueWhenMatchesSolution()
    {
        var p = SudokuGameEngine.Build(new Random(7));
        var fills = p.Solution.ToArray();
        Assert.True(SudokuGameEngine.IsSolved(p, fills));
        // Sai 1 ô (đảo 2 giá trị khác nhau) → không còn đúng.
        int a = 0, b = Array.FindIndex(fills, x => x != fills[0]);
        (fills[a], fills[b]) = (fills[b], fills[a]);
        Assert.False(SudokuGameEngine.IsSolved(p, fills));
    }

    // Giải hộ: điền các ô trống của 1 người theo lời giải (qua hub SubmitSudokuCell).
    private static void SolveFor(MatchManager mgr, Match match, Guid roomId, Guid uid)
    {
        var puzzle = match.Sudoku!;
        for (int i = 0; i < SudokuGameEngine.Cells; i++)
            if (!puzzle.Given[i])
                mgr.SubmitSudokuCell(roomId, uid, i, puzzle.Solution[i]);
    }

    [Fact]
    public void Flow_OneSolver_GetsPlus6_OthersMinus2()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Sudoku;
        Invoke("DealBreakSudokuRound", match);
        Assert.Equal(MatchStatus.BreakSudoku, match.Status);

        SolveFor(mgr, match, roomId, ids[0]); // chỉ P0 giải xong
        Assert.True(match.SudokuAnswers[ids[0]][0].Correct);

        // Hết giờ → finalize.
        match.SudokuDeadline = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryFinalizeSudoku(roomId));
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);

        var s = mgr.ComputeRoundScores(match);
        Assert.Equal(0, s.Sum());
        Assert.Equal(6, s[0]);
        Assert.Equal(new[] { 6, -2, -2, -2 }, s);
    }

    [Fact]
    public void Flow_AllSolve_FinalizesImmediately_StandardTable()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Sudoku;
        Invoke("DealBreakSudokuRound", match);

        // Cả 4 giải xong (thứ tự gọi = thứ tự thời gian) → tự finalize khi người cuối xong.
        foreach (var id in ids) SolveFor(mgr, match, roomId, id);
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);

        var s = mgr.ComputeRoundScores(match);
        Assert.Equal(0, s.Sum());
        // 4 người đều đúng → bảng chuẩn +2/+1/-1/-2 (P0 xong trước nhất).
        Assert.Equal(2, s[0]);
        Assert.Equal(-2, s[3]);
    }

    [Fact]
    public void Flow_NobodySolves_AllZero()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Sudoku;
        Invoke("DealBreakSudokuRound", match);

        match.SudokuDeadline = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryFinalizeSudoku(roomId));
        var s = mgr.ComputeRoundScores(match);
        Assert.Equal(new[] { 0, 0, 0, 0 }, s);
    }

    [Fact]
    public void Submit_CannotEditGivenCell()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Sudoku;
        Invoke("DealBreakSudokuRound", match);
        int givenCell = Array.FindIndex(match.Sudoku!.Given, g => g);
        Assert.Throws<InvalidOperationException>(() => mgr.SubmitSudokuCell(roomId, ids[0], givenCell, 1));
    }
}
