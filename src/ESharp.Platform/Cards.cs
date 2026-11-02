using System;

namespace ESharp.Platform;

/// <summary>
/// A small deterministic random number generator (xorshift32), so shuffles repeat from a seed on the host and on the
/// Amiga alike.
/// </summary>
public struct Rng
{
    public uint State;

    public static Rng FromSeed(uint seed)
    {
        Rng r = new Rng();
        r.State = seed == 0 ? 0x9E3779B9u : seed;
        return r;
    }

    public uint Next()
    {
        uint x = State;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        State = x;
        return x;
    }

    /// <summary>A value in 0..limit-1.</summary>
    public int Below(int limit)
    {
        return (int)(Next() % (uint)limit);
    }

    /// <summary>Fisher-Yates shuffle.</summary>
    public void Shuffle(Span<byte> cards)
    {
        for (int i = cards.Length - 1; i > 0; i--)
        {
            int j = Below(i + 1);
            byte t = cards[i];
            cards[i] = cards[j];
            cards[j] = t;
        }
    }
}

/// <summary>
/// Cards are bytes: <c>suit * 13 + rank</c>, rank 0 = ace .. 12 = king. A deck can have any number of suits (4 for a
/// standard deck, 8 for two decks), so the suit count is a parameter.
/// </summary>
public static class Deck
{
    public const int Ranks = 13;

    public static int Rank(int card) { return card % Ranks; }

    public static int Suit(int card) { return card / Ranks; }

    /// <summary>Fills <paramref name="cards"/> with <c>suits * 13</c> cards in order. Returns the number written, or 0 if the span is too small.</summary>
    public static int Fill(Span<byte> cards, int suits)
    {
        int n = suits * Ranks;
        if (n > cards.Length) return 0;
        for (int i = 0; i < n; i++) cards[i] = (byte)i;
        return n;
    }

    /// <summary>Sorts by suit, then rank (insertion sort: hands are small).</summary>
    public static void Sort(Span<byte> cards)
    {
        for (int i = 1; i < cards.Length; i++)
        {
            byte c = cards[i];
            int j = i - 1;
            while (j >= 0 && cards[j] > c)
            {
                cards[j + 1] = cards[j];
                j--;
            }
            cards[j + 1] = c;
        }
    }
}

/// <summary>
/// Rummy hand analysis. A meld is a set (3 or more cards of one rank in different suits) or a run (3 or more
/// consecutive ranks of one suit, ace low). <see cref="Deadwood"/> is the lowest total point value of the cards
/// left over when the hand is split into melds in the best possible way (ace 1, 2..10 face value, J/Q/K 10).
/// Looks at up to <see cref="MaxHand"/> cards; the work grows with 3 to the power of the hand size, so keep hands small.
/// </summary>
public static class Rummy
{
    public const int MaxHand = 10;

    private static byte[] _rank = new byte[MaxHand];
    private static byte[] _suit = new byte[MaxHand];
    private static byte[] _points = new byte[MaxHand];
    private static byte[] _isMeld = new byte[1 << MaxHand];
    private static int[] _best = new int[1 << MaxHand];
    private static int[] _choice = new int[1 << MaxHand];

    public static int Points(int card)
    {
        int rank = Deck.Rank(card);
        return rank >= 9 ? 10 : rank + 1;
    }

    /// <summary>True when the cards form one meld (a set or a run).</summary>
    public static bool IsMeld(ReadOnlySpan<byte> cards)
    {
        int n = cards.Length;
        if (n < 3 || n > MaxHand) return false;
        for (int i = 0; i < n; i++) { _rank[i] = (byte)Deck.Rank(cards[i]); _suit[i] = (byte)Deck.Suit(cards[i]); }
        return MeldMask((1 << n) - 1);
    }

    /// <summary>The lowest deadwood the hand can reach.</summary>
    public static int Deadwood(ReadOnlySpan<byte> hand)
    {
        return Analyze(hand);
    }

