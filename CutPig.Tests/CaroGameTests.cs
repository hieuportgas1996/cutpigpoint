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
/// Tests "Giải lao — Caro đồng đội": engine phát hiện 5 quân liên tiếp (4 hướng, kể cả bị chặn 2 đầu),
/// hòa khi bàn đầy; luồng quay chia team + đánh xen kẽ; scoring team thắng +2/người, team thua -2/người, hòa 0;
/// và nút xin hòa cần ≥1 người mỗi team đồng ý.
/// </summary>
public class CaroGameTests
{
    private static readonly FieldInfo MatchesField =
        typeof(MatchManager).GetField("_matchesByRoom", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private const int N = CaroGameEngine.Size; // 10

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

    private static int Idx(int row, int col) => row * N + col;

    // ============================== Engine ==============================

    [Fact]
    public void CheckWin_HorizontalFive_Wins()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int c = 0; c < 5; c++) b[Idx(2, c)] = 1;
        var line = CaroGameEngine.CheckWin(b, Idx(2, 4), 1);
        Assert.NotNull(line);
        Assert.Equal(5, line!.Count);
    }

    [Fact]
    public void CheckWin_VerticalFive_Wins()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int r = 0; r < 5; r++) b[Idx(r, 3)] = 2;
        Assert.NotNull(CaroGameEngine.CheckWin(b, Idx(0, 3), 2));
    }

    [Fact]
    public void CheckWin_MainDiagonalFive_Wins()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int k = 0; k < 5; k++) b[Idx(1 + k, 1 + k)] = 1;
        Assert.NotNull(CaroGameEngine.CheckWin(b, Idx(3, 3), 1));
    }

    [Fact]
    public void CheckWin_AntiDiagonalFive_Wins()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int k = 0; k < 5; k++) b[Idx(1 + k, 8 - k)] = 2;
        Assert.NotNull(CaroGameEngine.CheckWin(b, Idx(3, 6), 2));
    }

    [Fact]
    public void CheckWin_BlockedBothEnds_StillWins()
    {
        // O X X X X X O  (theo yêu cầu: bị chặn 2 đầu vẫn thắng)
        var b = CaroGameEngine.BuildBoard();
        b[Idx(4, 1)] = 2;                       // O chặn trái
        for (int c = 2; c <= 6; c++) b[Idx(4, c)] = 1;  // X X X X X
        b[Idx(4, 7)] = 2;                       // O chặn phải
        Assert.NotNull(CaroGameEngine.CheckWin(b, Idx(4, 4), 1));
    }

    [Fact]
    public void CheckWin_OnlyFour_DoesNotWin()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int c = 0; c < 4; c++) b[Idx(0, c)] = 1;
        Assert.Null(CaroGameEngine.CheckWin(b, Idx(0, 3), 1));
    }

    [Fact]
    public void CheckWin_SixInARow_AlsoWins()
    {
        var b = CaroGameEngine.BuildBoard();
        for (int c = 0; c < 6; c++) b[Idx(5, c)] = 1;
        var line = CaroGameEngine.CheckWin(b, Idx(5, 5), 1);
        Assert.NotNull(line);
        Assert.True(line!.Count >= 5);
    }

    [Fact]
    public void IsBoardFull_DetectsFull()
    {
        var b = CaroGameEngine.BuildBoard();
        Assert.False(CaroGameEngine.IsBoardFull(b));
        for (int i = 0; i < b.Length; i++) b[i] = 1;
        Assert.True(CaroGameEngine.IsBoardFull(b));
    }

    // ============================== Scoring ==============================

    private static int[] Score(MatchManager mgr, Match match, Guid[] ids, int winnerTeam, int[] teams)
    {
        match.IsBreakRound = true;
        match.BreakGame = BreakGameType.Caro;
        match.CaroMatchWinnerTeam = winnerTeam; // điểm theo team thắng CHUNG CUỘC
        match.CaroTeam = new Dictionary<Guid, int>();
        for (int i = 0; i < 4; i++) match.CaroTeam[ids[i]] = teams[i];
        return mgr.ComputeRoundScores(match);
    }

    [Fact]
    public void Score_TeamXWins_PlusTwoMinusTwo_ZeroSum()
    {
        var (mgr, match, _, ids) = Setup();
        // ids[0],ids[2] = team X (1); ids[1],ids[3] = team O (2). X thắng.
        var s = Score(mgr, match, ids, 1, new[] { 1, 2, 1, 2 });
        Assert.Equal(new[] { 2, -2, 2, -2 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Score_TeamOWins_PlusTwoMinusTwo()
    {
        var (mgr, match, _, ids) = Setup();
        var s = Score(mgr, match, ids, 2, new[] { 1, 2, 1, 2 });
        Assert.Equal(new[] { -2, 2, -2, 2 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Score_Draw_AllZero()
    {
        var (mgr, match, _, ids) = Setup();
        Assert.Equal(new[] { 0, 0, 0, 0 }, Score(mgr, match, ids, 0, new[] { 1, 2, 1, 2 }));
    }

    // ============================== Flow (mô hình 2 cặp đấu tuần tự) ==============================

    private static void Deal(Match match)
        => typeof(MatchManager).GetMethod("DealBreakCaroRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });

    // Quay chia team/cặp + bỏ qua pha hiện 5s → vào ván cặp hiện tại.
    private static void SpinAndStart(MatchManager mgr, Match match, Guid roomId, Guid organizer)
    {
        mgr.SpinCaroOrder(roomId, organizer);
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartCaroPlay(roomId);
    }

    // Cho cặp hiện tại thắng nhanh: gài sẵn 4 quân cho người đi đầu (X) rồi đặt quân thứ 5.
    private static int WinCurrentPairForX(MatchManager mgr, Match match, Guid roomId)
    {
        var xPlayer = match.CaroTurnOrder[0]; // X đi đầu trong cặp
        int xTeam = match.CaroTeam[xPlayer];  // = 1
        var board = match.CaroBoard!;
        for (int c = 0; c < 4; c++) board[Idx(0, c)] = xTeam;
        match.CaroTurnIdx = 0;
        mgr.PlaceCaroStone(roomId, xPlayer, Idx(0, 4)); // thắng cặp
        return xTeam;
    }

    [Fact]
    public void Deal_EntersSpinPhase_NoBoardYet()
    {
        var (_, match, _, _) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status);
        Assert.Null(match.CaroBoard);          // bàn deal khi vào ván cặp
        Assert.NotNull(match.CaroSpinDeadline);
    }

    [Fact]
    public void Spin_AssignsTwoTeams_TwoPairs()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];

        mgr.SpinCaroOrder(roomId, ids[0]);
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status); // pha hiện 5s
        Assert.NotNull(match.CaroRevealUntil);

        // 2 team, mỗi team 2 người.
        Assert.Equal(2, match.CaroTeam.Count(kv => kv.Value == 1));
        Assert.Equal(2, match.CaroTeam.Count(kv => kv.Value == 2));

        // 2 cặp đấu, mỗi cặp [X, O].
        Assert.Equal(2, match.CaroPairs.Count);
        Assert.All(match.CaroPairs, pr =>
        {
            Assert.Equal(1, match.CaroTeam[pr[0]]); // phần tử 0 = team X
            Assert.Equal(2, match.CaroTeam[pr[1]]); // phần tử 1 = team O
        });
        // 4 người xuất hiện đúng 1 lần trong 2 cặp.
        var all = match.CaroPairs.SelectMany(p => p).ToHashSet();
        Assert.Equal(4, all.Count);

        // Hết 5s → vào ván cặp 1.
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryStartCaroPlay(roomId));
        Assert.Equal(MatchStatus.BreakCaroPlay, match.Status);
        Assert.Equal(0, match.CaroPairIndex);
        Assert.Equal(2, match.CaroTurnOrder.Count); // chỉ 2 người của cặp đánh
        Assert.NotNull(match.CaroBoard);
        Assert.NotNull(match.CaroTurnDeadline);
    }

    [Fact]
    public void Place_NotYourTurn_Throws()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var notTurn = match.CaroTurnOrder[1];
        Assert.Throws<InvalidOperationException>(() => mgr.PlaceCaroStone(roomId, notTurn, 0));
    }

    [Fact]
    public void Place_OccupiedCell_Throws()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var first = match.CaroTurnOrder[0];
        mgr.PlaceCaroStone(roomId, first, 50);
        var next = match.CaroTurnOrder[1];
        Assert.Throws<InvalidOperationException>(() => mgr.PlaceCaroStone(roomId, next, 50));
    }

    [Fact]
    public void Place_AdvancesTurn_WithinPair()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var first = match.CaroTurnOrder[0];
        mgr.PlaceCaroStone(roomId, first, 0);
        Assert.Equal(1, match.CaroTurnIdx);
        Assert.Equal(match.CaroTurnOrder[1], match.CaroTurnOrder[match.CaroTurnIdx % 2]);
    }

    [Fact]
    public void Pair1Win_GoesToReveal_ThenPair2()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        // Cặp 1: X thắng.
        WinCurrentPairForX(mgr, match, roomId);
        // Còn cặp 2 → quay lại pha hiện 5s (BreakCaroSpin), CHƯA finalize chung cuộc.
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status);
        Assert.Equal(1, match.CaroPairIndex);
        Assert.Single(match.CaroPairWinners);
        Assert.Equal(1, match.CaroPairWinners[0]); // cặp 1: team X
        Assert.Equal(0, match.CaroMatchWinnerTeam); // chưa xong

        // Vào ván cặp 2.
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryStartCaroPlay(roomId));
        Assert.Equal(MatchStatus.BreakCaroPlay, match.Status);
        Assert.Equal(1, match.CaroPairIndex);
    }

    [Fact]
    public void BothPairsX_TeamXWinsMatch_2to0()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        int xTeam = WinCurrentPairForX(mgr, match, roomId); // cặp 1: X
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartCaroPlay(roomId);
        WinCurrentPairForX(mgr, match, roomId);             // cặp 2: X

        // Cả 2 cặp xong → finalize chung cuộc: team X thắng 2-0.
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(xTeam, match.CaroMatchWinnerTeam);
        Assert.Equal(new[] { 1, 1 }, match.CaroPairWinners.ToArray());
        Assert.All(match.Players, p =>
            Assert.Equal(match.CaroTeam[p.UserId] == xTeam ? 1 : 3, p.FinalRank));

        // Điểm: team X +2/người, team O -2/người.
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
        for (int i = 0; i < 4; i++)
            Assert.Equal(match.CaroTeam[match.Players[i].UserId] == xTeam ? 2 : -2, scores[i]);
    }

    [Fact]
    public void SplitOneEach_MatchIsDraw()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        // Cặp 1: X thắng.
        WinCurrentPairForX(mgr, match, roomId);
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartCaroPlay(roomId);

        // Cặp 2: cho O thắng (gài 4 quân O rồi O đặt quân thứ 5). O = người thứ 2 trong cặp.
        var oPlayer = match.CaroTurnOrder[1];
        int oTeam = match.CaroTeam[oPlayer]; // = 2
        var board = match.CaroBoard!;
        for (int c = 0; c < 4; c++) board[Idx(2, c)] = oTeam;
        // Đưa lượt về O: X đánh 1 nước vu vơ trước.
        var xPlayer = match.CaroTurnOrder[0];
        mgr.PlaceCaroStone(roomId, xPlayer, Idx(9, 9));
        mgr.PlaceCaroStone(roomId, oPlayer, Idx(2, 4)); // O thắng cặp 2

        // 1-1 → hòa chung cuộc, 0 điểm.
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.Equal(0, match.CaroMatchWinnerTeam);
        Assert.Equal(new[] { 1, 2 }, match.CaroPairWinners.ToArray());
        Assert.Equal(new[] { 0, 0, 0, 0 }, mgr.ComputeRoundScores(match));
        Assert.All(match.Players, p => Assert.Equal(1, p.FinalRank)); // hòa = mọi người rank 1
    }

    [Fact]
    public void DrawVote_NeedsBothPlayersOfPair()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var a = match.CaroTurnOrder[0];
        var b = match.CaroTurnOrder[1];

        // Chỉ 1 người trong cặp xin hòa → cặp CHƯA hòa.
        mgr.VoteCaroDraw(roomId, a);
        Assert.Equal(MatchStatus.BreakCaroPlay, match.Status);

        // Người thứ 2 đồng ý → cặp hòa → còn cặp 2 nên về pha hiện 5s.
        mgr.VoteCaroDraw(roomId, b);
        Assert.Equal(0, match.CaroWinnerTeam);
        Assert.Single(match.CaroPairWinners);
        Assert.Equal(0, match.CaroPairWinners[0]); // cặp 1 hòa
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status); // vào pha hiện cặp 2
    }

    [Fact]
    public void AutoSkipTurn_AdvancesWithoutPlacing()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        int filledBefore = match.CaroBoard!.Count(v => v != 0);
        match.CaroTurnDeadline = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryAutoSkipCaroTurn(roomId));
        Assert.Equal(filledBefore, match.CaroBoard!.Count(v => v != 0));
        Assert.Equal(1, match.CaroTurnIdx);
        Assert.NotNull(match.CaroTurnDeadline);
    }
}
