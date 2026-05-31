using System.Reflection;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho phase chọn về trắng (WhiteWinChoice). Bug đã gặp: khi tất cả candidate
/// từ chối về trắng, status kẹt ở WhiteWinChoice (không chuyển InProgress) → game treo.
/// </summary>
public class WhiteWinChoiceTests
{
    private static readonly MethodInfo ResolveMethod =
        typeof(MatchManager).GetMethod("TryResolveWhiteWinChoice",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Resolve(Match match) => ResolveMethod.Invoke(null, new object[] { match });

    private static Match MakeChoiceMatch(int n)
    {
        var match = new Match { RoomId = Guid.NewGuid(), HostUserId = Guid.NewGuid() };
        for (int i = 0; i < n; i++)
            match.Players.Add(new MatchPlayer { UserId = Guid.NewGuid(), SeatIndex = i, DisplayName = $"P{i + 1}" });
        match.Status = MatchStatus.WhiteWinChoice;
        match.WhiteWinDeadline = DateTime.UtcNow.AddSeconds(20);
        return match;
    }

    [Fact]
    public void AllDecline_ResumesInProgress()
    {
        // P2 có 5 đôi thông, chọn ĐÁNH TIẾP (decline) → ván phải tiếp tục, không treo.
        var m = MakeChoiceMatch(4);
        m.PreviousRoundWinnerId = m.Players[0].UserId; // P1 thắng round trước → đi đầu
        m.Players[1].WhiteWinReason = "5 đôi thông";
        m.Players[1].WhiteWinAccepted = false; // chọn đánh tiếp

        Resolve(m);

        Assert.Equal(MatchStatus.InProgress, m.Status);          // ❗ bug cũ: kẹt WhiteWinChoice
        Assert.Null(m.WhiteWinDeadline);
        Assert.All(m.Players, p => Assert.Null(p.WhiteWinReason));
        // Đi đầu = người thắng round trước (P1, seat 0)
        Assert.Equal(0, m.CurrentTurnSeatIndex);
    }

    [Fact]
    public void Accept_EndsRound_WaitingNextRound()
    {
        var m = MakeChoiceMatch(4);
        m.Players[1].WhiteWinReason = "5 đôi thông";
        m.Players[1].WhiteWinAccepted = true; // về trắng

        Resolve(m);

        Assert.Equal(MatchStatus.WaitingNextRound, m.Status);
        Assert.Equal(1, m.Players[1].FinalRank);     // người về trắng = Nhất
        Assert.True(m.NextRoundOpensWithThreeSpades); // round sau áp luật 3♠
    }

    [Fact]
    public void NotEveryoneChosen_NoOp()
    {
        // Còn người chưa chọn → chưa resolve, giữ nguyên WhiteWinChoice.
        var m = MakeChoiceMatch(4);
        m.Players[1].WhiteWinReason = "5 đôi thông";
        m.Players[2].WhiteWinReason = "6 đôi";
        m.Players[1].WhiteWinAccepted = false;
        // P3 chưa chọn (null)

        Resolve(m);

        Assert.Equal(MatchStatus.WhiteWinChoice, m.Status);
    }

    [Fact]
    public void MultiCandidate_OneAccepts_OneDeclines_EndsRound()
    {
        // 1 nhận + 1 từ chối → vẫn kết thúc ván (có người về trắng).
        var m = MakeChoiceMatch(4);
        m.Players[1].WhiteWinReason = "5 đôi thông";
        m.Players[2].WhiteWinReason = "6 đôi";
        m.Players[1].WhiteWinAccepted = true;
        m.Players[2].WhiteWinAccepted = false; // người này thành loser thường

        Resolve(m);

        Assert.Equal(MatchStatus.WaitingNextRound, m.Status);
        Assert.Equal(1, m.Players[1].FinalRank);   // người về trắng = Nhất
        Assert.Null(m.Players[2].WhiteWinReason);  // người từ chối bị clear reason
    }
}
