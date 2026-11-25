using System;
using ESharp;
using ESharp.Platform;

namespace ObjectsSample;

/// <summary>A crate of goods: a plain struct with a computed property, read on temporaries below.</summary>
public struct Crate
{
    public int Weight;
    public int Count;
    public int Total => Weight * Count;
    public int With(int extra) { return Total + extra; }
    public void Add(int n) { Count += n; }
}

public interface IVessel
{
    int Load();
    bool TryTake(int amount, out int left);
    void Swap(ref int a, ref int b);
    int Weigh(ReadOnlySpan<Crate> crates);
}

/// <summary>A ship that owns its hold: an array of crates inside the object, with initializers that run before the constructor body.</summary>
public class Ship : IVessel
{
    public string Name;
    public Crate[] Hold = new Crate[4];
    public int[] Marks = { 2, 4, 6 };
    public int Built = Yard.Next();
    public int Capacity { get; set; } = 40;

    public Ship(string name) { Name = name; }

    public virtual int Load()
    {
        int t = 0;
        for (int i = 0; i < Hold.Length; i++) t += Hold[i].Total;
        return t;
    }

    public bool TryTake(int amount, out int left)
    {
        left = Capacity - Load() - amount;
        return left >= 0;
    }

    public void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }

    public int Weigh(ReadOnlySpan<Crate> crates)
    {
        int t = 0;
        foreach (Crate c in crates) t += c.Total;
        return t;
    }
}

public class Tug : Ship
{
    public int Power = 9;
    public Tug(string name) : base(name) { }
    public override int Load() { return base.Load() + Power; }
}

public sealed class Raft : IVessel
{
    public int Load() { return 1; }
    public bool TryTake(int amount, out int left) { left = 0; return false; }
    public void Swap(ref int a, ref int b) { }
    public int Weigh(ReadOnlySpan<Crate> crates) { return crates.Length; }
}

/// <summary>A harbour keeps its ships in a list of references and its berths in an array of references; its log is a nested class.</summary>
public class Harbour
{
    public sealed class Log
    {
        public int[] Entries = new int[8];
        public int Count;
        public void Write(int v) { if (Count < Entries.Length) { Entries[Count] = v; Count++; } }
    }

    public Ship[] Berths = new Ship[3];
    public Vec<Ship> Fleet = new Vec<Ship>();
    public Log Book = new Log();

    public void Arrive(Ship s)
    {
        Fleet.Add(s);
        for (int i = 0; i < Berths.Length; i++)
            if (Berths[i] == null) { Berths[i] = s; Book.Write(i); return; }
        Book.Write(-1);
    }

    public Ship Find(string name)
    {
        foreach (Ship s in Fleet) if (s.Name == name) return s;
        return null;
    }
}

/// <summary>A generic struct with static fields: each instantiation has its own (issue #161).</summary>
public struct Tally<T> where T : unmanaged
{
    public static int Count;
    public static int Start = 5;
    public T Last;
    public static int Add() { Count++; return Count; }
}

public interface IBase { int Id(); }
public interface IDerived : IBase { int Extra(); }
public interface IMore : IDerived { int More(); }

public class Deep : IMore
{
    public int Id() { return 1; }
    public int Extra() { return 2; }
    public int More() { return 3; }
}

public class Shallow : IDerived
{
    public int Id() { return 10; }
    public int Extra() { return 20; }
}

public static class Yard
{
    static int _serial = 100;
    public static int Next() { _serial++; return _serial; }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    // Static initializers that run code at start-up (before the static constructor).
    static Harbour Port = new Harbour();
    static int Base = Yard.Next();
    static int Twice = Base * 2;
    static Func<int, int> Scale = x => x * 3;
    static int Ready;
    static Program() { Ready = Twice - Base; }

    static Crate MakeCrate(int weight, int count)
    {
        var c = new Crate();
        c.Weight = weight;
        c.Count = count;
        return c;
    }

    static void Noop() { }
    static void Other() { }
    static int Triple(int x) { return x * 3; }
    static IBase Widen(IDerived d) { return d; }

    static ReadOnlySpan<Crate> NoCrates() { return ReadOnlySpan<Crate>.Empty; }

    // C keywords as parameter and local names.
    static int Mix(int register, bool signed, int auto)
    {
        int unsigned = register + auto;
        return signed ? -unsigned : unsigned;
    }

    static string Kind(IVessel v)
    {
        if (v is Tug t) return "tug " + t.Name;
        if (v is Ship s) return "ship " + s.Name;
        return v is Raft ? "raft" : "none";
    }

