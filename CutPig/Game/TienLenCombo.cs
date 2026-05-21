namespace CutPig.GameEngine;

public enum ComboKind
{
    Single = 1,
    Pair = 2,
    Triple = 3,
    Four = 4,
    Run = 5,        // 3+ consecutive ranks
    RunOfPairs = 6, // 3+ consecutive pairs (3 đôi thông, 4 đôi thông)
}

public record Combo(ComboKind Kind, IReadOnlyList<Card> Cards, int TopValue)
{
    // TopValue: highest CompareValue() in the combo (used for ranking)
    public int Length => Cards.Count;
}

public static class TienLenComboEngine
{
    public static Combo? Detect(IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0) return null;
        var sorted = cards.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();

        if (sorted.Count == 1)
            return new Combo(ComboKind.Single, sorted, sorted[0].CompareValue());

        // All same rank?
        if (sorted.All(c => c.Rank == sorted[0].Rank))
        {
            return sorted.Count switch
            {
                2 => new Combo(ComboKind.Pair, sorted, sorted[^1].CompareValue()),
                3 => new Combo(ComboKind.Triple, sorted, sorted[^1].CompareValue()),
                4 => new Combo(ComboKind.Four, sorted, sorted[^1].CompareValue()),
                _ => null
            };
        }

        // Run (consecutive ranks, each appearing once). Length >= 3. Cannot contain "2" (rank 15).
        if (IsRun(sorted))
            return new Combo(ComboKind.Run, sorted, sorted[^1].CompareValue());

        // Run of pairs (consecutive pairs, length >= 3 pairs). Cannot contain "2".
        if (IsRunOfPairs(sorted))
            return new Combo(ComboKind.RunOfPairs, sorted, sorted[^1].CompareValue());

        return null;
    }

    private static bool IsRun(IReadOnlyList<Card> sorted)
    {
        if (sorted.Count < 3) return false;
        if (sorted.Any(c => c.Rank == 15)) return false; // "2" cannot be in a run
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Rank != sorted[i - 1].Rank + 1) return false;
        }
        return true;
    }

    private static bool IsRunOfPairs(IReadOnlyList<Card> sorted)
    {
        if (sorted.Count < 6 || sorted.Count % 2 != 0) return false;
        if (sorted.Any(c => c.Rank == 15)) return false;
        var groups = sorted.GroupBy(c => c.Rank).OrderBy(g => g.Key).ToList();
        if (groups.Count * 2 != sorted.Count) return false;
        if (!groups.All(g => g.Count() == 2)) return false;
        for (int i = 1; i < groups.Count; i++)
        {
            if (groups[i].Key != groups[i - 1].Key + 1) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if "next" beats "current" according to TLMN rules.
    /// </summary>
    public static bool Beats(Combo current, Combo next)
    {
        // 4-pair-run beats everything (including 2s) — no restriction.
        if (next.Kind == ComboKind.RunOfPairs && next.Cards.Count == 8)
            return true;

        // Tứ quý beats: con 2, đôi 2, 3 đôi thông
        if (next.Kind == ComboKind.Four)
        {
            if (current.Kind == ComboKind.Single && current.Cards[0].Rank == 15) return true;
            if (current.Kind == ComboKind.Pair && current.Cards[0].Rank == 15) return true;
            if (current.Kind == ComboKind.RunOfPairs && current.Cards.Count == 6) return true;
        }

        // 3 đôi thông beats: 1 con 2
        if (next.Kind == ComboKind.RunOfPairs && next.Cards.Count == 6)
        {
            if (current.Kind == ComboKind.Single && current.Cards[0].Rank == 15) return true;
        }

        // Same kind + same length + higher top value
        if (next.Kind == current.Kind && next.Length == current.Length && next.TopValue > current.TopValue)
            return true;

        return false;
    }

    /// <summary>True if this combo is a "4-pair-run" (4 đôi thông) — exempt from pass-tracking.</summary>
    public static bool IsFourPairRun(Combo c) => c.Kind == ComboKind.RunOfPairs && c.Cards.Count == 8;

    /// <summary>
    /// Detect ve-trang (white-win) hands. Returns reason string or null if not white-win.
    /// </summary>
    public static string? DetectWhiteWin(IReadOnlyList<Card> hand)
    {
        if (hand.Count < 10) return null;
        var sorted = hand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();

        // 1. Sảnh 3..A (12 lá, mỗi rank 3..14 xuất hiện đúng 1 lần)
        if (sorted.Count >= 12)
        {
            var rank3toA = sorted.GroupBy(c => c.Rank).Where(g => g.Key >= 3 && g.Key <= 14).ToList();
            if (rank3toA.Count == 12 && rank3toA.All(g => g.Any()))
                return "Sảnh 3 đến A";
        }

        // 2. Tứ quý 2
        var twos = sorted.Where(c => c.Rank == 15).ToList();
        if (twos.Count == 4)
            return "Tứ quý 2";

        // 3. 6 đôi (12 lá, 6 ranks mỗi rank 2 lá)
        var groups = sorted.GroupBy(c => c.Rank).ToList();
        var pairCount = groups.Count(g => g.Count() >= 2);
        if (pairCount >= 6)
            return "6 đôi";

        // 4. 5 đôi thông (10 lá liên tiếp rank, mỗi rank 2 lá, không chứa 2)
        if (HasFivePairRun(groups))
            return "5 đôi thông";

        return null;
    }

    private static bool HasFivePairRun(List<IGrouping<int, Card>> groups)
    {
        var pairs = groups
            .Where(g => g.Count() >= 2 && g.Key != 15)
            .Select(g => g.Key)
            .OrderBy(r => r)
            .ToList();
        if (pairs.Count < 5) return false;
        for (int i = 0; i <= pairs.Count - 5; i++)
        {
            bool ok = true;
            for (int j = 1; j < 5; j++)
            {
                if (pairs[i + j] != pairs[i + j - 1] + 1) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }
}
