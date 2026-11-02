using System;
using ESharp;

namespace CardsDemo;

/// <summary>
/// A small deterministic random number generator (xorshift32), so shuffles are repeatable from a seed
/// on the host and on the Amiga alike.
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
/// Cards are bytes: <c>suit * 13 + rank</c> with rank 0 = ace .. 12 = king. A deck can use any number of suits
/// (4 for a standard deck, 8 for a double deck such as a two-deck patience), which is why the suit count is a parameter.
/// </summary>
public static class Deck
{
    public const int Ranks = 13;

    public static int Rank(int card) { return card % Ranks; }

    public static int Suit(int card) { return card / Ranks; }

    /// <summary>Fills <paramref name="cards"/> with <c>suits * 13</c> cards in order. Returns the number written (the span must be big enough).</summary>
    public static int Fill(Span<byte> cards, int suits)
    {
        int n = suits * Ranks;
        if (n > cards.Length) return 0;
        for (int i = 0; i < n; i++) cards[i] = (byte)i;
        return n;
    }
}
