using System.Reflection;
using CutPig.Services;
using CutPig.GameEngine;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho engine Xì Dách (round Sát Phạt). Trọng tâm: điểm lá (A linh hoạt theo số lá),
/// phân loại tay (xì dách/vàng/ngũ linh/quắc), và so điểm cặp Nhà Cái vs Player.
/// </summary>
public class XiDachTests
{
    // rank: 3..10 = pip, 11=J 12=Q 13=K 14=A 15="2"
    private static Card C(int rank, Suit s = Suit.Spades) => new(rank, s);

    // ---- Card points ----

    [Fact]
    public void Ace_Is10_With2or3Cards_1_With4or5()
    {
        // A + 5 (2 lá) → A=10 → 15
        Assert.Equal(15, XiDachEngine.Total(new[] { C(14), C(5) }));
        // A + 3 + 4 + A + 3 (5 lá) → mỗi A=1 → 1+3+4+1+3 = 12
        Assert.Equal(12, XiDachEngine.Total(new[] { C(14), C(3), C(4), C(14, Suit.Hearts), C(3, Suit.Hearts) }));
    }

    [Fact]
    public void FaceCards_Are10_TwoIsTwo()
    {
        Assert.Equal(20, XiDachEngine.Total(new[] { C(11), C(13) }));          // J + K = 20
        Assert.Equal(12, XiDachEngine.Total(new[] { C(10), C(15) }));          // 10 + "2"(=2) = 12
    }

    // ---- Classify ----

    [Fact]
    public void XiDach_Ace_Plus_Ten()
    {
        Assert.Equal(XiDachEngine.HandKind.XiDach, XiDachEngine.Classify(new[] { C(14), C(13) })); // A+K
        Assert.Equal(XiDachEngine.HandKind.XiDach, XiDachEngine.Classify(new[] { C(14), C(10) })); // A+10
    }

    [Fact]
    public void XiVang_Two_Aces()
    {
        Assert.Equal(XiDachEngine.HandKind.XiVang, XiDachEngine.Classify(new[] { C(14), C(14, Suit.Hearts) }));
    }

    [Fact]
    public void FiveCard_FiveCards_Under21()
    {
        // 3+4+5+2("2")+? need ≤21 with 5 cards: 3,4,5,15(=2),6 → 3+4+5+2+6 = 20
        Assert.Equal(XiDachEngine.HandKind.FiveCard, XiDachEngine.Classify(new[] { C(3), C(4), C(5), C(15), C(6) }));
    }

    [Fact]
    public void Bust_Over21()
    {
        Assert.Equal(XiDachEngine.HandKind.Bust, XiDachEngine.Classify(new[] { C(13), C(12), C(5) })); // 10+10+5 = 25
    }

    // ---- MustDraw / CanStand ----

    [Fact]
    public void Player_Under16_MustDraw()
    {
        Assert.True(XiDachEngine.MustDraw(new[] { C(5), C(9) }, isDealer: false)); // 14 < 16
        Assert.False(XiDachEngine.MustDraw(new[] { C(7), C(9) }, isDealer: false)); // 16 ≥ 16
    }

    [Fact]
    public void Dealer_Under15_MustDraw()
    {
        Assert.True(XiDachEngine.MustDraw(new[] { C(5), C(9) }, isDealer: true)); // 14 < 15
        Assert.False(XiDachEngine.MustDraw(new[] { C(6), C(9) }, isDealer: true)); // 15 ≥ 15
    }

    // ---- ComparePlayerDelta: spec example (P1 = nhà cái 15) ----

    [Fact]
    public void SpecExample_DealerVsPlayers()
    {
        // P1 nhà cái = 15 (vd 7+8). P2 quắc(22) → player thua -2. P3 = 16 → player thắng +2. P4 = 15 → hòa 0.
        var dealer = new[] { C(7), C(8) };               // 15
        var p2 = new[] { C(13), C(12), C(15) };          // 10+10+2 = 22 quắc
        var p3 = new[] { C(7), C(9) };                   // 16
        var p4 = new[] { C(7), C(8, Suit.Hearts) };      // 15

        Assert.Equal(-2, XiDachEngine.ComparePlayerDelta(dealer, p2)); // P2 quắc thua
        Assert.Equal(+2, XiDachEngine.ComparePlayerDelta(dealer, p3)); // P3 cao hơn thắng
        Assert.Equal(0, XiDachEngine.ComparePlayerDelta(dealer, p4));  // bằng → hòa
    }

    [Fact]
    public void BothBust_Draw()
    {
        var dealer = new[] { C(13), C(12), C(5) }; // 25
        var player = new[] { C(13), C(11), C(6) }; // 26
        Assert.Equal(0, XiDachEngine.ComparePlayerDelta(dealer, player));
    }

    [Fact]
    public void DealerBust_PlayerValid_PlayerWins()
    {
        var dealer = new[] { C(13), C(12), C(5) }; // 25 quắc
        var player = new[] { C(9), C(9) };          // 18
        Assert.Equal(+2, XiDachEngine.ComparePlayerDelta(dealer, player));
    }

    [Fact]
    public void DealerBust_PlayerFiveCard_PlayerWinsDouble()
    {
        var dealer = new[] { C(13), C(12), C(5) };               // quắc
        var player = new[] { C(3), C(4), C(5), C(15), C(6) };    // ngũ linh 20
        Assert.Equal(+4, XiDachEngine.ComparePlayerDelta(dealer, player)); // ×2
    }

