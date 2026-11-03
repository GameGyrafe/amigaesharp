using System;
using System.Linq;
using ESharp;
using ESharp.Platform;

namespace LinqSample;

public struct Card
{
    public int Rank;
    public int Suit;
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static bool IsEven(int x) { return x % 2 == 0; }
    static int Square(int x) { return x * x; }

    [EntryPoint]
    public static void Main()
    {
        int[] a = { 5, 3, 8, 1, 9, 4 };

        Check(a.Count() == 6 && a.Count(x => x > 3) == 4, "Count");
        Check(a.Sum() == 30 && a.Sum(x => x * 2) == 60, "Sum");
        Check(a.Min() == 1 && a.Max() == 9 && a.Max(x => -x) == -1, "Min and Max");
        Check(a.Any() && a.Any(x => x == 8) && !a.Any(x => x > 100), "Any");
        Check(a.All(x => x > 0) && !a.All(x => x > 3), "All");
        Check(a.First() == 5 && a.First(x => x > 5) == 8 && a.Last() == 4 && a.Last(x => x < 5) == 4, "First and Last");
        Check(a.FirstOrDefault(x => x > 100) == 0 && a.LastOrDefault(x => x > 100) == 0, "FirstOrDefault, no match");
        Check(a.Contains(9) && !a.Contains(2), "Contains");

        Check(a.Where(x => x > 3).Sum() == 26, "Where then Sum");
        Check(a.Where(IsEven).Select(Square).Sum() == 80, "method groups");
        Check(a.Select(x => x + 1).Where(x => x % 3 == 0).Count() == 2, "Select then Where");
        Check(a.Where(x => x > 2).Select(x => x * 10).Max() == 90, "three steps");
        Check(a.Take(3).Sum() == 16 && a.Skip(4).Sum() == 13 && a.Skip(1).Take(2).Sum() == 11, "Take and Skip");
        Check(a.Where((x, i) => i % 2 == 0).Sum() == 22, "Where with an index");
        Check(a.Select((x, i) => x * i).Sum() == 78, "Select with an index");
        Check(a.Aggregate((p, q) => p * 2 + q) == 5 * 32 + 3 * 16 + 8 * 8 + 1 * 4 + 9 * 2 + 4 && a.Aggregate(100, (acc, x) => acc - x) == 70, "Aggregate");

        int limit = 4;
        Check(a.Where(x => x > limit).Count() == 3, "captures nothing: the lambda sees the locals");
        limit = 8;
        Check(a.Where(x => x >= limit).Count() == 2, "the lambda reads the current value");

        Check(Enumerable.Range(1, 5).Sum() == 15 && Enumerable.Range(10, 3).Max() == 12, "Enumerable.Range");
        Check(Enumerable.Range(1, 10).Where(x => x % 2 == 1).Select(x => x * x).Sum() == 165, "Range with steps");

        int total = 0;
        foreach (int v in a.Where(x => x > 3).Select(x => x * 2)) total += v;
        Check(total == 52, "foreach over a chain");
        total = 0;
        foreach (int v in Enumerable.Range(0, 100).Where(x => x % 7 == 0).Take(4)) { if (v == 14) continue; total += v; }
        Check(total == 0 + 7 + 21, "foreach over Range with continue and Take");

        Card[] hand = new Card[4];
        for (int i = 0; i < 4; i++) { hand[i].Rank = i + 2; hand[i].Suit = i % 2; }
        Check(hand.Where(c => c.Suit == 1).Select(c => c.Rank).Sum() == 3 + 5, "structs");
        Check(hand.Select(c => c.Rank * 10).Max() == 50, "struct projection");

        Check((from x in a where x > 3 select x * 2).Sum() == 52, "query: where and select");
        Check((from x in a let d = x * 2 where d > 10 select d).Count() == 2, "query: let");
        int qsum = 0;
        foreach (var x in from v in a where v < 5 select v) qsum += x;
        Check(qsum == 8, "foreach over a query");
        Check((from c in hand where c.Rank > 2 select c.Suit).Count() == 3, "query over structs");

        int outerBase = 100;
        Check(Enumerable.Range(1, 3).Select(i => a.Where(x => x > i * 3).Count() + outerBase).Sum() == 3 * 100 + 4 + 2, "a chain inside a lambda");
    }
}
