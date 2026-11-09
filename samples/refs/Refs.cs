using System;
using ESharp;
using ESharp.Platform;

namespace RefsSample;

public struct GridPos
{
    public int X;
    public int Y;
}

/// <summary>from, from + step, ... while below 'to': a collection that is only an enumerator, no storage.</summary>
public struct Steps
{
    public int From;
    public int To;
    public int Step;

    public Steps(int from, int to, int step) { From = from; To = to; Step = step; }

    public StepsEnumerator GetEnumerator() { return new StepsEnumerator(From - Step, To, Step); }
}

public struct StepsEnumerator
{
    private int _value;
    private int _to;
    private int _step;

    public StepsEnumerator(int start, int to, int step) { _value = start; _to = to; _step = step; }

    public bool MoveNext()
    {
        _value += _step;
        return _value < _to;
    }

    public int Current { get { return _value; } }
}

/// <summary>A class that hands out its list's enumerator, so foreach works on the class itself.</summary>
public class Hand
{
    public Vec<int> Cards;

    public Hand() { Cards = new Vec<int>(4, false); }

    public VecEnumerator<int> GetEnumerator() { return Cards.GetEnumerator(); }
}

public static class Program
{
    static GridPos s_origin;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static ref int At(Span<int> xs, int i) { return ref xs[i]; }

    static ref int Largest(Span<int> xs)
    {
        int best = 0;
        for (int i = 1; i < xs.Length; i++) if (xs[i] > xs[best]) best = i;
        return ref xs[best];
    }

    static ref int Axis(ref GridPos p, bool x) => ref x ? ref p.X : ref p.Y;

    static ref GridPos Origin() { return ref s_origin; }

    static ref readonly int First(ReadOnlySpan<int> xs) { return ref xs[0]; }

    static void AddTen(ref int v) { v += 10; }

    static Steps Evens(int below) { return new Steps(0, below, 2); }

    [EntryPoint]
    public static void Main()
    {
        int a = 1;
        ref int r = ref a;
        r = 5;
        r++;
        Check(a == 6, "ref local writes through");

        int b = 0;
        r = ref b;
        r = 7;
        Check(a == 6 && b == 7, "ref local pointed at another variable");

        int[] nums = { 4, 9, 2, 7 };
        ref int third = ref nums[2];
        third *= 10;
        Check(nums[2] == 20, "ref to an array element");

        At(nums, 0) = 40;
        At(nums, 1) += 1;
        Check(nums[0] == 40 && nums[1] == 10, "assignment to a ref return (array element)");

        Largest(nums) = 0;
        Check(nums[0] == 0 && Largest(nums) == 20, "ref to the largest element");

        GridPos p = new GridPos { X = 1, Y = 2 };
        Axis(ref p, true) = 30;
        Axis(ref p, false) += 40;
        Check(p.X == 30 && p.Y == 42, "ref return into a struct field");

        Origin().X = 3;
        ref GridPos o = ref Origin();
        o.Y = 4;
        Check(s_origin.X == 3 && s_origin.Y == 4, "ref return of a static struct");

        GridPos[] pts = new GridPos[3];
        ref GridPos mid = ref pts[1];
        mid.X = 8;
        mid.Y = mid.X * 2;
        Check(pts[1].X == 8 && pts[1].Y == 16, "ref local to a struct in an array");

        AddTen(ref At(nums, 3));
        Check(nums[3] == 17, "ref return passed on as a ref argument");

        ref readonly int head = ref First(nums);
        nums[0] = 99;
        Check(head == 99, "ref readonly local sees later writes");

        Span<int> span = nums;
        foreach (ref int v in span) v += 1;
        Check(nums[0] == 100 && nums[3] == 18, "foreach by ref over a span");

        Vec<int> list = new Vec<int>(4, false);
        for (int i = 1; i <= 6; i++) list.Add(i * i);
        int sum = 0;
        foreach (int sq in list) sum += sq;
        Check(sum == 91, "foreach over a Vec");

        int some = 0;
        foreach (int sq in list)
        {
            if (sq == 4) continue;
            if (sq > 20) break;
            some += sq;
        }
        Check(some == 1 + 9 + 16, "continue and break in foreach over a Vec");

        int digits = 0;
        foreach (int s in new Steps(1, 10, 3)) digits = digits * 10 + s;
        Check(digits == 147, "user struct enumerator");

        int evens = 0;
        foreach (int e in Evens(10)) evens += e;
        Check(evens == 20, "enumerable returned by a call");

        Hand hand = new Hand();
        hand.Cards.Add(11);
        hand.Cards.Add(12);
        hand.Cards.Add(13);
        int total = 0;
        foreach (int card in hand) total += card;
        Check(total == 36, "class handing out a Vec enumerator");

        int pairs = 0;
        foreach (int s in new Steps(0, 3, 1))
            foreach (int sq in list)
            {
                if (sq > 9) break;
                pairs += s;
            }
        Check(pairs == (0 + 1 + 2) * 3, "nested foreach with break in the inner loop");

        list.Destroy();
        hand.Cards.Destroy();
        int left = 0;
        foreach (int sq in list) left++;
        Check(left == 0, "foreach over an empty Vec");
    }
}
