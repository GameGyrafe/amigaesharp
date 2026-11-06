using ESharp;
using ESharp.Platform;

namespace CollectionsSample;

public enum Suit { Clubs, Diamonds, Hearts, Spades }

public struct Pos
{
    public short X;
    public short Y;
    public Pos(int x, int y) { X = (short)x; Y = (short)y; }
}

/// <summary>Dictionary, HashSet, StringMap and StringSet. Prints "ok NAME" or "FAIL NAME".</summary>
public static unsafe class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static string Word(int i)
    {
        switch (i % 5)
        {
            case 0: return "ace";
            case 1: return "king";
            case 2: return "queen";
            case 3: return "jack";
            default: return "ten";
        }
    }

    [EntryPoint]
    public static void Main()
    {
        Dictionary<int, int> d = default;
        Check(d.Count == 0 && !d.ContainsKey(5) && d[5] == 0, "empty dictionary");
        bool grew = true;
        for (int i = 0; i < 200; i++) grew &= d.Add(i * 7, i);
        Check(grew && d.Count == 200, "adds grow the table");
        bool all = true;
        for (int i = 0; i < 200; i++) all &= d.TryGetValue(i * 7, out int v) && v == i;
        Check(all && !d.ContainsKey(3), "lookups after growth");
        Check(!d.Add(14, 99) && d[14] == 2, "Add refuses a duplicate");
        d[14] = 99;
        Check(d[14] == 99 && d.Count == 200, "indexer replaces");
        bool removed = true;
        for (int i = 0; i < 200; i += 2) removed &= d.Remove(i * 7);
        Check(removed && d.Count == 100 && !d.ContainsKey(0) && d.ContainsKey(7) && !d.Remove(0), "remove");
        for (int i = 0; i < 200; i += 2) d.Set(i * 7, -i);
        Check(d.Count == 200 && d[14] == -2 && d[7] == 1, "removed slots are reused");
        int sum = 0;
        int seen = 0;
        for (int s = d.First(); s >= 0; s = d.Next(s)) { sum += d.ValueAt(s) + d.KeyAt(s); seen++; }
        Check(seen == 200 && sum > 0, "enumeration visits every entry once");
        d.Clear();
        Check(d.Count == 0 && !d.ContainsKey(7) && d.First() < 0, "clear");
        d.Destroy();
        Check(d.Count == 0 && d.First() < 0, "destroy");

        Dictionary<Suit, int> bySuit = default;
        bySuit[Suit.Hearts] = 3;
        bySuit[Suit.Spades] = 4;
        bySuit[Suit.Hearts] += 10;
        Check(bySuit[Suit.Hearts] == 13 && bySuit[Suit.Spades] == 4 && bySuit[Suit.Clubs] == 0, "enum keys");
        bySuit.Destroy();

        Dictionary<Pos, int> grid = default;
        for (int x = 0; x < 6; x++) for (int y = 0; y < 6; y++) grid.Set(new Pos(x, y), x * 10 + y);
        Check(grid.Count == 36 && grid[new Pos(4, 5)] == 45 && !grid.ContainsKey(new Pos(6, 0)), "struct keys");
        grid.Destroy();

        HashSet<int> set = default;
        bool fresh = set.Add(5) && set.Add(9) && set.Add(5 + 64);
        Check(fresh && !set.Add(9) && set.Count == 3 && set.Contains(69) && !set.Contains(6), "hash set");
        set.Remove(9);
        int items = 0;
        for (int s = set.First(); s >= 0; s = set.Next(s)) items += set.ItemAt(s);
        Check(!set.Contains(9) && items == 5 + 69, "hash set remove and enumerate");
        set.Destroy();

        StringMap<int> counts = default;
        for (int i = 0; i < 23; i++) counts[Word(i)] += 1;
        Check(counts.Count == 5 && counts["ace"] == 5 && counts["king"] == 5 && counts["queen"] == 5 && counts["jack"] == 4 && counts["ten"] == 4, "string map counts words");
        Check(counts.ContainsKey("jack") && !counts.ContainsKey("joker") && counts["joker"] == 0, "string map lookup");
        Check(counts.Remove("ace") && !counts.ContainsKey("ace") && counts.Count == 4, "string map remove");
        string longest = "";
        for (int s = counts.First(); s >= 0; s = counts.Next(s))
            if (counts.KeyAt(s).Length > longest.Length) longest = counts.KeyAt(s);
        Check(longest.Length == 5 && longest[0] == 'q', "string map keys");
        counts.Destroy();

        StringSet names = default;
        Check(names.Add("ann") && names.Add("bob") && !names.Add("ann") && names.Contains("bob") && !names.Contains("cy") && names.Count == 2, "string set");
        names.Destroy();

        // Free memory is measured around a block that prints nothing (printing allocates by itself).
        uint before = Mem.AvailFast() + Mem.AvailChip();
        Dictionary<int, int> a = default;
        StringMap<int> b = default;
        HashSet<int> c = default;
        for (int i = 0; i < 300; i++) { a.Set(i, i); b.Set(Word(i), i); c.Add(i); }
        for (int i = 0; i < 300; i += 3) { a.Remove(i); c.Remove(i); }
        a.Destroy(); b.Destroy(); c.Destroy();
        uint after = Mem.AvailFast() + Mem.AvailChip();
        Check(after == before, "all memory is given back");
    }
}
