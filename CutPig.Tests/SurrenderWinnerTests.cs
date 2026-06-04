using System;
using System.Linq;
using CutPig.GameEngine;
using CutPig.Services;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Bug fix: khi MỌI người khác đầu hàng (chưa đánh gì), người còn lại về Nhất nhưng
/// PreviousRoundWinnerId KHÔNG được set → ván sau người khác (giá trị stale) đi đầu sai.
/// Sau fix: người còn lại (Nhất) phải được set làm PreviousRoundWinnerId.
/// </summary>
public class SurrenderWinnerTests
{
    private readonly MatchManager _mgr = new();

    private (Guid roomId, Guid[] ids) CreateMatch(int n)
    {
        var roomId = Guid.NewGuid();
        var ids = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();
        var players = ids.Select((id, i) => (id, $"P{i + 1}", i, false)).ToList();
        _mgr.Create(roomId, ids[0], players);
        return (roomId, ids);
    }

    [Fact]
    public void AllOthersSurrender_SurvivorBecomesPreviousRoundWinner()
    {
        var (roomId, ids) = CreateMatch(4);

        // 3 người (trừ ids[1]) đầu hàng → ids[1] còn lại về Nhất.
        _mgr.Surrender(roomId, ids[0]);
        _mgr.Surrender(roomId, ids[2]);
        var res = _mgr.Surrender(roomId, ids[3]);
        var match = res.Match;

        var survivor = match.Players.First(p => p.UserId == ids[1]);
        Assert.Equal(1, survivor.FinalRank);                 // còn lại về Nhất
        Assert.Equal(ids[1], match.PreviousRoundWinnerId);   // FIX: winner ván = người còn lại
    }

    [Fact]
    public void AllOthersSurrender_SurvivorIsFirstNextRound()
    {
        var (roomId, ids) = CreateMatch(4);
        _mgr.Surrender(roomId, ids[0]);
        _mgr.Surrender(roomId, ids[2]);
        _mgr.Surrender(roomId, ids[3]);

        // Deal ván kế (không phải round 1 → không ép 3♠) → người còn lại đi đầu.
        var match = _mgr.StartNextRound(roomId, ids[0]);
        var firstSeatUserId = match.Players[match.CurrentTurnSeatIndex].UserId;
        // Ván kế không ép 3♠ (không phải round 1 / không white-win) → đi đầu theo PreviousRoundWinnerId.
        if (!match.EnforceThreeSpadesOpening)
            Assert.Equal(ids[1], firstSeatUserId);
    }
}
