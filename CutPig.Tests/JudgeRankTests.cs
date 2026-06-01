using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho gán hạng khi phán xử (Judge). Trọng tâm: Case C (≥2 pardoned) — victim bị ghim ở
/// hạng chót và KHÔNG được tính vào FinishedCount, nếu không pardoned về sau sẽ bị đẩy hạng sai
/// (regression: pardoned về Nhì lại bị tính thành Ba).
/// </summary>
public class JudgeRankTests
{
    private static readonly MethodInfo JudgeMethod =
        typeof(MatchManager).GetMethod("CheckAndApplyJudge",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool CheckJudge(Match m, System.Guid winnerId) =>
        (bool)JudgeMethod.Invoke(null, new object[] { m, winnerId })!;

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

    // Một lá bất kỳ để "còn bài" (rank 5♥, không phải heo).
    private static Card SomeCard => new(5, Suit.Hearts);

    [Fact]
    public void CaseC_VictimDoesNotConsumeRankSlot()
    {
        // 4 người: P1 Nhất (vừa hết bài), P2 + P3 đã ra bài (pardoned, còn bài),
        // P4 chưa ra bài (victim). Case C: victim ghim hạng 4, FinishedCount giữ nguyên = 1,
        // pardoned sẽ về Nhì (2) / Ba (3) ở các nước sau.
        var (m, ids) = MakeMatch(4);

        // P1 đã về Nhất trước khi gọi judge (giống flow thật).
        m.FinishedCount = 1;
        m.Players[0].FinalRank = 1;
        m.FinishOrder.Add(ids[0]);

        // Pardoned: đã ra bài + còn bài trong tay.
        m.Players[1].HasPlayedThisRound = true;
        m.Players[1].Hand.Add(SomeCard);
        m.Players[2].HasPlayedThisRound = true;
        m.Players[2].Hand.Add(SomeCard);

        // Victim: chưa ra bài, còn bài.
        m.Players[3].HasPlayedThisRound = false;
        m.Players[3].Hand.Add(SomeCard);

        bool roundEnded = CheckJudge(m, ids[0]);

        Assert.False(roundEnded); // Case C → ván tiếp tục cho pardoned chơi
        Assert.True(m.JudgeTriggered);

        // Victim ghim hạng chót.
        Assert.Equal(4, m.Players[3].FinalRank);
        Assert.True(m.Players[3].JudgeIsVictim);

        // Pardoned chưa có hạng, sẽ về sau.
        Assert.Null(m.Players[1].FinalRank);
        Assert.Null(m.Players[2].FinalRank);
        Assert.True(m.Players[1].JudgeIsPardoned);
        Assert.True(m.Players[2].JudgeIsPardoned);

        // Mấu chốt: FinishedCount KHÔNG bị victim cộng → vẫn = 1.
        // Pardoned tiếp theo hết bài sẽ là FinishedCount=2 (Nhì), người còn lại =3 (Ba).
        Assert.Equal(1, m.FinishedCount);
    }

    [Fact]
    public void CaseB_PardonedGetsSecond()
    {
        // 4 người: P1 Nhất, P2 pardoned (đã ra bài), P3 + P4 victim. Case B (1 pardoned):
        // kết thúc ngay → pardoned Nhì (2), victim chia hạng chót (3).
        var (m, ids) = MakeMatch(4);
        m.FinishedCount = 1;
        m.Players[0].FinalRank = 1;
        m.FinishOrder.Add(ids[0]);

        m.Players[1].HasPlayedThisRound = true;
        m.Players[1].Hand.Add(SomeCard);
        m.Players[2].HasPlayedThisRound = false;
        m.Players[2].Hand.Add(SomeCard);
        m.Players[3].HasPlayedThisRound = false;
        m.Players[3].Hand.Add(SomeCard);

        bool roundEnded = CheckJudge(m, ids[0]);

        Assert.True(roundEnded); // Case B → kết thúc ngay
        Assert.Equal(2, m.Players[1].FinalRank); // pardoned Nhì
        Assert.Equal(3, m.Players[2].FinalRank); // victim
        Assert.Equal(3, m.Players[3].FinalRank); // victim (tied)
    }
}
