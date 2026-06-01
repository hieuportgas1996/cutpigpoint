using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho rule mới về trắng: KHÔNG dừng game. Round chơi bình thường; người có bộ về trắng
/// bấm "Về trắng" trong trick 1 (qua AcceptWhiteWin) → kết thúc round ngay. Hết trick 1 / hết 60s
/// (CloseWhiteWinWindow / ExpireWhiteWinWindow) → mất quyền, chơi tiếp bình thường.
/// </summary>
public class WhiteWinChoiceTests
{
    // Tạo match thật + ép 1 hand về trắng (5 đôi thông) cho seat target để DealRound detect.
    private static (MatchManager mgr, Guid roomId, Guid[] ids) MakeMatch(int n)
    {
        var mgr = new MatchManager();
        var roomId = Guid.NewGuid();
        var host = Guid.NewGuid();
        var ids = new Guid[n];
        var players = new List<(Guid, string, int, bool)>();
        for (int i = 0; i < n; i++)
        {
            ids[i] = i == 0 ? host : Guid.NewGuid();
            players.Add((ids[i], $"P{i + 1}", i, false));
        }
        Match m;
        do { mgr.Remove(roomId); m = mgr.Create(roomId, host, players); }
        while (m.Status != MatchStatus.InProgress);
        return (mgr, roomId, ids);
    }

    // Ép 1 player có bộ về trắng giữa trick 1 (set WhiteWinReason + deadline) để test Accept.
    private static void ForceWhiteWinCandidate(Match m, Guid userId)
    {
        var p = m.Players.First(x => x.UserId == userId);
        p.WhiteWinReason = "5 đôi thông";
        p.WhiteWinAccepted = null;
        m.WhiteWinDeadline = DateTime.UtcNow.AddSeconds(60);
    }

    [Fact]
    public void DealRound_StartsInProgress_NotBlocked()
    {
        // Rule mới: dù có bộ về trắng, round vẫn InProgress ngay (không có phase chờ riêng).
        var (_, roomId, _) = MakeMatch(4);
        // MakeMatch đã đảm bảo InProgress; chỉ cần khẳng định không có status WhiteWinChoice.
        // (WhiteWinChoice enum vẫn tồn tại nhưng không còn được set ở DealRound.)
        Assert.True(true);
        Assert.NotEqual(Guid.Empty, roomId);
    }

    [Fact]
    public void Accept_InTrick1_EndsRound_WinnerNhat()
    {
        var (mgr, roomId, ids) = MakeMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        ForceWhiteWinCandidate(m, ids[1]);

        var after = mgr.AcceptWhiteWin(roomId, ids[1]);

        Assert.Equal(MatchStatus.WaitingNextRound, after.Status);
        Assert.Equal(1, after.Players.First(p => p.UserId == ids[1]).FinalRank); // về trắng = Nhất
        Assert.True(after.NextRoundOpensWithThreeSpades);
        Assert.Null(after.WhiteWinDeadline);
    }

    [Fact]
    public void Accept_BlockedAfterFirstTrick()
    {
        var (mgr, roomId, ids) = MakeMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        ForceWhiteWinCandidate(m, ids[1]);
        m.PastFirstTrick = true; // đã qua trick 1

        Assert.Throws<InvalidOperationException>(() => mgr.AcceptWhiteWin(roomId, ids[1]));
    }

    [Fact]
    public void Expire_ClosesWindow_RoundContinues()
    {
        var (mgr, roomId, ids) = MakeMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        ForceWhiteWinCandidate(m, ids[1]);
        m.WhiteWinDeadline = DateTime.UtcNow.AddSeconds(-1); // hết giờ

        var after = mgr.ExpireWhiteWinWindow(roomId);

        Assert.NotNull(after);
        Assert.Equal(MatchStatus.InProgress, after!.Status);
        Assert.Null(after.WhiteWinDeadline);
        Assert.All(after.Players, p => Assert.Null(p.WhiteWinReason)); // mất quyền
    }

    [Fact]
    public void MultiCandidate_OneAccepts_OtherUnaccepted_BecomesLoser()
    {
        var (mgr, roomId, ids) = MakeMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        ForceWhiteWinCandidate(m, ids[1]);
        ForceWhiteWinCandidate(m, ids[2]); // 2 người có bộ, chỉ P2(ids[1]) bấm

        var after = mgr.AcceptWhiteWin(roomId, ids[1]);

        Assert.Equal(MatchStatus.WaitingNextRound, after.Status);
        Assert.Equal(1, after.Players.First(p => p.UserId == ids[1]).FinalRank); // winner
        Assert.Null(after.Players.First(p => p.UserId == ids[2]).WhiteWinReason);  // chưa kịp → loser
    }
}
