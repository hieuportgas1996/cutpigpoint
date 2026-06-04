using System;
using System.Collections.Generic;
using System.Linq;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho Giải Lao Oẳn Tù Xì: luật kéo-búa-bao, hòa đánh lại, best-of-N, bracket 4 người,
/// xếp hạng cuối (1=thắng final, 2=thua final, 3=thắng tranh-3, 4=thua tranh-3) + scoring +2/+1/-1/-2.
/// </summary>
public class RpsTournamentTests
{
    // ---- Resolve kéo-búa-bao ----
    [Theory]
    [InlineData(RpsChoice.Rock, RpsChoice.Scissors, RpsOutcome.AWins)]   // búa thắng kéo
    [InlineData(RpsChoice.Scissors, RpsChoice.Paper, RpsOutcome.AWins)]  // kéo thắng bao
    [InlineData(RpsChoice.Paper, RpsChoice.Rock, RpsOutcome.AWins)]      // bao thắng búa
    [InlineData(RpsChoice.Scissors, RpsChoice.Rock, RpsOutcome.BWins)]
    [InlineData(RpsChoice.Rock, RpsChoice.Rock, RpsOutcome.Draw)]
    [InlineData(RpsChoice.Paper, RpsChoice.Paper, RpsOutcome.Draw)]
    public void Resolve_Works(RpsChoice a, RpsChoice b, RpsOutcome expected)
        => Assert.Equal(expected, RpsEngine.Resolve(a, b));

    // ---- Hòa đánh lại (không tăng điểm) ----
    [Fact]
    public void Draw_RematchNoScore()
    {
        var m = new RpsMatchup { PlayerAId = Guid.NewGuid(), PlayerBId = Guid.NewGuid(), WinTarget = 3 };
        m.ChoiceA = RpsChoice.Rock; m.ChoiceB = RpsChoice.Rock;
        var outcome = m.ResolveCurrentGame();
        Assert.Equal(RpsOutcome.Draw, outcome);
        Assert.Equal(0, m.WinsA);
        Assert.Equal(0, m.WinsB);
        Assert.Equal(0, m.GamesPlayed);
        Assert.False(m.IsDone);
        Assert.Equal(RpsChoice.None, m.ChoiceA); // reset để đánh lại
    }

    // ---- Best-of-3: chạm 3 thắng ----
    [Fact]
    public void BestOf3_FirstTo3Wins()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var m = new RpsMatchup { PlayerAId = a, PlayerBId = b, WinTarget = 3 };
        for (int i = 0; i < 3; i++)
        {
            m.ChoiceA = RpsChoice.Rock; m.ChoiceB = RpsChoice.Scissors; // A thắng
            m.ResolveCurrentGame();
        }
        Assert.True(m.IsDone);
        Assert.Equal(a, m.WinnerId);
        Assert.Equal(b, m.LoserId);
        Assert.Equal(3, m.WinsA);
    }

    // ---- Bracket đầy đủ + xếp hạng ----
    [Fact]
    public void FullBracket_RankingCorrect()
    {
        var seeds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var t = RpsTournament.Create(seeds);

        // Helper: cho A của cặp hiện tại thắng 'target' ván.
        void WinCurrentByA()
        {
            var cur = t.Current;
            while (!cur.IsDone)
            {
                cur.ChoiceA = RpsChoice.Rock; cur.ChoiceB = RpsChoice.Scissors;
                cur.ResolveCurrentGame();
            }
            t.AdvanceStage();
        }
        void WinCurrentByB()
        {
            var cur = t.Current;
            while (!cur.IsDone)
            {
                cur.ChoiceA = RpsChoice.Scissors; cur.ChoiceB = RpsChoice.Rock;
                cur.ResolveCurrentGame();
            }
            t.AdvanceStage();
        }

        // V1: seed0 (A) thắng → loser seed1
        WinCurrentByA();
        // V2: seed2 (A) thắng → loser seed3
        WinCurrentByA();
        // V3 (tranh hạng 3): A = loser V1 = seed1, B = loser V2 = seed3. Cho A (seed1) thắng → hạng 3.
        Assert.Equal(RpsStage.ThirdPlace, t.Stage);
        Assert.Equal(seeds[1], t.ThirdPlace.PlayerAId);
        Assert.Equal(seeds[3], t.ThirdPlace.PlayerBId);
        WinCurrentByA();
        // V4 (final): A = winner V1 = seed0, B = winner V2 = seed2. Cho B (seed2) thắng → hạng 1.
        Assert.Equal(RpsStage.Final, t.Stage);
        Assert.Equal(seeds[0], t.Final.PlayerAId);
        Assert.Equal(seeds[2], t.Final.PlayerBId);
        WinCurrentByB();

        Assert.Equal(RpsStage.Done, t.Stage);
        // Hạng: 1=winner final=seed2, 2=loser final=seed0, 3=winner tranh-3=seed1, 4=loser tranh-3=seed3.
        Assert.Equal(new[] { seeds[2], seeds[0], seeds[1], seeds[3] }, t.FinalRanking.ToArray());
    }

    // ---- Scoring +2/+1/-1/-2 theo hạng ----
    [Fact]
    public void BreakScoring_ByRank_ZeroSum()
    {
        var mgr = new MatchManager();
        var match = new Match { RoomId = Guid.NewGuid(), HostUserId = Guid.NewGuid() };
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        for (int i = 0; i < 4; i++)
            match.Players.Add(new MatchPlayer { UserId = ids[i], SeatIndex = i, DisplayName = $"P{i + 1}" });
        match.IsBreakRound = true;
        match.Players[0].FinalRank = 1;
        match.Players[1].FinalRank = 2;
        match.Players[2].FinalRank = 3;
        match.Players[3].FinalRank = 4;

        var s = mgr.ComputeRoundScores(match);
        Assert.Equal(new[] { 2, 1, -1, -2 }, s);
        Assert.Equal(0, s.Sum());
    }
}
