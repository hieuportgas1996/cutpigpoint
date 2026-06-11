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
        match.CaroWinnerTeam = winnerTeam;
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

    // ============================== Flow ==============================

    private static void Deal(Match match)
        => typeof(MatchManager).GetMethod("DealBreakCaroRound", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { match });

    private static void SpinAndStart(MatchManager mgr, Match match, Guid roomId, Guid organizer)
    {
        mgr.SpinCaroOrder(roomId, organizer);
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        mgr.TryStartCaroPlay(roomId);
    }

    [Fact]
    public void Deal_EntersSpinPhase()
    {
        var (_, match, _, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status);
        Assert.NotNull(match.CaroBoard);
        Assert.Equal(CaroGameEngine.CellCount, match.CaroBoard!.Length);
        Assert.NotNull(match.CaroSpinDeadline);
    }

    [Fact]
    public void Spin_AssignsTwoTeams_AlternatingOrder()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];

        mgr.SpinCaroOrder(roomId, ids[0]);
        // Quay xong → VẪN BreakCaroSpin (pha hiện 5s).
        Assert.Equal(MatchStatus.BreakCaroSpin, match.Status);
        Assert.NotNull(match.CaroRevealUntil);
        Assert.Equal(4, match.CaroTurnOrder.Count);

        // 2 người team X, 2 người team O.
        Assert.Equal(2, match.CaroTeam.Count(kv => kv.Value == 1));
        Assert.Equal(2, match.CaroTeam.Count(kv => kv.Value == 2));

        // Thứ tự xen kẽ X(1) → O(2) → X(1) → O(2).
        var order = match.CaroTurnOrder;
        Assert.Equal(1, match.CaroTeam[order[0]]);
        Assert.Equal(2, match.CaroTeam[order[1]]);
        Assert.Equal(1, match.CaroTeam[order[2]]);
        Assert.Equal(2, match.CaroTeam[order[3]]);

        // Hết 5s → vào pha chơi.
        match.CaroRevealUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(mgr.TryStartCaroPlay(roomId));
        Assert.Equal(MatchStatus.BreakCaroPlay, match.Status);
        Assert.NotNull(match.CaroTurnDeadline);
        Assert.NotNull(match.CaroDeadline);
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
    public void Place_AdvancesTurn_AfterNonWinningMove()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var first = match.CaroTurnOrder[0];
        mgr.PlaceCaroStone(roomId, first, 0);
        Assert.Equal(1, match.CaroTurnIdx);
        Assert.Equal(match.CaroTurnOrder[1], match.CaroTurnOrder[match.CaroTurnIdx % 4]);
    }

    [Fact]
    public void Place_FiveInARow_TeamWins_Finalizes()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        // Ép thứ tự cố định để team X (đi đầu) đặt được 4 quân hàng ngang sẵn, rồi đặt quân thứ 5 thắng.
        var xPlayer = match.CaroTurnOrder[0]; // team X đi đầu
        int xTeam = match.CaroTeam[xPlayer];  // = 1
        var board = match.CaroBoard!;
        for (int c = 0; c < 4; c++) board[Idx(0, c)] = xTeam; // gài sẵn 4 quân X
        // Bảo đảm tới lượt xPlayer.
        match.CaroTurnIdx = 0;

        mgr.PlaceCaroStone(roomId, xPlayer, Idx(0, 4)); // quân thứ 5 → thắng
        Assert.Equal(xTeam, match.CaroWinnerTeam);
        Assert.True(match.CaroWinLine.Count >= 5);
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        // FinalRank: team thắng = 1, team thua = 3.
        Assert.All(match.Players, p =>
            Assert.Equal(match.CaroTeam[p.UserId] == xTeam ? 1 : 3, p.FinalRank));
    }

    [Fact]
    public void DrawVote_NeedsBothTeams()
    {
        var (mgr, match, roomId, ids) = Setup();
        match.IsBreakRound = true;
        Deal(match);
        match.BreakOrganizerId = ids[0];
        SpinAndStart(mgr, match, roomId, ids[0]);

        var teamXplayers = match.CaroTeam.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        var teamOplayers = match.CaroTeam.Where(kv => kv.Value == 2).Select(kv => kv.Key).ToList();

        // Cả 2 người team X xin hòa → CHƯA hòa (thiếu team O).
        mgr.VoteCaroDraw(roomId, teamXplayersFirst(teamXplayers, 0));
        mgr.VoteCaroDraw(roomId, teamXplayersFirst(teamXplayers, 1));
        Assert.Equal(MatchStatus.BreakCaroPlay, match.Status);

        // 1 người team O đồng ý → đủ 2 team → hòa.
        mgr.VoteCaroDraw(roomId, teamOplayers[0]);
        Assert.Equal(0, match.CaroWinnerTeam);
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.All(match.Players, p => Assert.Equal(1, p.FinalRank)); // hòa = mọi người rank 1
    }

    private static Guid teamXplayersFirst(List<Guid> list, int i) => list[i];

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
        // Bỏ lượt: không đặt quân nào, qua người kế.
        Assert.Equal(filledBefore, match.CaroBoard!.Count(v => v != 0));
        Assert.Equal(1, match.CaroTurnIdx);
        Assert.NotNull(match.CaroTurnDeadline);
    }
}
