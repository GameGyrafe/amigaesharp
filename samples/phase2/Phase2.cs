using System;
using ESharp;
using ESharp.Platform;

namespace Phase2;

/// <summary>A struct with an inline array (a C# fixed-size buffer).</summary>
public unsafe struct Palette
{
    public fixed byte Colors[8];
    public int Count;
}

/// <summary>A struct with constructors and instance methods (the shape of a card-game Card).</summary>
public struct Card
{
    public int Rank;
    public int Suit;

    public Card(int rank, int suit)
    {
        if (rank < 0)
        {
            Rank = 0;
            Suit = 0;
            return;
        }
        Rank = rank;
        Suit = suit;
    }

    public Card(int rank)
    {
        Rank = rank;
        Suit = 0;
    }

    public bool IsRed() { return Suit == 1 || Suit == 2; }

    public int Points() { return IsRed() ? this.Rank * 2 : Rank; }

    public void Bump(int by) { Rank += by; }
}

/// <summary>A struct whose instance methods work on an inline array.</summary>
public unsafe struct Hand
{
    public fixed int Ranks[4];
    public int Count;

    public bool Add(int rank)
    {
        if (Count >= 4) return false;
        Ranks[Count] = rank;
        Count++;
        return true;
    }

    public int Total()
    {
        int t = 0;
        for (int i = 0; i < Count; i++) t += Ranks[i];
        return t;
    }
}

/// <summary>
/// Phase 2 data types: fixed-size arrays, spans and 16.16 fixed point. Every check prints
/// "ok NAME" or "FAIL NAME", so the same output is expected under dotnet, host gcc and on an Amiga.
/// </summary>
public static class Program
{
    static int[] Squares = { 0, 1, 4, 9, 16, 25 };
    static int[] Scratch = new int[8];

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static int Sum(ReadOnlySpan<int> values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++) total += values[i];
        return total;
    }

    static int SumEach(Span<int> values)
    {
        int total = 0;
        foreach (var v in values) total += v;
        return total;
    }

    static void Fill(Span<int> values, int value)
    {
        for (int i = 0; i < values.Length; i++) values[i] = value;
    }

    static int CountPrimes(int limit)
    {
        bool[] composite = new bool[64];
        int count = 0;
        for (int n = 2; n < limit; n++)
        {
            if (composite[n]) continue;
            count++;
            for (int m = n * 2; m < limit; m += n) composite[m] = true;
        }
        return count;
    }

    static unsafe int PaletteTotal()
    {
        Palette p = default;
        for (int i = 0; i < 8; i++) p.Colors[i] = (byte)(i * 2);
        p.Count = 8;
        int total = 0;
        for (int i = 0; i < p.Count; i++) total += p.Colors[i];
        return total;
    }

    static int Describe(Card k)
    {
        k.Bump(1);
        return k.Points();
    }

    [EntryPoint]
    public static void Main()
    {
        // Arrays and spans.
        Check(Squares.Length == 6, "static array length");
        Check(Sum(Squares) == 55, "array converts to ReadOnlySpan");
        Fill(Scratch, 3);
        Check(SumEach(Scratch) == 24, "fill and foreach over a static array");

        int[] local = new int[5];
        for (int i = 0; i < local.Length; i++) local[i] = i + 1;
        Span<int> all = local;
        Check(Sum(all) == 15, "span to ReadOnlySpan");
        Check(Sum(all.Slice(1, 3)) == 9, "Slice(start, length)");
        Check(Sum(all.Slice(3)) == 9, "Slice(start)");
        Fill(all.Slice(0, 2), 10);
        Check(local[0] == 10 && local[1] == 10 && local[2] == 3, "writes through a slice");
        Check(CountPrimes(50) == 15, "sieve");
        Check(PaletteTotal() == 56, "fixed buffer in a struct");

        // Fixed point.
        Fix three = Fix.FromInt(3);
        Fix half = Fix.FromRaw(Fix.OneRaw / 2);
        Fix c = three * half;
        Check(c.Raw == 98304, "3 * 0.5 = 1.5");
        Check(Fix.ToInt(c) == 1 && Fix.Frac(c) == 32768, "integer and fraction parts");
        Check((three / half).Raw == Fix.FromInt(6).Raw, "3 / 0.5 = 6");
        Check((Fix.FromInt(-2) * Fix.FromRaw(0x18000)).Raw == -196608, "-2 * 1.5 = -3");
        Check((-c).Raw == -98304, "negation");
        Check(Fix.ToInt(-c) == -2, "ToInt rounds towards negative infinity");
        Check(c > half && half < c && c != half && c >= c && c <= c && c == c, "comparisons");
        c += three;
        Check(c.Raw == 294912, "compound assignment");
        Check((three * 2).Raw == (2 * three).Raw && (three * 2).Raw == Fix.FromInt(6).Raw, "multiply by int");
        Check((Fix.FromInt(7) / 2).Raw == 229376, "divide by int");
        Check((Fix.FromRaw(0x28000) * Fix.FromRaw(0x28000)).Raw == 0x64000, "2.5 * 2.5 = 6.25");
        Check((Fix.FromInt(-1) * Fix.FromRaw(1)).Raw == -1, "smallest step keeps its sign");
        Check((Fix.FromRaw(-98304) * Fix.FromRaw(-163840)).Raw == 245760, "-1.5 * -2.5 = 3.75");
        Check((Fix.FromRaw(98304) * Fix.FromRaw(-163840)).Raw == -245760, "1.5 * -2.5 = -3.75");
        Check((Fix.FromRaw(0x00644000) * Fix.FromRaw(-0x00C88000)).Raw == -1317281792, "100.25 * -200.5");

        // Structs with constructors and instance methods.
        Card c2 = new Card(5, 1);
        Check(c2.Rank == 5 && c2.Suit == 1, "constructor sets fields");
        Check(c2.IsRed() && c2.Points() == 10, "instance method calling another");
        c2.Bump(2);
        Check(c2.Rank == 7, "method mutates its receiver");
        Card[] cards = new Card[3];
        cards[1] = new Card(4, 0);
        cards[1].Bump(1);
        Check(cards[1].Rank == 5 && cards[0].Rank == 0, "method on an array element");
        Card bad = new Card(-3, 2);
        Check(bad.Rank == 0 && bad.Suit == 0, "constructor with early return");
        Card solo = new Card(9);
        Check(solo.Rank == 9 && solo.Suit == 0, "constructor overloads");
        Check(Describe(c2) == 16 && c2.Rank == 7, "method on a by-value parameter");
        Hand hand = default;
        bool a1 = hand.Add(1), a2 = hand.Add(2), a3 = hand.Add(3), a4 = hand.Add(4), a5 = hand.Add(5);
        Check(a1 && a2 && a3 && a4 && !a5 && hand.Total() == 10, "inline array in an instance method");
    }
}
