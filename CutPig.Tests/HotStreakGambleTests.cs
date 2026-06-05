using System.Linq;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho "Liều Ăn Nhiều" (Hot Streak Gamble): y hệt Ngôi Sao Hi Vọng nhưng nhân ×3 (thay vì ×2)
/// mọi giao dịch điểm dính người liều (cả 2 chiều thắng/thua), zero-sum theo cặp. Đếm streak về Nhất
/// qua BuildRoundEndDto; round biến tấu (lễ hội/xì dách) không tính streak.
/// </summary>
public class HotStreakGambleTests
{
    private static (Match match, System.Guid[] ids) MakeMatch(int n)
    {
        var match = new Match { RoomId = System.Guid.NewGuid(), HostUserId = System.Guid.NewGuid() };
        var ids = new System.Guid[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = System.Guid.NewGuid();
            match.Players.Add(new MatchPlayer { UserId = ids[i], SeatIndex = i, DisplayName = $"P{i + 1}" });
        }
        return (match, ids);
    }

    private static Card RedTwo => new(15, Suit.Hearts); // 2♥ = heo đỏ 2đ

    private readonly MatchManager _mgr = new();

    // ---- Base rank ×3 ----

    [Fact]
    public void Rank4_GamblerNhat_TriplesNhatBetPair_KeepsNhiBa()
    {
        // P1🔥 Nhất +2→+6, P2 Nhì +1 giữ, P3 Ba -1 giữ, P4 Bét -2→-6.
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 6, 1, -1, -6 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Rank4_GamblerBet_TriplesNhatBetPair()
    {
        // P4🔥 Bét -2→-6 (đối tiền với Nhất P1 +2→+6).
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4; m.Players[3].IsGambling = true;

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 6, 1, -1, -6 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Rank4_GamblerMiddle_TriplesNhiBaPair_KeepsNhatBet()
    {
        // P2🔥 Nhì +1→+3, đối tiền Ba P3 -1→-3. Nhất/Bét cặp riêng giữ ±2.
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2; m.Players[1].IsGambling = true;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 2, 3, -3, -2 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Rank2_GamblerNhat_Tripled()
    {
        // 2 người: +1/-1. P1🔥 Nhất +1→+3, Bét -1→-3.
        var (m, _) = MakeMatch(2);
        m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        m.Players[1].FinalRank = 2;
        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 3, -3 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void Rank3_GamblerNhat_TriplesNhatBet_MiddleZero()
    {
        // 3 người: +2/0/-2. P1🔥 Nhất +2→+6, Bét -2→-6, Nhì 0 giữ.
        var (m, _) = MakeMatch(3);
        m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 6, 0, -6 }, s);
        Assert.Equal(0, s.Sum());
    }

    [Fact]
    public void NoGambler_LeavesScoresUnchanged()
    {
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 2, 1, -1, -2 }, s);
    }

    // ---- Chop-pig ×3 ----

    [Fact]
    public void Chop_GamblerIsCutter_TriplesChopAndRank()
    {
        // P1🔥 Nhất chặt heo của P4 Bét (pot 2). base P1 +2+2=+4, P4 -2-2=-4. ×3: P1 +12, P4 -12.
        var (m, ids) = MakeMatch(4);
        m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        m.RoundChopExtra[ids[0]] = +2;
        m.RoundChopExtra[ids[3]] = -2;

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(12, s[0]);
        Assert.Equal(1, s[1]);
        Assert.Equal(-1, s[2]);
        Assert.Equal(-12, s[3]);
        Assert.Equal(0, s.Sum());
    }

    // ---- White-win ×3 ----

    [Fact]
    public void WhiteWin_GamblerSoleWinner_TriplesAll()
    {
        // 4 người, P1🔥 về trắng: base P1 +6, mỗi người kia -2. ×3: P1 +18, mỗi người -6.
        var (m, _) = MakeMatch(4);
        m.Players[0].WhiteWinReason = "Sảnh rồng"; m.Players[0].IsGambling = true;
        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(new[] { 18, -6, -6, -6 }, s);
        Assert.Equal(0, s.Sum());
    }

    // ---- Judge ×3 ----

