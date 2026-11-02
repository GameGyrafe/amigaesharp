using System;
using ESharp;
using ESharp.Platform;

namespace OperatorsSample;

public struct Mini
{
    public short V;

    public static implicit operator Mini(int x)
    {
        Mini m = default;
        m.V = (short)x;
        return m;
    }

    public static explicit operator int(Mini m) => m.V;

    public static Mini operator +(Mini a, Mini b) => a.V + b.V;
}

public unsafe struct Grid
{
    fixed int cells[16];
    public int Writes { get; set; }

    public int this[int i]
    {
        get { return cells[i]; }
        set { cells[i] = value; Writes++; }
    }

    public int this[int x, int y] => cells[y * 4 + x];

    public int Sum()
    {
        int total = 0;
        for (int i = 0; i < 16; i++) total += this[i];
        return total;
    }

    public void Fill(int v) { for (int i = 0; i < 16; i++) this[i] = v; }
}

public struct Table<T> where T : unmanaged
{
    T a;
    T b;

    public T this[int i]
    {
        get { return i == 0 ? a : b; }
        set { if (i == 0) a = value; else b = value; }
    }
}

/// <summary>
/// Implicit user-defined conversions and indexers on structs. Every check prints "ok NAME" or
/// "FAIL NAME"; the output is the same under dotnet, host gcc and on an Amiga.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static int Take(Mini m) { return m.V; }
    static Mini Make() { return 40; }
    static Fix Half(Fix f) { return f / 2; }

    [EntryPoint]
    public static unsafe void Main()
    {
        Mini a = 5;
        Check(a.V == 5, "implicit conversion in an initializer");
        a = 9;
        Check(a.V == 9, "implicit conversion in an assignment");
        Check(Take(12) == 12, "implicit conversion of an argument");
        Check(Make().V == 40, "implicit conversion of a return value");
        Mini sum = a + 3;
        Check(sum.V == 12, "implicit conversion of an operand");
        Check((int)sum == 12, "explicit conversion still works");

        Fix f = 3;
        Check(Fix.ToInt(f) == 3, "Fix from int in an initializer");
        f = f + 2;
        Check(Fix.ToInt(f) == 5, "Fix plus int");
        Check(f > 4 && f < 6 && f == 5, "Fix compared with int");
        Check(Fix.ToInt(Half(7)) == 3 && Fix.Frac(Half(7)) == 32768, "Fix argument from int");
        Check(Fix.ToInt(f * 2) == 10, "Fix times int is still the int overload");

        Grid g = default;
        g[3] = 30;
        g[5] = 50;
        Check(g[3] == 30 && g[5] == 50 && g[0] == 0, "indexer get and set");
        Check(g.Writes == 2, "indexer setter runs its body");
        g[3] += 7;
        g[5]++;
        Check(g[3] == 37 && g[5] == 51 && g.Writes == 4, "compound assignment and ++ on an indexer");
        g[1 + 1] = 2;
        Check(g[2] == 2, "indexer with an expression index");
        g.Fill(1);
        Check(g.Sum() == 16 && g.Writes == 4 + 1 + 16, "this[...] inside a method");
        g[2 + 1] = 7;
        g[1] = 1;
        Check(g[1, 0] == 1 && g[3, 0] == 7, "two-index indexer, expression-bodied");

        Grid[] grids = new Grid[2];
        grids[1][2] = 8;
        Check(grids[1][2] == 8 && grids[0][2] == 0, "indexer on an array element");

        Table<int> t = default;
        t[0] = 4;
        t[1] = 6;
        t[1] += 1;
        Check(t[0] == 4 && t[1] == 7, "indexer in a generic struct");
        Table<Fix> tf = default;
        tf[1] = 2;
        Check(Fix.ToInt(tf[1]) == 2, "generic indexer with an implicit conversion");
    }
}
