using System.Collections.Generic;
using CutPig.GameEngine;
using Xunit;

namespace CutPig.Tests;

/// <summary>Tests cho engine Cào Rùa (round lễ hội).</summary>
public class CaoRuaTests
{
    // rank: A=14, "2"=15, 3..13 như TLMN (10,J=11,Q=12,K=13).
    private static Card C(int rank) => new(rank, Suit.Spades);
    private static Card C(int rank, Suit s) => new(rank, s);

    [Theory]
    [InlineData(new[] { 14, 4, 5 }, 0)]   // 1+4+5=10 → 0
    [InlineData(new[] { 14, 4, 3 }, 8)]   // 1+4+3=8 → 8
    [InlineData(new[] { 14, 4, 10 }, 5)]  // 1+4+10=15 → 5
    [InlineData(new[] { 14, 4, 13 }, 5)]  // 1+4+K(10)=15 → 5
    [InlineData(new[] { 14, 11, 13 }, 1)] // 1+J(10)+K(10)=21 → 1
    public void Score_MatchesSpecExamples(int[] ranks, int expected)
    {
        var cards = new List<Card>();
        foreach (var r in ranks) cards.Add(C(r));
        Assert.Equal(expected, CaoRuaEngine.Score(cards));
    }

    [Fact]
    public void Two_CountsAsPoint2()
    {
        // "2" (rank 15) = 2 điểm. 2+2+3 = 7.
        Assert.Equal(7, CaoRuaEngine.Score(new List<Card> { C(15), C(15, Suit.Hearts), C(3) }));
    }

    [Fact]
    public void Strength_Ordering_PointsBelowJQKBelowTriple()
    {
        // 9 điểm
        var nine = new List<Card> { C(3), C(3, Suit.Hearts), C(3, Suit.Clubs) };
        // Đây là bộ ba 3 → tier 2, không phải 9 điểm. Dùng bộ khác cho 9 điểm:
        var realNine = new List<Card> { C(4), C(5, Suit.Hearts), C(14) }; // 4+5+1=10→0... chọn lại
        realNine = new List<Card> { C(3), C(5, Suit.Hearts), C(14) };     // 3+5+1=9
        Assert.Equal(9, CaoRuaEngine.Score(realNine));

        var jqk = new List<Card> { C(11), C(12, Suit.Hearts), C(13) };   // J Q K → tier 1
        var tripleA = new List<Card> { C(14), C(14, Suit.Hearts), C(14, Suit.Clubs) }; // AAA → tier 2

        var sNine = CaoRuaEngine.Strength(realNine);
        var sJqk = CaoRuaEngine.Strength(jqk);
        var sTriple = CaoRuaEngine.Strength(tripleA);

        Assert.True(Cmp(sJqk) > Cmp(sNine));   // J/Q/K > 9 điểm
        Assert.True(Cmp(sTriple) > Cmp(sJqk)); // bộ ba > J/Q/K
        // dùng `nine` để khỏi cảnh báo biến chưa dùng
        Assert.Equal((2, 3), CaoRuaEngine.Strength(nine));
    }

    [Fact]
    public void Triple_Ordering_TwoLowestAceHighest()
    {
        var tripleTwo = new List<Card> { C(15), C(15, Suit.Hearts), C(15, Suit.Clubs) };   // 222 → thấp nhất
        var tripleThree = new List<Card> { C(3), C(3, Suit.Hearts), C(3, Suit.Clubs) };     // 333
        var tripleTen = new List<Card> { C(10), C(10, Suit.Hearts), C(10, Suit.Clubs) };    // 10 10 10
        var tripleAce = new List<Card> { C(14), C(14, Suit.Hearts), C(14, Suit.Clubs) };    // AAA → cao nhất

        Assert.True(Cmp(CaoRuaEngine.Strength(tripleThree)) > Cmp(CaoRuaEngine.Strength(tripleTwo)));
        Assert.True(Cmp(CaoRuaEngine.Strength(tripleTen)) > Cmp(CaoRuaEngine.Strength(tripleThree)));
        Assert.True(Cmp(CaoRuaEngine.Strength(tripleAce)) > Cmp(CaoRuaEngine.Strength(tripleTen)));
    }

    [Fact]
    public void JQK_AllCombosEqual()
    {
        var jqk = CaoRuaEngine.Strength(new List<Card> { C(11), C(12), C(13) });
        var jjq = CaoRuaEngine.Strength(new List<Card> { C(11), C(11, Suit.Hearts), C(12) });
        var qkk = CaoRuaEngine.Strength(new List<Card> { C(12), C(13), C(13, Suit.Hearts) });
        Assert.Equal(jqk, jjq);
        Assert.Equal(jqk, qkk);
        Assert.Equal((1, 0), jqk);
    }

    private static long Cmp((int Tier, int Tiebreak) s) => (long)s.Tier * 1000 + s.Tiebreak;
}
