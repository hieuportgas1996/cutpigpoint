using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests "Giải lao — Phản xạ": engine sinh 3 lượt, mỗi lượt lưới 3×3 gồm 9 cặp (hình,màu) DUY NHẤT + 1 ô target;
/// + luồng MatchManager (cooldown 3s → click → reveal → lượt kế → finalize xếp hạng theo đúng+nhanh).
/// </summary>
public class ReflexGameTests
{
    [Fact]
    public void BuildRounds_ThreeRounds_NineUniqueCellsEach()
    {
        var rng = new Random(99);
        for (int trial = 0; trial < 300; trial++)
        {
            var rounds = ReflexGameEngine.BuildRounds(rng);
            Assert.Equal(ReflexGameEngine.NumRounds, rounds.Count);
            foreach (var r in rounds)
            {
                Assert.Equal(ReflexGameEngine.GridSize, r.Grid.Count);
                // 9 cặp (shape,color) DUY NHẤT.
                Assert.Equal(ReflexGameEngine.GridSize, r.Grid.Select(c => $"{c.Shape}|{c.Color}").Distinct().Count());
                Assert.InRange(r.TargetIndex, 0, 8);
                Assert.All(r.Grid, c => {
                    Assert.Contains(c.Shape, ReflexGameEngine.Shapes.Select(s => s.Key));
                    Assert.Contains(c.Color, ReflexGameEngine.Colors.Select(s => s.Key));
                });
            }
        }
    }

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
    public void Deal_CooldownThenPlayAfterTimeout()
    {
        var (mgr, match, roomId, _) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Reflex;
        Invoke("DealBreakReflexRound", match);
        Assert.Equal(MatchStatus.BreakReflexCooldown, match.Status);
        Assert.NotNull(match.ReflexRounds);
        Assert.NotNull(match.ReflexCooldownUntil);

        match.ReflexCooldownUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryStartReflexPlay(roomId));
        Assert.Equal(MatchStatus.BreakReflexPlay, match.Status);
        Assert.NotNull(match.ReflexAnswerDeadline);
    }

    [Fact]
    public void Click_CorrectTarget_FastestRanksFirst_AcrossThreeRounds()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Reflex;
        Invoke("DealBreakReflexRound", match);

        for (int round = 0; round < ReflexGameEngine.NumRounds; round++)
        {
            match.ReflexCooldownUntil = DateTime.UtcNow.AddSeconds(-1);
            mgr.TryStartReflexPlay(roomId);
            int target = match.ReflexRounds![round].TargetIndex;
            // P0 click trước (nhanh nhất), rồi P1,P2,P3.
            for (int i = 0; i < 4; i++) mgr.SubmitReflexCell(roomId, ids[i], target);
            Assert.NotNull(match.ReflexRevealUntil);
            Invoke("FinalizeReflexReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank); // cùng đúng 3/3 → nhanh nhất hạng 1
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
        Assert.Equal(2, scores[0]);
    }

    [Fact]
    public void Click_WrongCellAndTimeout_CountAsIncorrect()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Reflex;
        Invoke("DealBreakReflexRound", match);

        for (int round = 0; round < ReflexGameEngine.NumRounds; round++)
        {
            match.ReflexCooldownUntil = DateTime.UtcNow.AddSeconds(-1);
            mgr.TryStartReflexPlay(roomId);
            int target = match.ReflexRounds![round].TargetIndex;
            int wrong = (target + 1) % ReflexGameEngine.GridSize;
            mgr.SubmitReflexCell(roomId, ids[0], target);  // P0 đúng
            mgr.SubmitReflexCell(roomId, ids[1], wrong);   // P1 sai
            match.ReflexAnswerDeadline = DateTime.UtcNow.AddSeconds(-1); // P2,P3 hết giờ
            Assert.True(mgr.TryAutoCloseReflexRound(roomId));
            Invoke("FinalizeReflexReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank); // 3 đúng → hạng 1
    }
}
