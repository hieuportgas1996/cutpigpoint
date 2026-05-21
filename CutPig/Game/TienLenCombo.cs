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
    /// Basic rules only — no chặt heo / cut 2 yet (Phase 4).
    /// </summary>
    public static bool Beats(Combo current, Combo next)
    {
        // Same kind, same length, higher TopValue
        if (next.Kind == current.Kind && next.Length == current.Length && next.TopValue > current.TopValue)
            return true;
        return false;
    }
}
