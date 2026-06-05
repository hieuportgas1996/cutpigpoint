using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests "Giải lao — Phản xạ" (bài 52 lá, lưới 4×4, tìm 3 lá): engine sinh 3 lượt, mỗi lượt 16 lá DUY NHẤT +
/// 3 ô target khác nhau; + luồng MatchManager (cooldown → click chọn 3 lá → reveal → finalize xếp hạng đúng+nhanh).
/// </summary>
public class ReflexGameTests
{
    [Fact]
    public void BuildRounds_ThreeRounds_SixteenUniqueCards_ThreeTargets()
    {
        var rng = new Random(99);
        for (int trial = 0; trial < 300; trial++)
        {
            var rounds = ReflexGameEngine.BuildRounds(rng);
            Assert.Equal(ReflexGameEngine.NumRounds, rounds.Count);
            foreach (var r in rounds)
            {
                Assert.Equal(ReflexGameEngine.GridSize, r.Grid.Count);                  // 16 lá
                Assert.Equal(ReflexGameEngine.GridSize, r.Grid.Distinct().Count());     // duy nhất
                Assert.Equal(ReflexGameEngine.NumTargets, r.TargetIndexes.Count);       // 3 ô target
                Assert.Equal(ReflexGameEngine.NumTargets, r.TargetIndexes.Distinct().Count());
                Assert.All(r.TargetIndexes, i => Assert.InRange(i, 0, 15));
            }
        }
    }

    [Fact]
    public void IsCorrect_OnlyWhenExactThreeTargets()
    {
        var rng = new Random(1);
        var round = ReflexGameEngine.BuildRounds(rng)[0];
        var t = round.TargetIndexes;
        Assert.True(ReflexGameEngine.IsCorrect(round, t));                       // đúng cả 3
        Assert.True(ReflexGameEngine.IsCorrect(round, t.AsEnumerable().Reverse())); // không quan tâm thứ tự
        Assert.False(ReflexGameEngine.IsCorrect(round, t.Take(2)));             // thiếu 1
        var wrong = Enumerable.Range(0, 16).First(i => !t.Contains(i));
        Assert.False(ReflexGameEngine.IsCorrect(round, new[] { t[0], t[1], wrong })); // sai 1 lá
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
    public void Pick_ThreeCorrect_ChotsAndFastestRanksFirst_AcrossThreeRounds()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Reflex;
        Invoke("DealBreakReflexRound", match);

        for (int round = 0; round < ReflexGameEngine.NumRounds; round++)
        {
            match.ReflexCooldownUntil = DateTime.UtcNow.AddSeconds(-1);
            mgr.TryStartReflexPlay(roomId);
            var targets = match.ReflexRounds![round].TargetIndexes;
            // Mỗi người chọn đúng 3 lá; P0 hoàn tất trước (nhanh nhất).
            foreach (var id in ids)
                foreach (var t in targets)
                    mgr.SubmitReflexCell(roomId, id, t);
            Assert.NotNull(match.ReflexRevealUntil);
            Invoke("FinalizeReflexReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank);   // cùng đúng 3/3 → nhanh nhất hạng 1
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
        Assert.Equal(2, scores[0]);
    }

    [Fact]
    public void Pick_WrongCardCountsIncorrect_Timeout()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.Reflex;
        Invoke("DealBreakReflexRound", match);

        for (int round = 0; round < ReflexGameEngine.NumRounds; round++)
        {
            match.ReflexCooldownUntil = DateTime.UtcNow.AddSeconds(-1);
            mgr.TryStartReflexPlay(roomId);
            var targets = match.ReflexRounds![round].TargetIndexes;
            int wrong = Enumerable.Range(0, 16).First(i => !targets.Contains(i));
            // P0 chọn đúng 3 lá; P1 chọn 2 đúng + 1 sai (=> sai); P2,P3 không chọn → hết giờ.
            foreach (var t in targets) mgr.SubmitReflexCell(roomId, ids[0], t);
            mgr.SubmitReflexCell(roomId, ids[1], targets[0]);
            mgr.SubmitReflexCell(roomId, ids[1], targets[1]);
            mgr.SubmitReflexCell(roomId, ids[1], wrong);
            match.ReflexAnswerDeadline = DateTime.UtcNow.AddSeconds(-1);
            Assert.True(mgr.TryAutoCloseReflexRound(roomId));
            Invoke("FinalizeReflexReveal", match);
        }

        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(1, match.Players[0].FinalRank); // 3 đúng → hạng 1
    }
}
