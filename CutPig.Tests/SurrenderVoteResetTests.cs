using System;
using System.Collections.Generic;
using System.Linq;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho 2 tính năng mới: Đầu hàng (Surrender) và Vote chia bài lại (VoteReset).
/// Dùng MatchManager qua public API + reflection-free (tạo match thật qua Create).
/// </summary>
public class SurrenderVoteResetTests
{
    // Tạo match 4 người + deal thật. Tránh trường hợp white-win bằng cách thử lại vài lần
    // (deal random, white-win rất hiếm nhưng có thể xảy ra → ép InProgress).
    private static (MatchManager mgr, Guid roomId, Guid[] ids) MakeStartedMatch(int n)
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
        do
        {
            mgr.Remove(roomId);
            m = mgr.Create(roomId, host, players);
        } while (m.Status != MatchStatus.InProgress); // bỏ qua deal ra white-win
        return (mgr, roomId, ids);
    }

    [Fact]
    public void Surrender_FirstGetsLastRank_RoundContinues()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;

        // P3 (ids[2]) đầu hàng đầu tiên → hạng 4 (chót), ván vẫn tiếp tục.
        var r = mgr.Surrender(roomId, ids[2]);
        Assert.False(r.RoundEnded);
        Assert.Equal(4, m.Players.First(p => p.UserId == ids[2]).FinalRank);
        Assert.True(m.Players.First(p => p.UserId == ids[2]).Surrendered);

        // P4 (ids[3]) đầu hàng tiếp → hạng 3 (người đầu hàng sau hạng cao hơn).
        var r2 = mgr.Surrender(roomId, ids[3]);
        Assert.Equal(3, m.Players.First(p => p.UserId == ids[3]).FinalRank);

        // Còn lại 2 người (P1, P2) chưa có hạng → ván vẫn tiếp tục (remaining=2 > 1).
        Assert.False(r2.RoundEnded);
        Assert.Equal(2, m.Players.Count(p => p.FinalRank == null));
    }

    [Fact]
    public void Surrender_EndsRoundWhenOneLeft()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(2);
        var m = mgr.GetByRoom(roomId)!;

        // 2 người: 1 người đầu hàng → còn 1 người → ván kết thúc ngay.
        var loser = m.CurrentTurnSeatIndex == 0 ? ids[1] : ids[0]; // ai cũng được, chọn người không phải lượt cho chắc
        var r = mgr.Surrender(roomId, loser);
        Assert.True(r.RoundEnded);
        Assert.Equal(MatchStatus.WaitingNextRound, m.Status);
        Assert.Equal(2, m.Players.First(p => p.UserId == loser).FinalRank);     // chót
        Assert.Equal(1, m.Players.First(p => p.UserId != loser).FinalRank);     // người còn lại Nhất
    }

    [Fact]
    public void Surrender_BlockedAfterFinalRank()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        mgr.Surrender(roomId, ids[2]); // P3 đầu hàng
        // Đầu hàng lần 2 cùng người → lỗi.
        Assert.Throws<InvalidOperationException>(() => mgr.Surrender(roomId, ids[2]));
    }

    [Fact]
    public void VoteReset_TwoYes_RedealsSameRoundNumber()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        int roundBefore = m.RoundNumber;
        var handsBefore = m.Players.Select(p => string.Join(",", p.Hand.Select(c => $"{c.Rank}{c.Suit}"))).ToArray();

        // P1 mở vote (tự +1 phiếu), P2 đồng ý → đủ 2 → chia lại.
        var r1 = mgr.StartVoteReset(roomId, ids[0]);
        Assert.False(r1.Dealt); // mới 1 phiếu
        Assert.Equal(MatchStatus.VoteReset, m.Status);

        var r2 = mgr.RespondVoteReset(roomId, ids[1], true);
        Assert.True(r2.Dealt);
        Assert.Equal(MatchStatus.InProgress, m.Status);
        Assert.Equal(roundBefore, m.RoundNumber); // không nhảy số ván

        // Bài đã được chia lại (khác bài cũ ở ít nhất 1 người — xác suất giống hệt ~0).
        var handsAfter = m.Players.Select(p => string.Join(",", p.Hand.Select(c => $"{c.Rank}{c.Suit}"))).ToArray();
        Assert.True(handsBefore.Where((h, i) => h != handsAfter[i]).Any());
    }

    [Fact]
    public void VoteReset_AllDecided_NotEnough_Cancels()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;

        mgr.StartVoteReset(roomId, ids[0]);       // P1 +1 (đồng ý)
        mgr.RespondVoteReset(roomId, ids[1], false);
        mgr.RespondVoteReset(roomId, ids[2], false);
        var r = mgr.RespondVoteReset(roomId, ids[3], false); // tất cả đã bỏ, chỉ 1 đồng ý
        Assert.False(r.Dealt);
        Assert.Equal(MatchStatus.InProgress, m.Status); // huỷ vote, chơi tiếp
    }

    [Fact]
    public void VoteReset_OncePerPlayerPerMatch()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;

        // P1 mở vote (tiêu quyền), không đủ phiếu → huỷ.
        mgr.StartVoteReset(roomId, ids[0]);
        mgr.RespondVoteReset(roomId, ids[1], false);
        mgr.RespondVoteReset(roomId, ids[2], false);
        mgr.RespondVoteReset(roomId, ids[3], false);
        Assert.Equal(MatchStatus.InProgress, m.Status);

        // P1 mở lại → lỗi vì đã dùng quyền.
        Assert.Throws<InvalidOperationException>(() => mgr.StartVoteReset(roomId, ids[0]));
    }

    [Fact]
    public void VoteReset_OnlyInitiatorConsumesRight()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);

        // P1 mở vote (tiêu quyền), P2 đồng ý → đủ 2 phiếu → deal lại.
        mgr.StartVoteReset(roomId, ids[0]);
        var r = mgr.RespondVoteReset(roomId, ids[1], true);
        Assert.True(r.Dealt);

        // Chỉ initiator P1 mất quyền; responder P2 (dù đã "Đồng ý") VẪN giữ quyền mở vote.
        Assert.True(r.Match.Players.First(p => p.UserId == ids[0]).HasUsedVoteReset);
        Assert.False(r.Match.Players.First(p => p.UserId == ids[1]).HasUsedVoteReset);

        // P2 vẫn mở được vote ở round mới (chỉ thử nếu bài mới không phải white-win → đang InProgress).
        if (r.Match.Status == MatchStatus.InProgress)
        {
            var r2 = mgr.StartVoteReset(roomId, ids[1]);
            Assert.Equal(MatchStatus.VoteReset, r2.Match.Status);
        }
    }

    [Fact]
    public void VoteReset_BlockedAfterFirstTrick()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        m.PastFirstTrick = true; // giả lập đã qua trick 1
        Assert.Throws<InvalidOperationException>(() => mgr.StartVoteReset(roomId, ids[0]));
    }

    [Fact]
    public void VoteReset_RightNotRestoredNextRound()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;

        // P1 dùng quyền vote (mở vote → tiêu quyền), vote không thành.
        mgr.StartVoteReset(roomId, ids[0]);
        mgr.RespondVoteReset(roomId, ids[1], false);
        mgr.RespondVoteReset(roomId, ids[2], false);
        mgr.RespondVoteReset(roomId, ids[3], false);
        Assert.True(m.Players.First(p => p.UserId == ids[0]).HasUsedVoteReset);

        // Kết thúc round bằng đầu hàng dồn cho tới khi WaitingNextRound, rồi sang round mới.
        foreach (var id in new[] { ids[1], ids[2], ids[3] })
            if (m.Players.First(p => p.UserId == id).FinalRank == null && m.Status == MatchStatus.InProgress)
                mgr.Surrender(roomId, id);
        Assert.Equal(MatchStatus.WaitingNextRound, m.Status);

        var next = mgr.StartNextRound(roomId, null);

        // Quyền vote của P1 VẪN bị tiêu ở round mới (1 lần / trận, không reset ở DealRound).
        Assert.True(next.Players.First(p => p.UserId == ids[0]).HasUsedVoteReset);
    }

    [Fact]
    public void Festival_ScheduledRoundDealsThreeCards_AndScoresZeroSum()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;

        // P1 tổ chức lễ hội → round hiện tại vẫn TLMN, chưa phải festival.
        mgr.ScheduleFestival(roomId, ids[0]);
        Assert.True(m.FestivalScheduled);
        Assert.False(m.IsFestivalRound);
        Assert.True(m.Players.First(p => p.UserId == ids[0]).HasUsedFestival);

        // Kết thúc round hiện tại (đầu hàng dồn) → WaitingNextRound.
        foreach (var id in new[] { ids[1], ids[2], ids[3] })
            if (m.Players.First(p => p.UserId == id).FinalRank == null && m.Status == MatchStatus.InProgress)
                mgr.Surrender(roomId, id);
        Assert.Equal(MatchStatus.WaitingNextRound, m.Status);

        // Round kế tiếp = lễ hội: chia 3 lá/người, vào pha nặn bài FestivalReveal.
        var fest = mgr.StartNextRound(roomId, null);
        Assert.True(fest.IsFestivalRound);
        Assert.False(fest.FestivalScheduled); // đã tiêu
        Assert.All(fest.Players, p => Assert.Equal(3, p.Hand.Count));
        Assert.Equal(MatchStatus.FestivalReveal, fest.Status);
        Assert.Contains(fest.Players, p => p.FestivalWinner);
        Assert.Equal(ids[0], fest.FestivalOrganizerId); // P1 là người tổ chức

        // Mọi người lật hết bài → finalize → WaitingNextRound.
        foreach (var id in ids) mgr.FlipFestivalCard(roomId, id, flipAll: true);
        var done = mgr.FinalizeFestival(roomId);
        Assert.NotNull(done);
        Assert.Equal(MatchStatus.WaitingNextRound, done!.Status);

        // Điểm zero-sum.
        var scores = mgr.ComputeRoundScores(fest);
        Assert.Equal(0, scores.Sum());
        // Nếu có loser (không phải mọi người đồng hạng): winner(s) dương, mỗi loser -2.
        bool anyLoser = fest.Players.Any(p => !p.FestivalWinner);
        if (anyLoser)
        {
            for (int i = 0; i < fest.Players.Count; i++)
            {
                if (fest.Players[i].FestivalWinner) Assert.True(scores[i] > 0);
                else Assert.Equal(-2, scores[i]);
            }
        }
    }

    [Fact]
    public void Festival_OncePerMatch()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        mgr.ScheduleFestival(roomId, ids[0]);
        // Đã có người đặt → người khác không đặt được round này.
        Assert.Throws<InvalidOperationException>(() => mgr.ScheduleFestival(roomId, ids[1]));
    }

    [Fact]
    public void Festival_RevealFlow_FlipPerCard_ThenViewDeadline()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        mgr.ScheduleFestival(roomId, ids[0]);
        foreach (var id in new[] { ids[1], ids[2], ids[3] })
            if (m.Players.First(p => p.UserId == id).FinalRank == null && m.Status == MatchStatus.InProgress)
                mgr.Surrender(roomId, id);
        var fest = mgr.StartNextRound(roomId, null);
        Assert.Equal(MatchStatus.FestivalReveal, fest.Status);

        // Lật từng lá: mỗi người lật 1 → revealed=1.
        foreach (var id in ids) mgr.FlipFestivalCard(roomId, id, flipAll: false);
        Assert.All(fest.Players, p => Assert.Equal(1, p.FestivalRevealed));
        Assert.Null(fest.FestivalRevealDeadline); // chưa lật hết

        // Lật hết cho tất cả → set deadline xem bài 5s.
        foreach (var id in ids) mgr.FlipFestivalCard(roomId, id, flipAll: true);
        Assert.All(fest.Players, p => Assert.Equal(3, p.FestivalRevealed));
        Assert.NotNull(fest.FestivalRevealDeadline);
    }

    [Fact]
    public void Festival_AutoFlip_RevealsAll()
    {
        var (mgr, roomId, ids) = MakeStartedMatch(4);
        var m = mgr.GetByRoom(roomId)!;
        mgr.ScheduleFestival(roomId, ids[0]);
        foreach (var id in new[] { ids[1], ids[2], ids[3] })
            if (m.Players.First(p => p.UserId == id).FinalRank == null && m.Status == MatchStatus.InProgress)
                mgr.Surrender(roomId, id);
        mgr.StartNextRound(roomId, null);

        var flipped = mgr.AutoFlipFestival(roomId);
        Assert.NotNull(flipped);
        Assert.All(flipped!.Players, p => Assert.Equal(3, p.FestivalRevealed));
        Assert.NotNull(flipped.FestivalRevealDeadline);
    }
}
