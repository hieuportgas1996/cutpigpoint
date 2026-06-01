namespace CutPig.GameEngine;

/// <summary>
/// Engine cho biến thể "Cào Rùa" (3 lá / người) dùng trong round "Lễ hội".
/// Điểm 3 lá = tổng giá trị (A=1, 2..9 = mặt, riêng "2"(rank15)=2, 10/J/Q/K=10) lấy hàng đơn vị (mod 10).
/// Thứ hạng tăng dần: điểm 0 &lt; 1 &lt; ... &lt; 9 &lt; (1) toàn J/Q/K (không phải bộ ba) &lt; (2) ba lá giống nhau.
/// Ba lá giống nhau xếp: 2 &lt; 3 &lt; ... &lt; 9 &lt; 10 &lt; J &lt; Q &lt; K &lt; A.
/// </summary>
public static class CaoRuaEngine
{
    /// <summary>Giá trị 1 lá khi tính điểm Cào: A=1, "2"=2, 3..9 = mặt, 10/J/Q/K = 10.</summary>
    public static int CardPoint(Card c) => c.Rank switch
    {
        14 => 1,                       // A
        15 => 2,                       // "2" (rank 15 trong encoding TLMN)
        10 or 11 or 12 or 13 => 10,    // 10, J, Q, K
        _ => c.Rank,                   // 3..9
    };

    /// <summary>Điểm số (hàng đơn vị) của bộ 3 lá — 0..9.</summary>
    public static int Score(IReadOnlyList<Card> cards) => cards.Sum(CardPoint) % 10;

    /// <summary>Thứ tự "tự nhiên" của rank cho bộ ba giống nhau: 2 thấp nhất, A cao nhất.</summary>
    private static int NaturalOrder(int rank) => rank switch
    {
        15 => 2,    // "2" → thấp nhất
        14 => 14,   // A → cao nhất
        _ => rank,  // 3..13 giữ nguyên (10=10, J=11, Q=12, K=13)
    };

    private static bool IsFaceJQK(Card c) => c.Rank is 11 or 12 or 13;

    /// <summary>
    /// Tính "sức mạnh" bộ 3 lá để so hạng. Trả về (tier, tiebreak):
    ///  - tier 0: điểm thường (tiebreak = điểm 0..9)
    ///  - tier 1: toàn J/Q/K nhưng KHÔNG phải bộ ba (tiebreak = 0, mọi tổ hợp ngang nhau)
    ///  - tier 2: bộ ba giống nhau (tiebreak = NaturalOrder của rank)
    /// So sánh: tier trước, rồi tiebreak. Cao hơn = mạnh hơn.
    /// </summary>
    public static (int Tier, int Tiebreak) Strength(IReadOnlyList<Card> cards)
    {
        bool isTriple = cards.Count == 3 && cards[0].Rank == cards[1].Rank && cards[1].Rank == cards[2].Rank;
        if (isTriple)
            return (2, NaturalOrder(cards[0].Rank));

        bool allJQK = cards.Count == 3 && cards.All(IsFaceJQK);
        if (allJQK)
            return (1, 0);

        return (0, Score(cards));
    }

    /// <summary>Nhãn hiển thị bộ bài cho lịch sử (vd "9 điểm", "Bộ ba A", "J/Q/K").</summary>
    public static string Label(IReadOnlyList<Card> cards)
    {
        var (tier, tiebreak) = Strength(cards);
        return tier switch
        {
            2 => $"Bộ ba {RankLabel(cards[0].Rank)}",
            1 => "Tổ hợp J/Q/K",
            _ => $"{tiebreak} điểm",
        };
    }

    private static string RankLabel(int rank) => rank switch
    {
        11 => "J", 12 => "Q", 13 => "K", 14 => "A", 15 => "2",
        _ => rank.ToString(),
    };
}
