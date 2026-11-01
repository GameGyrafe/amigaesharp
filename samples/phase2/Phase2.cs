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
    }
}
