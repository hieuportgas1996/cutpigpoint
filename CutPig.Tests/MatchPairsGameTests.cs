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
/// Tests "Giải lao — Cơ hội" (Match Pairs): engine sinh lưới 8 cặp giống hệt, luồng lật theo lượt (trúng đi tiếp,
/// trật úp lại + qua lượt), và bảng điểm theo SỐ CẶP (các trường hợp nhóm hạng).
/// </summary>
public class MatchPairsGameTests
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

    private static int[] Score(MatchManager mgr, Match match, Guid[] ids, params int[] pairs)
    {
        match.IsBreakRound = true;
        match.BreakGame = BreakGameType.MatchPairs;
        match.MatchPairsCount = new Dictionary<Guid, int>();
        for (int i = 0; i < 4; i++) match.MatchPairsCount[ids[i]] = pairs[i];
        return mgr.ComputeRoundScores(match);
    }

    // Quay thứ tự + bỏ qua pha hiện 5s để vào pha chơi (cho test flow).
    private static void SpinAndStart(MatchManager mgr, Match match, Guid roomId, Guid organizer)
    {
        mgr.SpinMatchPairsOrder(roomId, organizer);
        match.MatchPairsRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartMatchPairsPlay(roomId);
    }

    [Fact]
    public void Spin_EntersRevealThenPlay()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.MatchPairs;
        typeof(MatchManager).GetMethod("DealBreakMatchPairsRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });
        match.BreakOrganizerId = ids[0];

        mgr.SpinMatchPairsOrder(roomId, ids[0]);
        // Quay xong → VẪN BreakMatchSpin (pha hiện thứ tự 5s) + đã có order + reveal deadline.
        Assert.Equal(MatchStatus.BreakMatchSpin, match.Status);
        Assert.Equal(4, match.MatchPairsTurnOrder.Count);
        Assert.NotNull(match.MatchPairsRevealUntil);

        // Hết 5s → vào pha chơi.
        match.MatchPairsRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryStartMatchPairsPlay(roomId));
        Assert.Equal(MatchStatus.BreakMatchPlay, match.Status);
        Assert.NotNull(match.MatchPairsTurnDeadline);
        Assert.NotNull(match.MatchPairsDeadline);
    }

    [Fact]
    public void Board_Has8DistinctPairs_EachTwice()
    {
        var rng = new Random(123);
        for (int t = 0; t < 50; t++)
        {
            var board = MatchPairsGameEngine.BuildBoard(rng);
            Assert.Equal(16, board.Count);
            var groups = board.GroupBy(c => (c.Rank, c.Suit)).ToList();
            Assert.Equal(8, groups.Count);                 // 8 lá khác nhau
            Assert.All(groups, g => Assert.Equal(2, g.Count())); // mỗi lá đúng 2 lần
        }
    }

    [Fact]
    public void Score_FourDistinct_StandardTable()
    {
        var (mgr, match, _, ids) = Setup();
        var s = Score(mgr, match, ids, 4, 3, 1, 0);
        Assert.Equal(new[] { 2, 1, -1, -2 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Score_AllEqual_AllZero()
    {
        var (mgr, match, _, ids) = Setup();
        Assert.Equal(new[] { 0, 0, 0, 0 }, Score(mgr, match, ids, 0, 0, 0, 0));
        Assert.Equal(new[] { 0, 0, 0, 0 }, Score(mgr, match, ids, 2, 2, 2, 2));
    }

    [Fact]
    public void Score_OneWinner_RestZeroOrTie_Plus6()
    {
        var (mgr, match, _, ids) = Setup();
        // 3 người kia = 0.
        Assert.Equal(new[] { 6, -2, -2, -2 }, Score(mgr, match, ids, 8, 0, 0, 0));
        // 3 người kia bằng nhau nhưng > 0 (5-1-1-1) → vẫn +6/-2.
        Assert.Equal(new[] { 6, -2, -2, -2 }, Score(mgr, match, ids, 5, 1, 1, 1));
    }

    [Fact]
    public void Score_TwoTop_TwoBottom_Variants()
    {
        var (mgr, match, _, ids) = Setup();
        // [2,2]: top tie, bottom tie → +2/+2 / -2/-2.
        Assert.Equal(new[] { 2, 2, -2, -2 }, Score(mgr, match, ids, 3, 3, 1, 1));
        // [2,1,1]: top tie, 2 dưới khác nhau → +2/+2 / -1(người nhiều hơn) / -3(ít nhất).
        Assert.Equal(new[] { 2, 2, -1, -3 }, Score(mgr, match, ids, 3, 3, 2, 1));
        // [1,1,2]: 2 top khác nhau, 2 dưới tie → +3 / +1 / -2/-2.
        Assert.Equal(new[] { 3, 1, -2, -2 }, Score(mgr, match, ids, 5, 3, 1, 1));
    }

    [Fact]
    public void Score_ThreeTie_And_MiddleTie()
    {
        var (mgr, match, _, ids) = Setup();
        // [3,1]: 3 top tie, 1 dưới → +2/+2/+2 / -6.
        Assert.Equal(new[] { 2, 2, 2, -6 }, Score(mgr, match, ids, 2, 2, 2, 0));
        // [1,2,1]: 1 nhất, 2 giữa tie, 1 bét → +4 / +1/+1 / -6.
        Assert.Equal(new[] { 4, 1, 1, -6 }, Score(mgr, match, ids, 5, 2, 2, 0));
    }

    [Fact]
    public void Flow_SpinThenMatch_KeepTurn_MismatchPassesTurn()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.MatchPairs;
        typeof(MatchManager).GetMethod("DealBreakMatchPairsRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });
        Assert.Equal(MatchStatus.BreakMatchSpin, match.Status);
        match.BreakOrganizerId = ids[0];

        SpinAndStart(mgr, match, roomId, ids[0]);
        Assert.Equal(MatchStatus.BreakMatchPlay, match.Status);
        Assert.Equal(4, match.MatchPairsTurnOrder.Count);

        var board = match.MatchPairsBoard!;
        var first = match.MatchPairsTurnOrder[0];
        // Tìm 1 cặp (2 ô cùng rank+suit) để người đi đầu lật trúng → giữ lượt.
        int a = 0, b = -1;
        for (int i = 1; i < 16; i++)
            if (MatchPairsGameEngine.IsMatch(board, a, i)) { b = i; break; }
        Assert.True(b > 0);

        mgr.FlipMatchPairsCell(roomId, first, a);
        mgr.FlipMatchPairsCell(roomId, first, b);
        Assert.True(match.MatchPairsMatched[a] && match.MatchPairsMatched[b]);
        Assert.Equal(1, match.MatchPairsCount[first]);
        Assert.Equal(first, match.MatchPairsTurnOrder[match.MatchPairsTurnIdx % 4]); // GIỮ lượt

        // Giờ lật 2 lá KHÔNG khớp → chờ úp.
        int c = -1, d = -1;
        for (int i = 0; i < 16 && c < 0; i++)
            if (!match.MatchPairsMatched[i]) c = i;
        for (int i = 0; i < 16 && d < 0; i++)
            if (!match.MatchPairsMatched[i] && i != c && !MatchPairsGameEngine.IsMatch(board, c, i)) d = i;
        Assert.True(c >= 0 && d >= 0);
        mgr.FlipMatchPairsCell(roomId, first, c);
        mgr.FlipMatchPairsCell(roomId, first, d);
        Assert.NotNull(match.MatchPairsMismatchUntil);

        // Hết 1.5s → úp lại + qua lượt.
        match.MatchPairsMismatchUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryResolveMatchPairsMismatch(roomId));
        Assert.Empty(match.MatchPairsFlipped);
        Assert.Equal(match.MatchPairsTurnOrder[1], match.MatchPairsTurnOrder[match.MatchPairsTurnIdx % 4]); // qua người kế
    }

    [Fact]
    public void Flow_TurnTimeout_AutoFlipsMismatch_NoPairGained()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.MatchPairs;
        typeof(MatchManager).GetMethod("DealBreakMatchPairsRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);
        var first = match.MatchPairsTurnOrder[0];

        // Hết 10s lượt mà chưa lật gì → auto lật 2 lá TRẬT (8 cặp còn nguyên nên luôn tạo được trật).
        Assert.NotNull(match.MatchPairsTurnDeadline);
        match.MatchPairsTurnDeadline = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryAutoFlipMatchPairsTurn(roomId));

        // Auto lật trật → vào pha chờ úp 1.5s, KHÔNG ai được cặp.
        Assert.NotNull(match.MatchPairsMismatchUntil);
        Assert.Equal(2, match.MatchPairsFlipped.Count);
        Assert.Equal(0, match.MatchPairsCount[first]);

        // Hết 1.5s → úp + qua lượt người kế.
        match.MatchPairsMismatchUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryResolveMatchPairsMismatch(roomId));
        Assert.Equal(match.MatchPairsTurnOrder[1], match.MatchPairsTurnOrder[match.MatchPairsTurnIdx % 4]);
        Assert.NotNull(match.MatchPairsTurnDeadline); // lượt mới có đồng hồ 10s
    }

    [Fact]
    public void Flow_NotYourTurn_Throws()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true; match.BreakGame = BreakGameType.MatchPairs;
        typeof(MatchManager).GetMethod("DealBreakMatchPairsRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);
        var notTurn = match.MatchPairsTurnOrder[1];
        Assert.Throws<InvalidOperationException>(() => mgr.FlipMatchPairsCell(roomId, notTurn, 0));
    }
}
