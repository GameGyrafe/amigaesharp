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

/// <summary>A heap class: a linked list node that is allocated with new and freed with Heap.Free.</summary>
public class ListNode
{
    public int Value;
    public ListNode Next;

    public ListNode(int value, ListNode next) { Value = value; Next = next; }

    public int Sum() { return Value + (Next == null ? 0 : Next.Sum()); }
}

public class Card { public int Rank; }

public class Ace : Card { public Ace() { Rank = 14; } }

public interface IScorer
{
    int Score(int card);
}

public struct DoubleScorer : IScorer
{
    public int Score(int card) { return card * 2; }
}

public class BonusScorer : IScorer
{
    public int Bonus;
    public int Score(int card) { return card + Bonus; }
}

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
    const string FoldKey = "key";
    static bool FoldedA = FoldKey == "key";
    static bool FoldedB = FoldKey != "key";

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

    static int Total<T>(ref T scorer, int n) where T : IScorer
    {
        int sum = 0;
        for (int i = 1; i <= n; i++) sum += scorer.Score(i);
        return sum;
    }

    static int Sum(IScorer scorer, int n)
    {
        int sum = 0;
        for (int i = 1; i <= n; i++) sum += scorer.Score(i);
        return sum;
    }

    /// <summary>A copy of the text at a different address (formatted into a buffer), so == has to compare characters.</summary>
    static string Copy(string s) => $"{s}";

    static int SuitColor(string suit)
    {
        switch (suit)
        {
            case "hearts":
            case "diamonds": return 1;
            case "spades":
            case "clubs": return 2;
            case null: return -1;
            default: return 0;
        }
    }

    static int Points(string rank) => rank switch { "ace" => 11, "king" or "queen" => 10, null => -1, _ => 0 };

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

        ListNode head = new ListNode(1, new ListNode(2, new ListNode(3, null)));
        Check(head.Sum() == 6 && head.Next.Next.Next == null, "heap objects linked by reference");
        ListNode alias = head;
        alias.Value = 10;
        Check(head.Value == 10 && head == alias, "an object is shared, not copied");
        Heap.Free(ref head.Next.Next);
        Heap.Free(ref head.Next);
        Heap.Free(ref head);
        Check(head == null && alias != null, "Heap.Free clears the variable");
        DoubleScorer doubled = default;
        BonusScorer bonus = new BonusScorer { Bonus = 100 };
        Check(Total(ref doubled, 3) == 12 && Total(ref bonus, 3) == 306, "interface constraint with a struct and a class");
        IScorer asInterface = bonus;
        Check(asInterface != null && asInterface.Score(2) == 102 && Sum(asInterface, 3) == 306, "interface value calls the class");
        bonus.Bonus = 0;
        Check(Sum(asInterface, 3) == 6, "the interface value shares the object");
        IScorer nothing = null;
        Check(nothing == null && nothing is null && asInterface is not null, "null interface value");
        Heap.Free(ref asInterface);
        Check(asInterface == null, "Heap.Free on an interface value");

        string who = "Ann";
        int count = 42;
        string text = $"Hi {who}: {count,5}|{count,-5}|{count:D5}|{255:X}|{-7}|{true}|{'x'}";
        Check(text == "Hi Ann:    42|42   |00042|FF|-7|True|x", "interpolated string");
        Check("n=" + count + "!" == "n=42!" && $"{{literal}} {count}" == "{literal} 42", "concatenation and escaped braces");

        int[] nums = new int[4];
        for (int i = 0; i < nums.Length; i++) nums[i] = i + 1;
        Check(Fold(nums, 0, (a, b) => a + b) == 10 && Fold(nums, 1, (a, b) => a * b) == 24, "lambdas with two parameters");
        Func<int, int> twice = Double;
        Check(twice(21) == 42 && Fold(nums, 0, (a, b) => a + Double(b)) == 20, "method group and delegate variable");

        // Strings compare by their characters (C# semantics), whatever their addresses; null equals only null.
        string ann = Copy("Ann");
        string noText = null;
        Check(ann == who && who == ann && ann != "Anne" && !(ann != "Ann") && ann.Equals(who) && string.Equals(who, ann), "string == compares characters");
        Check(noText == null && noText != ann && ann != noText && noText != "" && !string.Equals(noText, ""), "string null rules");
        Check(SuitColor(Copy("diamonds")) == 1 && SuitColor(Copy("clubs")) == 2 && SuitColor(Copy("stars")) == 0 && SuitColor(null) == -1, "switch on a string");
        Check(Points(Copy("queen")) == 10 && Points(Copy("ace")) == 11 && Points(Copy("two")) == 0 && Points(null) == -1
            && Copy("Ann") is "Ann" && Copy("Ann") is not "Bob", "string patterns");

        // The null-forgiving operator only quiets the nullable analysis: no code.
        ListNode chain = new ListNode(1, new ListNode(2, null));
        string bob = Copy("Bob");
        Check(chain!.Next!.Value == 2 && chain.Next!.Sum() == 2 && bob! == "Bob", "null-forgiving operator");

        // A base-typed and a derived-typed reference to the same object are equal; ?? and ??= pick the first non-null one.
        Ace ace = new Ace();
        Card face = ace;
        Card none = null;
        Check(face == ace && ace == face && none != ace && (none ?? face) == ace, "base and derived references");
        none ??= ace;
        Check(none == face && (noText ?? "empty") == "empty", "?? and ??=");

        // Equals on classes without an override is reference equality; a constant string compare folds (also in a static initializer).
        Card other = new Card();
        Check(face.Equals(ace) && !face.Equals(other) && object.Equals(ace, face) && !object.Equals(ace, other) && object.ReferenceEquals(face, ace), "Equals is reference equality");
        Check(FoldedA && !FoldedB, "constant string compare in a static initializer");
    }
}