    [Fact]
    public void Judge_GamblerWinner_TriplesAllVictimPayments()
    {
        // 4 người Case A: P1🔥 Nhất phán xử, 3 victim held=0. base: mỗi victim -4, P1 +12. ×3: -12 / +36.
        var (m, _) = MakeMatch(4);
        m.JudgeTriggered = true;
        m.Players[0].JudgeIsWinner = true; m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        for (int i = 1; i < 4; i++) { m.Players[i].JudgeIsVictim = true; m.Players[i].FinalRank = 4; }

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(36, s[0]);
        Assert.Equal(-12, s[1]);
        Assert.Equal(-12, s[2]);
        Assert.Equal(-12, s[3]);
        Assert.Equal(0, s.Sum());
    }

    // ---- Held penalty ×3 (zero-sum) ----

    [Fact]
    public void HeldPenalty_GamblerIsChot_Tripled_ZeroSum()
    {
        // 4 người, P4🔥 Chót còn 1 heo đỏ (held=2). base P4 = -2(rank) -2(held) = -4 → ×3 = -12.
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4; m.Players[3].IsGambling = true;
        m.Players[3].Hand.Add(RedTwo);

        var s = _mgr.ComputeRoundScores(m);
        Assert.Equal(-12, s[3]);
        Assert.Equal(0, s.Sum());
    }

    // ---- Breakdown GambleDelta reconciles ----

    [Fact]
    public void Breakdown_GambleDelta_MatchesFinalTotal()
    {
        var (m, _) = MakeMatch(4);
        m.IsGambleRound = true;
        m.Players[0].FinalRank = 1; m.Players[0].IsGambling = true;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        var final = _mgr.ComputeRoundScores(m);
        var bds = _mgr.ComputeRoundScoreBreakdowns(m);
        for (int i = 0; i < 4; i++)
            Assert.Equal(final[i], bds[i].Total);
        // Người liều: base +2, GambleDelta = 6-2 = 4.
        Assert.Equal(4, bds[0].GambleDelta);
        Assert.Equal(2, bds[0].BaseRank);
    }

    // ---- Streak tracking qua BuildRoundEndDto ----

    [Fact]
    public void Streak_IncrementsOnNhat_ResetsOnNonNhat()
    {
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;

        _mgr.BuildRoundEndDto(m);
        Assert.Equal(1, m.Players[0].WinStreak);
        Assert.Equal(0, m.Players[1].WinStreak);

        _mgr.BuildRoundEndDto(m);
        Assert.Equal(2, m.Players[0].WinStreak);

        m.Players[0].FinalRank = 3;
        m.Players[1].FinalRank = 1;
        _mgr.BuildRoundEndDto(m);
        Assert.Equal(0, m.Players[0].WinStreak);
        Assert.Equal(1, m.Players[1].WinStreak);
    }

    [Fact]
    public void Streak_FifthNhat_SetsGambleOffer_DoesNotResetStreak()
    {
        var (m, ids) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;

        for (int r = 0; r < MatchManager.GambleStreakThreshold - 1; r++)
        {
            _mgr.BuildRoundEndDto(m);
            Assert.Null(m.GambleOfferUserId);
            Assert.Equal(r + 1, m.Players[0].WinStreak);
        }
        _mgr.BuildRoundEndDto(m); // ván thứ 5
        Assert.Equal(ids[0], m.GambleOfferUserId);
        // Đạt 5 → mời nhưng KHÔNG reset chuỗi — streak vẫn = 5, đếm tiếp tới mốc 10.
        Assert.Equal(5, m.Players[0].WinStreak);
        Assert.Equal(5, m.Players[0].GambleOfferedAtStreak);
    }

    [Fact]
    public void Streak_CountsPastThreshold_NoCap()
    {
        // KHÔNG cap: thắng 8 ván liên tiếp → streak = 8 (kể cả khi lời mời mốc-5 bị treo).
        var (m, ids) = MakeMatch(4);
        m.GambleOfferUserId = ids[3]; // giả lập đã có lời mời treo cho P4 → mốc-5 của P1 hoãn
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        for (int r = 0; r < 8; r++) _mgr.BuildRoundEndDto(m); // thắng 8 ván liên tiếp
        Assert.Equal(8, m.Players[0].WinStreak); // không cap
    }