    [Fact]
    public void XiDach_Beats_Normal()
    {
        var dealer = new[] { C(10), C(9) };       // 19 thường
        var player = new[] { C(14), C(13) };      // xì dách
        Assert.Equal(+2, XiDachEngine.ComparePlayerDelta(dealer, player));
        // ngược lại nhà cái xì dách
        Assert.Equal(-2, XiDachEngine.ComparePlayerDelta(new[] { C(14), C(13) }, new[] { C(10), C(9) }));
    }

    [Fact]
    public void XiVang_Double_And_Beats_XiDach()
    {
        var dealerXiDach = new[] { C(14), C(13) };          // xì dách
        var playerXiVang = new[] { C(14), C(14, Suit.Hearts) }; // xì vàng
        Assert.Equal(+4, XiDachEngine.ComparePlayerDelta(dealerXiDach, playerXiVang)); // xì vàng > xì dách, ×2
    }

    [Fact]
    public void BothXiDach_PlayerWins()
    {
        var d = new[] { C(14), C(13) };
        var p = new[] { C(14, Suit.Hearts), C(10) };
        Assert.Equal(+2, XiDachEngine.ComparePlayerDelta(d, p)); // cả 2 xì dách → player thắng
    }

    [Fact]
    public void FiveCard_Beats_21_ButLoses_XiDach()
    {
        var five = new[] { C(3), C(4), C(5), C(15), C(6) };  // ngũ linh 20
        var p21 = new[] { C(14), C(13) };                    // xì dách (mạnh hơn ngũ linh)
        // nhà cái ngũ linh, player xì dách → player thắng (xì dách > ngũ linh), ×? mức theo player xì dách = ±2
        Assert.Equal(+2, XiDachEngine.ComparePlayerDelta(five, p21));
        // nhà cái 21 thường, player ngũ linh → player thắng ×2
        var d21 = new[] { C(14), C(11) }; // A+J = xì dách thật ra... dùng 10+10+1? Dùng tay thường 21:
        var dealer21 = new[] { C(7), C(7, Suit.Hearts), C(7, Suit.Diamonds) }; // 21 (3 lá, không đặc biệt)
        Assert.Equal(+4, XiDachEngine.ComparePlayerDelta(dealer21, five));
    }

    [Fact]
    public void FiveCard_vs_FiveCard_HigherTotalWins()
    {
        var dealer = new[] { C(3), C(4), C(5), C(15), C(6) };          // 20
        var player = new[] { C(3, Suit.Hearts), C(4, Suit.Hearts), C(5, Suit.Hearts), C(15, Suit.Hearts), C(5, Suit.Diamonds) }; // 3+4+5+2+5 = 19
        Assert.Equal(-4, XiDachEngine.ComparePlayerDelta(dealer, player)); // nhà cái 20 > player 19 → player thua ×2
    }

    // ---- Flow integration (drive MatchManager qua round xì dách) ----

    private static readonly MethodInfo DealXiDachMethod =
        typeof(MatchManager).GetMethod("DealXiDachRound", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void FullRound_DriveToEnd_ZeroSum_AllSettled()
    {
        // Tạo match qua MatchManager.Create (round 1 thường), rồi ép round xì dách bằng cách gọi
        // DealXiDachRound trực tiếp (reflection) — deal 2 lá random, P1 = nhà cái.
        var mgr = new MatchManager();
        var roomId = System.Guid.NewGuid();
        var ids = Enumerable.Range(0, 4).Select(_ => System.Guid.NewGuid()).ToArray();
        var players = ids.Select((id, i) => (id, $"P{i + 1}", i, false)).ToList();
        var match = mgr.Create(roomId, ids[0],
            players.Select(p => (p.id, p.Item2, p.Item3, p.Item4)).ToList());

        // Ép vào round xì dách: P1 nhà cái. (DealRound thật sẽ set IsXiDachRound; ở test set tay.)
        match.IsXiDachRound = true;
        DealXiDachMethod.Invoke(null, new object[] { match, ids[0] });

        // Drive: mỗi khi có người tới lượt, rút tới khi không buộc rút nữa rồi dừng (nếu được).
        int guard = 0;
        while (match.Status == MatchStatus.XiDachPlaying && guard++ < 100)
        {
            var uid = match.XiDachTurnUserId!.Value;
            var p = match.Players.First(x => x.UserId == uid);
            bool isDealer = p.IsXiDachDealer;
            if (XiDachEngine.CanStand(p.Hand, isDealer))
                mgr.StandXiDach(roomId, uid);
            else
                mgr.DrawXiDachCard(roomId, uid);
        }

        // Pha so điểm: nhà cái bấm So từng player chưa chốt.
        guard = 0;
        while (match.Status == MatchStatus.XiDachCompare && guard++ < 100)
        {
            var target = match.Players.First(x => !x.IsXiDachDealer && !x.XiDachSettled);
            mgr.CompareXiDachPlayer(roomId, ids[0], target.UserId);
        }

        // Kết thúc: mọi cặp đã chốt, tổng zero-sum.
        Assert.Equal(MatchStatus.WaitingNextRound, match.Status);
        Assert.All(match.Players.Where(p => !p.IsXiDachDealer), p => Assert.True(p.XiDachSettled));
        var scores = mgr.ComputeRoundScores(match);
        Assert.Equal(0, scores.Sum());
    }
}
