namespace CutPig.GameEngine;

/// <summary>
/// Engine cho biến thể "Xì Dách" (Blackjack VN) dùng trong round "Sát Phạt".
/// Điểm lá: 2..10 = mặt; J/Q/K = 10; A = 10 nếu tay 2-3 lá / 1 nếu tay 4-5 lá; "2"(rank15) = 2.
/// Bài đặc biệt (yếu→mạnh): Quắc(>21) &lt; điểm thường 16..21 &lt; Ngũ Linh (5 lá ≤21) &lt; Xì Dách (A+10điểm, 2 lá) &lt; Xì Vàng (A+A, 2 lá).
/// </summary>
public static class XiDachEngine
{
    public const int BlackjackTarget = 21;
    public const int PlayerStandMin = 16;   // player được dừng khi tổng ≥ 16
    public const int DealerStandMin = 15;   // nhà cái được dừng khi tổng ≥ 15
    public const int MaxCards = 5;           // tối đa 5 lá

    /// <summary>Loại tay bài (để so sức mạnh). Số lớn = mạnh hơn.</summary>
    public enum HandKind
    {
        Bust = 0,        // Quắc (> 21)
        Normal = 1,      // điểm thường (≤ 21, không đặc biệt)
        FiveCard = 2,    // Ngũ Linh (5 lá, tổng ≤ 21)
        XiDach = 3,      // A + (10/J/Q/K) ở đúng 2 lá
        XiVang = 4,      // A + A ở đúng 2 lá
    }

    /// <summary>Giá trị 1 lá theo số lá hiện có trong tay (cho A linh hoạt).</summary>
    public static int CardPoint(Card c, int handCount) => c.Rank switch
    {
        14 => (handCount <= 3 ? 10 : 1),   // A: 10 nếu tay 2-3 lá, 1 nếu 4-5 lá
        15 => 2,                            // "2" (rank 15 trong encoding TLMN)
        11 or 12 or 13 => 10,               // J, Q, K
        _ => c.Rank,                        // 2..10 (rank 3..10) — face value
    };

    /// <summary>Tổng điểm của tay (A tính theo số lá hiện tại).</summary>
    public static int Total(IReadOnlyList<Card> hand) => hand.Sum(c => CardPoint(c, hand.Count));

    /// <summary>True nếu là Xì Dách: đúng 2 lá, 1 lá A + 1 lá điểm 10 (10/J/Q/K).</summary>
    public static bool IsXiDach(IReadOnlyList<Card> hand)
    {
        if (hand.Count != 2) return false;
        bool hasAce = hand.Any(c => c.Rank == 14);
        bool hasTen = hand.Any(c => c.Rank is 10 or 11 or 12 or 13);
        return hasAce && hasTen;
    }

    /// <summary>True nếu là Xì Vàng: đúng 2 lá, cả 2 đều A.</summary>
    public static bool IsXiVang(IReadOnlyList<Card> hand)
        => hand.Count == 2 && hand.All(c => c.Rank == 14);

    /// <summary>True nếu Ngũ Linh: 5 lá và tổng ≤ 21 (không quắc).</summary>
    public static bool IsFiveCard(IReadOnlyList<Card> hand)
        => hand.Count == 5 && Total(hand) <= BlackjackTarget;

    public static bool IsBust(IReadOnlyList<Card> hand) => Total(hand) > BlackjackTarget;

    /// <summary>Phân loại tay bài. Ưu tiên: Xì Vàng > Xì Dách > (quắc?) > Ngũ Linh > thường.</summary>
    public static HandKind Classify(IReadOnlyList<Card> hand)
    {
        if (IsXiVang(hand)) return HandKind.XiVang;
        if (IsXiDach(hand)) return HandKind.XiDach;
        if (IsBust(hand)) return HandKind.Bust;
        if (IsFiveCard(hand)) return HandKind.FiveCard;
        return HandKind.Normal;
    }