    [Fact]
    public void Streak_OffersAgainAtTenAndFifteen()
    {
        // Mời lại ở MỖI mốc bội-5: 5, 10, 15… (không reset chuỗi). Từ chối mốc trước rồi tiếp tục thắng.
        var (m, ids) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;

        // Tới mốc 5 → mời.
        for (int r = 0; r < 5; r++) _mgr.BuildRoundEndDto(m);
        Assert.Equal(ids[0], m.GambleOfferUserId);
        Assert.Equal(5, m.Players[0].WinStreak);

        // P1 từ chối lời mời mốc 5 (clear offer). Thắng tiếp 5→9: KHÔNG mời lại (chưa tới mốc 10).
        m.GambleOfferUserId = null; m.GambleOfferDeadline = null;
        for (int r = 0; r < 4; r++) _mgr.BuildRoundEndDto(m); // streak 6,7,8,9
        Assert.Null(m.GambleOfferUserId);
        Assert.Equal(9, m.Players[0].WinStreak);

        // Mốc 10 → mời lại.
        _mgr.BuildRoundEndDto(m); // streak 10
        Assert.Equal(ids[0], m.GambleOfferUserId);
        Assert.Equal(10, m.Players[0].WinStreak);
        Assert.Equal(10, m.Players[0].GambleOfferedAtStreak);

        // Từ chối mốc 10, thắng tới 15 → mời lại lần nữa.
        m.GambleOfferUserId = null; m.GambleOfferDeadline = null;
        for (int r = 0; r < 5; r++) _mgr.BuildRoundEndDto(m); // streak 11..15
        Assert.Equal(ids[0], m.GambleOfferUserId);
        Assert.Equal(15, m.Players[0].WinStreak);
    }

    [Fact]
    public void Streak_NonNhat_ResetsStreakAndMilestoneMarker()
    {
        // Thua → reset cả WinStreak lẫn GambleOfferedAtStreak (chuỗi mới đếm lại từ đầu, mốc-5 mời lại được).
        var (m, ids) = MakeMatch(4);
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        for (int r = 0; r < 5; r++) _mgr.BuildRoundEndDto(m); // mốc 5 → mời + đánh dấu
        Assert.Equal(5, m.Players[0].GambleOfferedAtStreak);
        m.GambleOfferUserId = null; m.GambleOfferDeadline = null;

        // P1 thua 1 ván.
        m.Players[0].FinalRank = 4; m.Players[3].FinalRank = 1;
        _mgr.BuildRoundEndDto(m);
        Assert.Equal(0, m.Players[0].WinStreak);
        Assert.Equal(0, m.Players[0].GambleOfferedAtStreak); // marker reset

        // Thắng lại 5 ván → mời lại được ở mốc 5 mới.
        m.Players[0].FinalRank = 1; m.Players[3].FinalRank = 4;
        for (int r = 0; r < 5; r++) _mgr.BuildRoundEndDto(m);
        Assert.Equal(ids[0], m.GambleOfferUserId);
        Assert.Equal(5, m.Players[0].WinStreak);
    }

    [Fact]
    public void Offer_SetsDeadline_DoesNotBlockNextRound()
    {
        // Đạt 5 → set offer + GambleOfferDeadline; KHÔNG đụng NextRoundAt (ván n+1 chơi bình thường).
        var (m, ids) = MakeMatch(4);
        var when = System.DateTime.UtcNow.AddSeconds(20);
        m.NextRoundAt = when; // giả lập đã hẹn deal ván n+1
        m.Players[0].FinalRank = 1;
        m.Players[1].FinalRank = 2;
        m.Players[2].FinalRank = 3;
        m.Players[3].FinalRank = 4;
        for (int r = 0; r < MatchManager.GambleStreakThreshold; r++) _mgr.BuildRoundEndDto(m);

        Assert.Equal(ids[0], m.GambleOfferUserId);
        Assert.NotNull(m.GambleOfferDeadline);
        Assert.Equal(when, m.NextRoundAt); // KHÔNG bị chặn — ván n+1 vẫn deal đúng hẹn
    }

    [Fact]
    public void Streak_FestivalRound_DoesNotTouchStreakNorOffer()
    {
        var (m, _) = MakeMatch(4);
        m.Players[0].FinalRank = 1; m.Players[1].FinalRank = 2; m.Players[2].FinalRank = 3; m.Players[3].FinalRank = 4;
        for (int r = 0; r < 4; r++) _mgr.BuildRoundEndDto(m);
        Assert.Equal(4, m.Players[0].WinStreak);

        m.IsFestivalRound = true;
        m.Players[0].FestivalWinner = true;
        _mgr.BuildRoundEndDto(m);
        Assert.Equal(4, m.Players[0].WinStreak); // giữ nguyên, không tăng
    }
}