    [EntryPoint]
    public static void Main()
    {
        Check(Base == 101 && Twice == 202 && Ready == 101 && Scale(5) == 15, "static initializers run code before the static constructor");

        var ark = new Ship("Ark");
        var tug = new Tug("Puff");
        Check(ark.Built == 102 && tug.Built == 103 && ark.Capacity == 40 && tug.Power == 9, "field initializers, base class first");
        Check(ark.Hold.Length == 4 && ark.Marks.Length == 3 && ark.Marks[2] == 6 && ark.Hold[3].Count == 0, "array fields in an object");

        ark.Hold[0] = MakeCrate(3, 2);
        ark.Hold[1].Weight = 5;
        ark.Hold[1].Add(2);
        Check(ark.Load() == 16 && tug.Load() == 9, "the hold is the ship's own");

        Check(MakeCrate(4, 5).Total == 20 && MakeCrate(1, 1).With(2) == 3, "property and method on a returned struct");
        Span<Crate> hold = ark.Hold;
        hold[2].Add(3);
        Check(ark.Hold[2].Count == 3 && hold[0].Total == 6, "span element: changed in place");

        Port.Arrive(ark);
        Port.Arrive(tug);
        Port.Arrive(new Ship("Cog"));
        Port.Arrive(new Ship("Dhow"));
        Check(Port.Fleet.Count == 4 && Port.Berths[2].Name == "Cog" && Port.Book.Count == 4 && Port.Book.Entries[3] == -1, "a list and an array of references");
        Check(Port.Find("Puff") == tug && Port.Find("Junk") == null && Port.Fleet.IndexOf(tug) == 1, "find by reference");
        Check(Port.Fleet[0].Hold[1].Total == 10, "member chain through a list element");
        Port.Fleet.Remove(tug);
        Check(Port.Fleet.Count == 3 && Port.Fleet[1].Name == "Cog", "remove keeps the order");

        IVessel v = ark;
        IVessel w = tug;
        IVessel r = new Raft();
        Check(v.TryTake(20, out int left) && left == 4 && !v.TryTake(30, out _), "out parameters through an interface");
        int a = 1, b = 2;
        v.Swap(ref a, ref b);
        Check(a == 2 && b == 1, "ref parameters through an interface");
        Check(v.Weigh(ark.Hold) == 16 && r.Weigh(ark.Hold) == 4 && v.Weigh(NoCrates()) == 0, "span of structs through an interface");
        Check(Kind(v) == "ship Ark" && Kind(w) == "tug Puff" && Kind(r) == "raft" && Kind(null) == "none", "type tests on interface values");
        Check(w is Ship && !(r is Ship) && (w as Tug) == tug, "is and as");

        // A delegate compared with a method group, the static fields of a generic struct, an interface converted to its base interface (issue #161).
        Action noop = Noop;
        Func<int, int> triple = Triple;
        Check(noop == Noop && Noop == noop && noop != Other && triple == Triple && !(noop == Other), "delegate compared with a method group");
        Tally<int>.Add();
        Tally<int>.Add();
        Tally<byte>.Add();
        Check(Tally<int>.Count == 2 && Tally<byte>.Count == 1 && Tally<int>.Start == 5 && Tally<byte>.Start == 5, "static fields in a generic struct");
        IMore deep = new Deep();
        IDerived mid = deep;
        IBase low = mid;
        IBase fromShallow = Widen(new Shallow());
        IDerived none = null;
        IBase nothing = none;
        Check(low.Id() == 1 && mid.Id() == 1 && deep.Id() == 1 && ((IBase)deep).Id() == 1 && fromShallow.Id() == 10 && nothing == null,
            "interface converted to its base interface");

        var crates = new Vec<Crate>();
        for (int i = 1; i <= 6; i++) crates.Add(MakeCrate(i, 1));
        Span<Crate> middle = crates.AsSpan().Slice(2, 3);
        Check(middle.Length == 3 && middle[0].Weight == 3 && crates[5].Total == 6, "Slice and members on a list's span");
        Check(NoCrates().IsEmpty && !middle.IsEmpty, "an empty span");
        crates.Destroy();

        Check(Mix(2, true, 3) == -5 && Mix(register: 1, signed: false, auto: 1) == 2, "C keywords as names");

        string title = "Harbour master";
        Check(title.Substring(0, 7) == "Harbour" && title.Substring(8) == "master" && title.Substring(14).Length == 0, "Substring");

        // esharp test passes none unless given --args "SEED=42 auto" (then the host and the target both see them)
        if (Args.Count == 0) Check(Args.Get(0) == null && !Args.Has("AUTO") && Args.Int("SEED", 7) == 7, "no program arguments");
        else Check(Args.Get(0) != null && Args.Get(Args.Count) == null, "program arguments are seen");
        if (Args.Count > 0) Dos.PutStr($"{Args.Count} arguments, first {Args.Get(0)}, SEED {Args.Int("SEED", -1)}, AUTO {(Args.Has("AUTO") ? "on" : "off")}\n");

        for (int i = Port.Fleet.Count - 1; i >= 0; i--)
        {
            Ship s = Port.Fleet[i];
            Heap.Free(ref s);
        }
        Port.Fleet.Destroy();
        Heap.Free(ref tug);
        Check(Port.Fleet.Count == 0 && tug == null, "freed");
    }
}
