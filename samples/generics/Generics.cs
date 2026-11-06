using System;
using ESharp;
using ESharp.Platform;

namespace GenericsSample;

public struct Card
{
    public int Rank;
    public int Suit;
}

public struct Pair<T> where T : unmanaged
{
    public T First;
    public T Second;

    public Pair(T first, T second)
    {
        First = first;
        Second = second;
    }

    public Pair<T> Swapped() { return new Pair<T>(Second, First); }
}

public struct Box<T> where T : unmanaged
{
    public T Value;
    public int Count;

    public void Set(T v) { Value = v; Count++; }
}

/// <summary>
/// Monomorphised generics: generic structs, generic static methods (explicit and inferred type
/// arguments), nested instantiation (Pair of Pair), and Pool&lt;T&gt; from the platform library.
/// Every check prints "ok NAME" or "FAIL NAME"; the output is the same under dotnet, host gcc and on an Amiga.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static T Pick<T>(bool first, T a, T b) where T : unmanaged { return first ? a : b; }

    static int SumPair(Pair<int> p) { return p.First + p.Second; }

    static T Zero<T>() where T : unmanaged { return default; }

    static unsafe void Fill<T>(Span<T> s, T value) where T : unmanaged
    {
        for (int i = 0; i < s.Length; i++) s[i] = value;
    }

    [EntryPoint]
    public static unsafe void Main()
    {
        var pi = new Pair<int>(3, 4);
        Check(pi.First == 3 && pi.Second == 4, "generic struct with int");
        Check(SumPair(pi.Swapped()) == 7 && pi.Swapped().First == 4, "generic struct method");

        var pf = new Pair<Fix>(Fix.FromInt(2), Fix.FromInt(5));
        Check(Fix.ToInt(pf.Second) == 5 && Fix.ToInt(pf.Swapped().First) == 5, "generic struct with Fix");

        var pc = new Pair<Card>(new Card { Rank = 12, Suit = 1 }, new Card { Rank = 3, Suit = 2 });
        Check(pc.Swapped().First.Rank == 3, "generic struct with a user struct");

        var nested = new Pair<Pair<int>>(new Pair<int>(1, 2), new Pair<int>(3, 4));
        Check(nested.Second.First == 3 && nested.Swapped().First.Second == 4, "nested instantiation");

        Check(Pick(true, 10, 20) == 10 && Pick<int>(false, 10, 20) == 20, "generic method, inferred and explicit");
        Check(Fix.ToInt(Pick(false, Fix.FromInt(1), Fix.FromInt(8))) == 8, "generic method with Fix");
        Check(Zero<int>() == 0 && Zero<Card>().Rank == 0, "generic method with only an explicit argument");

        Box<byte> b = default;
        b.Set(200);
        b.Set(201);
        Check(b.Value == 201 && b.Count == 2, "generic struct instance method mutates");

        int[] data = new int[5];
        Fill(data, 7);
        Check(data[0] == 7 && data[4] == 7, "generic method over Span");

        // Pool<T>
        uint before = Mem.AvailChip();
        var pool = new Pool<Card>(4, true);
        Check(pool.Capacity() == 4 && pool.InUse() == 0, "pool created");
        Card* c1 = pool.Alloc();
        Card* c2 = pool.Alloc();
        Check(c1 != null && c2 != null && c1 != c2, "pool hands out distinct items");
        c1->Rank = 5;
        c2->Rank = 6;
        Check(c1->Rank == 5 && c2->Rank == 6 && pool.InUse() == 2, "pool items are writable");
        Check(pool.Free(c1) && pool.InUse() == 1, "pool free");
        Check(!pool.Free(c1), "pool ignores a double free");
        Card* c3 = pool.Alloc();
        Check(c3 == c1 && c3->Rank == 0, "pool reuses a freed slot, zeroed");
        Card* c4 = pool.Alloc();
        Card* c5 = pool.Alloc();
        Card* c6 = pool.Alloc();
        Check(c4 != null && c5 != null && c6 == null, "pool returns null when full");
        Check(pool.InUse() == 4, "pool is full");
        Card outside = default;
        Check(!pool.Free(&outside), "pool rejects a foreign pointer");
        Check(!pool.Free((Card*)((byte*)c2 + 1)), "pool rejects a misaligned pointer");
        pool.Reset();
        Check(pool.InUse() == 0 && pool.Alloc() != null, "pool reset");
        pool.Destroy();
        Check(Mem.AvailChip() == before && pool.Capacity() == 0, "pool destroy returns the memory");

        var ints = new Pool<int>(3, false);
        int* n = ints.Alloc();
        *n = 42;
        Check(*n == 42 && ints.InUse() == 1, "pool of int");
        ints.Destroy();

        var impossible = new Pool<Card>(100000000, true);
        Check(impossible.Capacity() == 0 && impossible.Alloc() == null, "impossible pool fails cleanly");
    }
}
