using System;
using ESharp;
using ESharp.Platform;

namespace LanguageSample;

public struct Item
{
    public int Id;
    public int Weight;
}

public struct Box
{
    public int W;
    public int H;
    public int Tag;
    public Box(int w, int h) { W = w; H = h; Tag = 1; }
    public Box(int side) : this(side, side) { Tag += 10; }
}

public enum Color { Red, Green, Blue }

public static class Table
{
    public static int[] Squares = new int[6];
    public static int Count = 1;
    static Table()
    {
        for (int i = 0; i < Squares.Length; i++) Squares[i] = i * i;
        Count = Squares.Length;
    }
}

/// <summary>
/// Language features added for card games: ref/out/in parameters, value tuples and deconstruction, patterns and
/// switch expressions, local functions, static constructors, this(...) initializers and Vec&lt;T&gt;.
/// Prints "ok NAME" or "FAIL NAME".
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static bool TryHalf(int n, out int half)
    {
        if (n % 2 != 0) { half = 0; return false; }
        half = n / 2;
        return true;
    }

    static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }

    static int Weight(in Item it) { return it.Weight * 2; }

    static (int Low, int High) MinMax(int a, int b) { return a < b ? (a, b) : (b, a); }

    static int Classify(int n) => n switch { < 0 => -1, 0 => 0, > 0 and < 10 => 1, _ => 2 };


    static int Rank(Item it) => it switch { { Id: 0 } => 100, { Weight: > 5 } => 50, _ => 1 };

    static int CountWhere(ReadOnlySpan<Item> items, Func<Item, bool> test)
    {
        int n = 0;
        for (int i = 0; i < items.Length; i++) if (test(items[i])) n++;
        return n;
    }

    static int Fold(ReadOnlySpan<int> xs, int start, Func<int, int, int> f)
    {
        int acc = start;
        for (int i = 0; i < xs.Length; i++) acc = f(acc, xs[i]);
        return acc;
    }

    static int Double(int x) => x * 2;

    static int Fact(int n)
    {
        int Go(int k) { return k <= 1 ? 1 : k * Go(k - 1); }
        return Go(n);
    }

    [EntryPoint]
    public static void Main()
    {
        if (TryHalf(10, out int h)) Check(h == 5, "out declaration");
        int a = 1, b = 2;
        Swap(ref a, ref b);
        Check(a == 2 && b == 1, "ref swap");
        Item item = new Item { Id = 3, Weight = 8 };
        Check(Weight(in item) == 16, "in parameter");

        var mm = MinMax(9, 4);
        Check(mm.Low == 4 && mm.High == 9, "named tuple elements");
        (int lo, int hi) = MinMax(7, 2);
        Check(lo == 2 && hi == 7, "deconstruction");
        (a, b) = (b, a);
        Check(a == 1 && b == 2, "tuple swap");

        Check(Classify(-5) == -1 && Classify(0) == 0 && Classify(7) == 1 && Classify(70) == 2, "switch expression");
        Check(Rank(new Item()) == 100 && Rank(item) == 50 && Rank(new Item { Id = 4 }) == 1, "property patterns");
        int n = 5;
        Check(n is > 3 and < 9 && n is not 4, "is patterns");

        Check(Fact(5) == 120, "local function");
        Check(Table.Count == 6 && Table.Squares[5] == 25, "static constructor");
        Box box = new Box(3);
        Check(box.W == 3 && box.H == 3 && box.Tag == 11, "this initializer");

        Vec<Item> list = default;
        for (int i = 0; i < 10; i++) list.Add(new Item { Id = i, Weight = i * 2 });
        list.Insert(0, new Item { Id = 77 });
        list.RemoveAt(1);
        Item first = list[0];
        Check(list.Count == 10 && first.Id == 77 && list[1].Id == 1, "list add insert remove");
        int total = 0;
        Span<Item> items = list.AsSpan();
        foreach (Item it in items) total += it.Weight;
        Check(total == 90, "list as span");
        Check(CountWhere(list.AsSpan(), it => it.Weight > 5) == 7 && CountWhere(list.AsSpan(), it => it.Id < 100) == 10, "lambda as a predicate");
        list.Destroy();
        Check(list.Count == 0, "list destroy");

        int[] nums = new int[4];
        for (int i = 0; i < nums.Length; i++) nums[i] = i + 1;
        Check(Fold(nums, 0, (a, b) => a + b) == 10 && Fold(nums, 1, (a, b) => a * b) == 24, "lambdas with two parameters");
        Func<int, int> twice = Double;
        Check(twice(21) == 42 && Fold(nums, 0, (a, b) => a + Double(b)) == 20, "method group and delegate variable");
    }
}
