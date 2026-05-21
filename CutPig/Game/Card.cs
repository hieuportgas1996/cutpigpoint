namespace CutPig.GameEngine;

public enum Suit
{
    Spades = 0,
    Clubs = 1,
    Diamonds = 2,
    Hearts = 3,
}

public readonly record struct Card(int Rank, Suit Suit)
{
    // Rank: 3..15 (15 = "2" which is highest in TLMN)
    public string Code => $"{Rank}-{(int)Suit}";

    public static Card Parse(string code)
    {
        var parts = code.Split('-');
        return new Card(int.Parse(parts[0]), (Suit)int.Parse(parts[1]));
    }

    public int CompareValue() => Rank * 4 + (int)Suit;

    public override string ToString()
    {
        var label = Rank switch
        {
            11 => "J",
            12 => "Q",
            13 => "K",
            14 => "A",
            15 => "2",
            _ => Rank.ToString()
        };
        var glyph = Suit switch
        {
            Suit.Spades => "S",
            Suit.Clubs => "C",
            Suit.Diamonds => "D",
            Suit.Hearts => "H",
            _ => "?"
        };
        return $"{label}{glyph}";
    }
}

public static class Deck
{
    public static List<Card> Build()
    {
        var cards = new List<Card>(52);
        for (int r = 3; r <= 15; r++)
            for (int s = 0; s < 4; s++)
                cards.Add(new Card(r, (Suit)s));
        return cards;
    }

    public static List<Card> Shuffle(List<Card> cards, Random rng)
    {
        var arr = cards.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr.ToList();
    }
}
