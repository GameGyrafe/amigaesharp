using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Positional, property and list patterns and switch statements with pattern labels (issue #12): compiled with host gcc and run.</summary>
public class CEmitterPatternTests
{
    private const string Check = @"static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }";

    private static void RunAllOk(string members, string body, int expected)
    {
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(CompileOk("using System;\n" + Program(members + "\n" + Check, body)));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(expected, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private const string Types = @"
public struct Pt
{
    public int X; public int Y;
    public Pt(int x, int y) { X = x; Y = y; }
    public void Deconstruct(out int x, out int y) { x = X; y = Y; }
    public int Sum { get { return X + Y; } }
}
public class Shape { public virtual int Area() { return 0; } }
public class Circle : Shape
{
    public int R;
    public Circle(int r) { R = r; }
    public void Deconstruct(out int r, out bool big) { r = R; big = R > 9; }
    public override int Area() { return 3 * R * R; }
}
public class Box : Shape
{
    public Pt Size;
    public int Hits;
    public Box(int w, int h) { Size = new Pt(w, h); }
    public int Width { get { Hits++; return Size.X; } }
    public override int Area() { return Size.X * Size.Y; }
}
public class Holder { public Pt P; public Box B; }
public static class Counter { public static int Calls; public static Pt Next() { Calls++; return new Pt(Calls, 2); } }
";

    [Fact]
    public void Positional_patterns_on_tuples_structs_and_classes_work_natively()
    {
        const string members = Types + @"
static string Quadrant(Pt p) => p switch
{
    (0, 0) => ""origin"",
    (> 0, > 0) => ""one"",
    (< 0, var y) when y > 0 => ""two"",
    (var x, var y) when x < 0 && y < 0 => ""three"",
    (_, _) => ""other"",
};
static int Kind(Shape s) => s switch
{
    Circle(var r, true) => 100 + r,
    Circle(var r, _) => r,
    Box { Size: (var w, var h) } when w == h => -w,
    Box { Size.X: var w } => -100 - w,
    null => -1,
    _ => 0,
};
";
        const string body = @"
var t = (1, ""a"");
Check(t is (1, ""a"") && t is (_, not ""b"") && !(t is (2, _)), ""tuple positional with constants"");
Check(t is (var n, var s) && n == 1 && s == ""a"", ""tuple positional binds"");
Check((3, 4) is (> 2 and < 5, 4), ""tuple literal subject, relational and"");
Check(new Pt(2, 3) is (2, var py) && py == 3, ""struct Deconstruct"");
Check(Quadrant(new Pt(0, 0)) == ""origin"" && Quadrant(new Pt(1, 2)) == ""one"" && Quadrant(new Pt(-1, 2)) == ""two""
    && Quadrant(new Pt(-1, -2)) == ""three"" && Quadrant(new Pt(-1, 0)) == ""other"", ""switch expression with positional arms"");
Shape c = new Circle(12);
Check(Kind(c) == 112 && Kind(new Circle(3)) == 3, ""class Deconstruct after a type test"");
Check(Kind(new Box(4, 4)) == -4 && Kind(new Box(4, 5)) == -104 && Kind(null) == -1 && Kind(new Shape()) == 0, ""nested property and positional"");
Counter.Calls = 0;
bool m = Counter.Next() is (1, 2) and (_, > 1);
Check(m && Counter.Calls == 1, ""subject evaluated once"");
Check(new Pt(5, 6) is var (a, b) && a == 5 && b == 6, ""var (a, b)"");
Check(c is Circle(12, true) whole && whole.R == 12, ""positional with a designation"");
";
        RunAllOk(members, body, 10);
    }

    [Fact]
    public void Property_patterns_on_properties_extended_names_and_null_work_natively()
    {
        const string members = Types;
        const string body = @"
Box b = new Box(7, 2);
Check(b is { Width: 7 } && b.Hits == 1, ""getter called once"");
Check(b is { Width: > 5 and < 9 } && b.Hits == 2, ""getter read once for an and pattern"");
Check(b is { Size.X: 7, Size.Y: 2 }, ""extended property pattern"");
Check(new Pt(1, 2) is { Sum: 3 }, ""struct property with a getter"");
Holder h = new Holder();
Check(!(h is { B.Size.X: 7 }), ""extended pattern stops at null"");
h.B = b;
Check(h is { B.Size.X: 7 } && h is { B: { Size: { Y: 2 } } }, ""extended pattern through a class field"");
Box none = null;
Check(!(none is { }) && b is { } && !(none is Box) && b is Box, ""{ } and a type test never match null"");
string s = ""abc"";
Check(s is { Length: 3 } && !(s is { Length: 2 }), ""string Length"");
";
        RunAllOk(members, body, 8);
    }

    [Fact]
    public void List_patterns_over_arrays_spans_strings_and_vec_work_natively()
    {
        const string members = @"
static int First(Span<int> xs) => xs switch
{
    [] => -1,
    [var only] => only,
    [var a, var b] => a * 10 + b,
    [var f, .., var l] => f * 100 + l,
};
static int MiddleSum(ReadOnlySpan<int> xs)
{
    if (xs is [_, .. var mid, _])
    {
        int s = 0;
        for (int i = 0; i < mid.Length; i++) s += mid[i];
        return s;
    }
    return -1;
}
";
        const string body = @"
int[] arr = { 1, 2, 3, 4 };
Check(arr is [1, 2, 3, 4] && arr is [1, ..] && arr is [.., 4] && arr is [_, 2, ..] && !(arr is [1, 2]), ""array list patterns"");
Check(arr is [var f, .., var l] && f == 1 && l == 4, ""array first and last"");
int[] one = { 5 };
Span<int> all = arr;
Span<int> empty = all.Slice(0, 0);
int[] two = { 5, 6 };
Check(First(arr) == 104 && First(one) == 5 && First(two) == 56 && First(empty) == -1, ""span switch expression"");
Check(MiddleSum(arr) == 5 && MiddleSum(one) == -1, ""slice of a span"");
string s = ""hey"";
Check(s is ['h', .., 'y'] && !(s is ['x', ..]), ""string list pattern"");
Vec<int> v = new Vec<int>(4, false);
v.Add(7); v.Add(8); v.Add(9);
Check(v is [7, _, 9] && v is [.., > 8] && !(v is [7]), ""Vec list pattern"");
Check(v is [var a, .. , var z] && a == 7 && z == 9 && v.Count is 3, ""Vec bindings"");
v.Destroy();
Check(v is [], ""empty Vec"");
";
        RunAllOk(members, body, 8);
    }

    [Fact]
    public void Switch_statements_with_pattern_labels_work_natively()
    {
        const string members = Types + @"
static int Classify(Shape s)
{
    switch (s)
    {
        case Circle { R: > 10 }:
            return 2;
        case Circle(var r, _) when r == 0:
            return 0;
        case Circle:
            return 1;
        case null:
            return -1;
        default:
            return 5;
    }
}
static int Grid(int x, int y)
{
    int r = 0;
    switch (x, y)
    {
        case (0, 0):
            r = 1;
            break;
        case (var a, 0) when a > 0:
            r = 2;
            break;
        case (_, var a):
            r = 30 + a;
            break;
    }
    return r;
}
static int Head(Span<int> xs)
{
    switch (xs)
    {
        case []: return -1;
        case [var h, ..] when h < 0: return 0;
        case [var h, ..]: return h;
    }
    return -2;
}
";
        const string body = @"
Check(Classify(new Circle(11)) == 2 && Classify(new Circle(0)) == 0 && Classify(new Circle(5)) == 1, ""class patterns in a switch statement"");
Check(Classify(null) == -1 && Classify(new Box(2, 3)) == 5, ""null and default"");
Check(Grid(0, 0) == 1 && Grid(3, 0) == 2 && Grid(-3, 0) == 30 && Grid(1, 4) == 34, ""tuple switch statement with when"");
int[] xs = { 6, 1 };
Span<int> some = xs;
Span<int> empty = some.Slice(0, 0);
Check(Head(xs) == 6 && Head(empty) == -1, ""list patterns in a switch statement"");
int k = 3;
string w = """";
switch (k)
{
    case < 0: w = ""neg""; break;
    case 0 or 1: w = ""small""; break;
    case > 1 and < 10: w = ""mid""; break;
    default: w = ""big""; break;
}
Check(w == ""mid"", ""relational case labels"");
";
        RunAllOk(members, body, 5);
    }

    [Fact]
    public void Out_variables_in_when_clauses_are_declared()
    {
        const string members = @"
static bool TryHalf(int v, out int h) { h = v / 2; return v % 2 == 0; }
static int Stmt(int x)
{
    switch (x)
    {
        case > 0 when TryHalf(x, out var n): return n;
        case > 0 when TryHalf(x + 1, out var n): return 100 + n;
        case var _ when TryHalf(x, out _): return -1;
        default: return -2;
    }
}
static int Expr(int x) => x switch
{
    > 0 when TryHalf(x, out var n) => n,
    > 0 when TryHalf(x + 1, out int m) => 100 + m,
    _ => -2,
};
";
        const string body = @"
Check(Stmt(8) == 4 && Stmt(7) == 104 && Stmt(0) == -1 && Stmt(-3) == -2, ""out var in a switch statement when clause"");
Check(Expr(8) == 4 && Expr(7) == 104 && Expr(-3) == -2, ""out var in a switch expression when clause"");
";
        RunAllOk(members, body, 2);
    }

    [Fact]
    public void Patterns_sample_compiles_and_runs_natively()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "patterns", "Patterns.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(23, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData("static int[] A = { 1, 2, 3 };", "bool b = A is [_, .. var m];")]
    [InlineData("static bool F(string s) => s is [_, .. var m];", "")]
    [InlineData("static bool F(string s) => s is [_, .. ['b', ..]];", "")]
    public void Slices_that_would_copy_report_ES0099(string members, string body)
    {
        var r = Compile(Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
    }
}
