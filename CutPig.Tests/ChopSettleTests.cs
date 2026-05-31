using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho rule chặt heo: người bị chặt cuối (second-to-last trong chain) đã HẾT BÀI
/// (có FinalRank — Nhất/Nhì/Ba bất kỳ) → không settle, last cutter ăn 0.
/// Còn bài → settle bình thường, gánh toàn bộ pot chain[0..^1].
/// </summary>
public class ChopSettleTests
{
    // Gọi private static MatchManager.SettleTrickChopChain qua reflection.
    private static readonly MethodInfo SettleMethod =
        typeof(MatchManager).GetMethod("SettleTrickChopChain",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Settle(Match match) => SettleMethod.Invoke(null, new object[] { match });

    private static (Match match, Guid[] ids) MakeMatch(int n)
    {
        var match = new Match { RoomId = Guid.NewGuid(), HostUserId = Guid.NewGuid() };
        var ids = new Guid[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = Guid.NewGuid();
            match.Players.Add(new MatchPlayer { UserId = ids[i], SeatIndex = i, DisplayName = $"P{i + 1}" });
        }
        return (match, ids);
    }

    private static int Extra(Match m, Guid id)
    {
        m.RoundChopExtra.TryGetValue(id, out var v);
        return v;
    }

    [Fact]
    public void SecondToLast_FinishedFirst_NoSettle()
    {
        // P1 đánh 2♠ (1đ) rồi hết bài (Nhất), P2 pass (không vào chain), P3 chặt bằng 3-đôi-thông.
        // second-to-last = P1 (đã hết bài) → P3 không ăn gì.
        var (m, ids) = MakeMatch(3);
        m.Players[0].FinalRank = 1; // P1 về Nhất
        m.TrickChopChain.Add((ids[0], 1, ComboKind.Single));      // P1: 2♠
        m.TrickChopChain.Add((ids[2], 3, ComboKind.RunOfPairs));  // P3: 3 đôi thông

        Settle(m);

        Assert.Equal(0, Extra(m, ids[0]));
        Assert.Equal(0, Extra(m, ids[2]));
        Assert.Empty(m.TrickChopChain);
    }

    [Fact]
    public void SecondToLast_StillHoldingCards_SettlesFullPot()
    {
        // P1 đánh 2♠ (1đ) rồi hết bài (Nhất), P2 đánh 2♥ (2đ) CÒN bài, P3 chặt 3-đôi-thông.
        // second-to-last = P2 (còn bài) → P3 +3 (gánh cả heo P1), P2 -3, P1 net 0.
        var (m, ids) = MakeMatch(3);
        m.Players[0].FinalRank = 1; // P1 hết bài, nhưng nằm giữa chain
        // P2, P3 còn bài (FinalRank null)
        m.TrickChopChain.Add((ids[0], 1, ComboKind.Single));      // P1: 2♠
        m.TrickChopChain.Add((ids[1], 2, ComboKind.Single));      // P2: 2♥
        m.TrickChopChain.Add((ids[2], 3, ComboKind.RunOfPairs));  // P3: 3 đôi thông

        Settle(m);

        Assert.Equal(0, Extra(m, ids[0]));   // P1 net 0
        Assert.Equal(-3, Extra(m, ids[1]));  // P2 gánh pot 1+2
        Assert.Equal(3, Extra(m, ids[2]));   // P3 ăn 3
    }

    [Fact]
    public void UserExample_4Players_SecondToLastFinished_NoSettle()
    {
        // Ví dụ user: P1 đánh 2♠ (chưa hết bài) → P2 đánh 2♥ (HẾT BÀI) → P3 pass → P4 đánh 4 đôi thông.
        // chain = [P1(2♠), P2(2♥), P4(4-đôi-thông)]. second-to-last = P2 (đã hết bài) → P4 ăn 0.
        var (m, ids) = MakeMatch(4);
        m.Players[1].FinalRank = 1; // P2 hết bài
        m.TrickChopChain.Add((ids[0], 1, ComboKind.Single));      // P1: 2♠ (còn bài)
        m.TrickChopChain.Add((ids[1], 2, ComboKind.Single));      // P2: 2♥ (hết bài)
        m.TrickChopChain.Add((ids[3], 5, ComboKind.RunOfPairs));  // P4: 4 đôi thông

        Settle(m);

        Assert.Equal(0, Extra(m, ids[0]));
        Assert.Equal(0, Extra(m, ids[1]));
        Assert.Equal(0, Extra(m, ids[3]));
        Assert.Empty(m.TrickChopChain);
    }

    [Fact]
    public void LastCutterSingle_NeverSettles()
    {
        // single 2 chặn single 2 → không tính (rule cũ, vẫn giữ).
        var (m, ids) = MakeMatch(2);
        m.TrickChopChain.Add((ids[0], 1, ComboKind.Single)); // 2♠
        m.TrickChopChain.Add((ids[1], 2, ComboKind.Single)); // 2♥

        Settle(m);

        Assert.Equal(0, Extra(m, ids[0]));
        Assert.Equal(0, Extra(m, ids[1]));
    }

    [Theory]
    // (combo người hết bài bị chặt, chop value của nó, combo người chặt, chop value)
    [InlineData(ComboKind.Pair, 3, ComboKind.Four, 4)]            // đôi 2 (2♠+2♥=3) bị chặt bằng tứ quý
    [InlineData(ComboKind.Pair, 3, ComboKind.RunOfPairs, 5)]      // đôi 2 bị chặt bằng 4 đôi thông
    [InlineData(ComboKind.RunOfPairs, 3, ComboKind.Four, 4)]      // 3 đôi thông bị chặt bằng tứ quý
    [InlineData(ComboKind.RunOfPairs, 3, ComboKind.RunOfPairs, 5)]// 3 đôi thông bị chặt bằng 4 đôi thông
    [InlineData(ComboKind.Four, 4, ComboKind.RunOfPairs, 5)]      // tứ quý non-2 bị chặt bằng 4 đôi thông
    public void FinishedPlayerCut_AnyComboKind_NoSettle(
        ComboKind victimKind, int victimVal, ComboKind cutterKind, int cutterVal)
    {
        // Người đã HẾT BÀI đánh đôi 2 / 3 đôi thông / tứ quý rồi bị chặt → cutter ăn 0,
        // bất kể loại combo. (rule chung: second-to-last hết bài → không settle)
        var (m, ids) = MakeMatch(2);
        m.Players[0].FinalRank = 1; // người bị chặt đã hết bài
        m.TrickChopChain.Add((ids[0], victimVal, victimKind));
        m.TrickChopChain.Add((ids[1], cutterVal, cutterKind));

        Settle(m);

        Assert.Equal(0, Extra(m, ids[0]));
        Assert.Equal(0, Extra(m, ids[1]));
        Assert.Empty(m.TrickChopChain);
    }

    [Fact]
    public void NormalChop_BothActive_Settles()
    {
        // Cả 2 còn bài: P1 2♥ (2đ) → P2 tứ quý (4đ chặt). P2 +2, P1 -2.
        var (m, ids) = MakeMatch(2);
        m.TrickChopChain.Add((ids[0], 2, ComboKind.Single)); // P1: 2♥
        m.TrickChopChain.Add((ids[1], 4, ComboKind.Four));   // P2: tứ quý

        Settle(m);

        Assert.Equal(-2, Extra(m, ids[0]));
        Assert.Equal(2, Extra(m, ids[1]));
    }
}