    /// <summary>True nếu tay BUỘC phải rút thêm (chưa đạt ngưỡng dừng). isDealer phân biệt ngưỡng.</summary>
    public static bool MustDraw(IReadOnlyList<Card> hand, bool isDealer)
    {
        if (hand.Count >= MaxCards) return false;          // đủ 5 lá, không rút nữa
        if (Classify(hand) is HandKind.XiDach or HandKind.XiVang) return false; // đặc biệt 2 lá, dừng luôn
        int total = Total(hand);
        if (total > BlackjackTarget) return false;          // đã quắc, không rút thêm (kết thúc)
        int min = isDealer ? DealerStandMin : PlayerStandMin;
        return total < min;
    }

    /// <summary>True nếu tay ĐƯỢC PHÉP dừng (không bị buộc rút và chưa quắc/chưa full).</summary>
    public static bool CanStand(IReadOnlyList<Card> hand, bool isDealer)
    {
        if (Classify(hand) is HandKind.XiDach or HandKind.XiVang) return true;
        if (IsBust(hand)) return false;     // quắc thì không "dừng" — đã chốt thua
        return !MustDraw(hand, isDealer);
    }

    /// <summary>
    /// So 1 cặp Nhà Cái vs Player. Trả về điểm PLAYER nhận (zero-sum: nhà cái nhận giá trị âm lại).
    /// Quy tắc:
    ///  - Cả 2 quắc → 0 (hòa). Nhà cái quắc, player không → player thắng. Player quắc, nhà cái không → player thua.
    ///  - Xì Vàng / Xì Dách: theo bậc; cả 2 cùng bậc đặc biệt → PLAYER thắng. Ngũ Linh vs Ngũ Linh → tổng cao thắng.
    ///  - Điểm thường: tổng cao thắng; bằng điểm → hòa (0).
    ///  - Mức điểm: thắng/thua thường ±2; nếu BÊN THẮNG là Ngũ Linh hoặc Xì Vàng → ×2 (±4). Xì Dách thường = ±2.
    /// </summary>
    public static int ComparePlayerDelta(IReadOnlyList<Card> dealer, IReadOnlyList<Card> player)
    {
        var dk = Classify(dealer);
        var pk = Classify(player);

        // Cả 2 quắc → hòa.
        if (dk == HandKind.Bust && pk == HandKind.Bust) return 0;
        // Một bên quắc.
        if (pk == HandKind.Bust) return -Multiplier(dk) * 2;   // player quắc → thua; mức theo tay nhà cái
        if (dk == HandKind.Bust) return +Multiplier(pk) * 2;   // nhà cái quắc → player thắng; mức theo tay player

        // So theo bậc HandKind.
        if (pk != dk)
        {
            // Bên có bậc cao hơn thắng. Mức ×2 nếu bên thắng là Ngũ Linh / Xì Vàng.
            if (pk > dk) return +Multiplier(pk) * 2;
            return -Multiplier(dk) * 2;
        }

        // Cùng bậc:
        switch (pk)
        {
            case HandKind.XiVang:
            case HandKind.XiDach:
                // Cả 2 cùng xì dách / xì vàng → PLAYER thắng (mức theo bậc đó).
                return +Multiplier(pk) * 2;
            case HandKind.FiveCard:
            case HandKind.Normal:
            {
                int dt = Total(dealer), pt = Total(player);
                if (pt > dt) return +Multiplier(pk) * 2;
                if (pt < dt) return -Multiplier(dk) * 2;
                return 0; // bằng điểm → hòa
            }
            default:
                return 0;
        }
    }

    /// <summary>Hệ số nhân điểm theo tay: Ngũ Linh / Xì Vàng = 2 (×2 → ±4); còn lại = 1 (±2).</summary>
    private static int Multiplier(HandKind k) => (k is HandKind.FiveCard or HandKind.XiVang) ? 2 : 1;

    /// <summary>Nhãn tiếng Việt cho tay bài (hiển thị round-end).</summary>
    public static string Label(IReadOnlyList<Card> hand)
    {
        return Classify(hand) switch
        {
            HandKind.XiVang => "Xì Vàng (A+A)",
            HandKind.XiDach => "Xì Dách",
            HandKind.FiveCard => $"Ngũ Linh ({Total(hand)})",
            HandKind.Bust => $"Quắc ({Total(hand)})",
            _ => $"{Total(hand)} điểm",
        };
    }
}