    /// <summary>
    /// Like <see cref="Deadwood"/>, and also tells which cards are melded: <paramref name="group"/>[i] is 0 for a
    /// deadwood card or 1, 2, ... for the meld the card belongs to (the span must be at least as long as the hand).
    /// </summary>
    public static int Melds(ReadOnlySpan<byte> hand, Span<byte> group)
    {
        int deadwood = Analyze(hand);
        int n = hand.Length < MaxHand ? hand.Length : MaxHand;
        for (int i = 0; i < group.Length; i++) group[i] = 0;
        int rest = (1 << n) - 1;
        int number = 0;
        while (rest != 0)
        {
            int m = _choice[rest];
            if (m == 0)
            {
                rest &= rest - 1;   // drops the lowest bit: the lowest card stays deadwood
                continue;
            }
            number++;
            for (int i = 0; i < n; i++)
                if ((m & (1 << i)) != 0) group[i] = (byte)number;
            rest &= ~m;
        }
        return deadwood;
    }

    /// <summary>
    /// The card to throw away: after discarding it the rest of the hand has the lowest deadwood (ties go to the card
    /// worth more points). Returns its index and the deadwood that remains.
    /// </summary>
    public static (int Index, int Deadwood) BestDiscard(ReadOnlySpan<byte> hand)
    {
        byte[] rest = new byte[MaxHand];
        Span<byte> view = rest;
        int best = -1;
        int bestDeadwood = 1000;
        for (int skip = 0; skip < hand.Length && skip < MaxHand; skip++)
        {
            int n = 0;
            for (int i = 0; i < hand.Length && i < MaxHand; i++)
                if (i != skip) { rest[n] = hand[i]; n++; }
            int d = Analyze(view.Slice(0, n));
            if (d < bestDeadwood || d == bestDeadwood && Points(hand[skip]) > Points(hand[best]))
            {
                best = skip;
                bestDeadwood = d;
            }
        }
        return (best, bestDeadwood);
    }

    private static int Analyze(ReadOnlySpan<byte> hand)
    {
        int n = hand.Length < MaxHand ? hand.Length : MaxHand;
        for (int i = 0; i < n; i++)
        {
            _rank[i] = (byte)Deck.Rank(hand[i]);
            _suit[i] = (byte)Deck.Suit(hand[i]);
            _points[i] = (byte)Points(hand[i]);
        }
        int full = (1 << n) - 1;
        for (int mask = 1; mask <= full; mask++) _isMeld[mask] = MeldMask(mask) ? (byte)1 : (byte)0;

        _best[0] = 0;
        for (int rest = 1; rest <= full; rest++)
        {
            int low = rest & -rest;
            int lowIndex = 0;
            while ((1 << lowIndex) != low) lowIndex++;
            int without = rest ^ low;
            int bestValue = _best[without] + _points[lowIndex];
            int choice = 0;
            // Every meld that contains the lowest card: the low bit plus a subset of the others.
            int sub = without;
            while (true)
            {
                int meld = sub | low;
                if (_isMeld[meld] != 0)
                {
                    int value = _best[rest ^ meld];
                    if (value < bestValue) { bestValue = value; choice = meld; }
                }
                if (sub == 0) break;
                sub = (sub - 1) & without;
            }
            _best[rest] = bestValue;
            _choice[rest] = choice;
        }
        return _best[full];
    }

    /// <summary>Is the set of cards (bit i = card i of the analysed hand) a set or a run?</summary>
    private static bool MeldMask(int mask)
    {
        int count = 0;
        int first = -1;
        bool sameRank = true;
        bool sameSuit = true;
        int suitSeen = 0;
        int rankSeen = 0;
        bool distinctSuits = true;
        bool distinctRanks = true;
        for (int i = 0; i < MaxHand; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            count++;
            if (first < 0) first = i;
            else
            {
                if (_rank[i] != _rank[first]) sameRank = false;
                if (_suit[i] != _suit[first]) sameSuit = false;
            }
            if ((suitSeen & (1 << _suit[i])) != 0) distinctSuits = false;
            suitSeen |= 1 << _suit[i];
            if ((rankSeen & (1 << _rank[i])) != 0) distinctRanks = false;
            rankSeen |= 1 << _rank[i];
        }
        if (count < 3) return false;
        if (sameRank && distinctSuits) return true;
        if (sameSuit && distinctRanks)
        {
            int low = 0;
            while ((rankSeen & (1 << low)) == 0) low++;
            int shifted = rankSeen >> low;
            return (shifted & (shifted + 1)) == 0;   // one block of consecutive ranks
        }
        return false;
    }
}
