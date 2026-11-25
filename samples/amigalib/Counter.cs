using ESharp;

namespace AmigaLibSample;

/// <summary>A counter per opener: every program (or every OpenLibrary) that opens amigalib.library counts on its own.</summary>
public static class Counter
{
    private static int _value = 100;        // a constant start value: copied into each opener's data when it opens
    private static int _calls;
    private static History _history = new History();   // made by each opener's start-up code, in its own memory pool

    /// <summary>Adds <paramref name="step"/> and returns the new value.</summary>
    [LibraryFunction]
    public static int Add(int step)
    {
        _calls++;
        _value += step;
        _history.Push(_value);
        return _value;
    }

    [LibraryFunction]
    public static int Value() { return _value; }

    /// <summary>The calls of Add so far, in this opener.</summary>
    [LibraryFunction]
    public static int Calls() { return _calls; }

    /// <summary>The value <paramref name="back"/> Add calls ago (0: the last one), or -1.</summary>
    [LibraryFunction]
    public static int Recent(int back) { return _history.Get(back); }

    [LibraryFunction]
    public static void Reset(int to)
    {
        _value = to;
        _calls = 0;
        _history = new History();
    }

    [LibraryFunction]
    public static bool IsEven(int x) { return (x & 1) == 0; }

    /// <summary>Nine numbers: the ninth travels in a0 (d0-d7 are taken).</summary>
    [LibraryFunction]
    public static int Sum9(int a, int b, int c, int d, int e, int f, int g, int h, int i)
    {
        return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6 + g * 7 + h * 8 + i * 9;
    }
}

/// <summary>The last eight values of a counter (an E# object inside the library: it never crosses the boundary).</summary>
public sealed class History
{
    private int[] _items = new int[8];
    private int _count;

    public void Push(int v)
    {
        _items[_count & 7] = v;
        _count++;
    }

    public int Get(int back)
    {
        if (back < 0 || back >= 8 || back >= _count) return -1;
        return _items[(_count - 1 - back) & 7];
    }
}
