using System;
using ESharp;
using ESharp.Platform;

namespace LambdasSample;

public delegate int Op(int a, int b);

public class Counter
{
    public int Total;
    public int Step;

    public Counter() { Step = 3; }

    public int AddAll(ReadOnlySpan<int> xs)
    {
        Each(xs, x => Total += x * Step);   // uses 'this'
        return Total;
    }

    static void Each(ReadOnlySpan<int> xs, Action<int> f)
    {
        for (int i = 0; i < xs.Length; i++) f(xs[i]);
    }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static int Apply(Func<int, int> f, int x) { return f(x); }
    static int Apply2(Op op, int a, int b) { return op(a, b); }
    static void Repeat(int n, Action body) { for (int i = 0; i < n; i++) body(); }
    static int CountIf(ReadOnlySpan<int> xs, Func<int, bool> test)
    {
        int n = 0;
        for (int i = 0; i < xs.Length; i++) if (test(xs[i])) n++;
        return n;
    }
    static int Add(int a, int b) { return a + b; }
    static int Square(int x) { return x * x; }
    static int Twice(Func<int, int> f, int x) { return f(f(x)); }

    [EntryPoint]
    public static void Main()
    {
        int k = 10;
        Check(Apply(x => x + k, 5) == 15, "lambda captures a local");

        int sum = 0;
        Repeat(4, () => sum += k);
        Check(sum == 40, "a lambda writes to a captured local");

        Func<int, int> addK = x => x + k;
        k = 20;
        Check(addK(1) == 21, "a captured local is shared, not copied");

        int a = 2, b = 3;
        Func<int> both = () => a * 100 + b;
        a = 7;
        Check(both() == 703, "two captured locals");

        Check(Apply2((x, y) => x * y + k, 3, 4) == 32, "a user delegate type with a capture");
        Check(Apply2(Add, 3, 4) == 7, "a static method as a user delegate");

        int[] data = { 3, 8, 1, 9, 4 };
        int limit = 3;
        Check(CountIf(data, v => v > limit) == 3, "capture as a span filter");
        limit = 8;
        Check(CountIf(data, v => v > limit) == 1, "same code, new captured value");

        Check(Twice(x => x * k, 2) == 800, "a lambda passed through another method");
        Check(Twice(Square, 3) == 81, "a method group");

        int outer = 5;
        Func<int, int> nested = x => Apply(y => y + x + outer, 100);
        Check(nested(1) == 106, "a lambda inside a lambda captures through both");

        int hits = 0;
        for (int i = 0; i < 3; i++)
        {
            int j = i * 2;
            Repeat(2, () => hits += j + 1);
        }
        Check(hits == 18, "closures created inside a loop");

        Func<int, int> none = null;
        Check(none == null, "null delegate");
        none = Square;
        Check(none != null && none(6) == 36, "assigned delegate");

        Counter c = new Counter();
        Check(c.AddAll(data) == 75, "a lambda that uses this");
        Heap.Free(ref c);

        Func<int, int> twiceIt = x => Twice(y => y + k, x);
        Check(twiceIt(0) == 40, "capture of a captured variable");
    }
}
