using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

public class CEmitterTests
{
    [Fact]
    public void Hello_world_emits_a_dos_call_and_main()
    {
        var c = CompileOk(Program("", "Dos.PutStr(\"Hello from E#!\\n\");"));
        Assert.Contains("#include <proto/dos.h>", c);
        Assert.Contains("PutStr(\"Hello from E#!\\n\");", c);
        Assert.Contains("int main(void)", c);
        Assert.Contains("Es_P_Main();", c);
    }

    [Fact]
    public void Output_is_stable_between_runs()
    {
        var src = Program("static int Add(int a, int b) { return a + b; }", "Add(1, 2);");
        Assert.Equal(CompileOk(src), CompileOk(src));
    }

    [Fact]
    public void Line_directives_point_back_to_the_cs_file()
    {
        var c = CompileOk(Program("static int Add(int a, int b) { return a + b; }"));
        Assert.Contains("#line ", c);
        Assert.Contains("\"test.cs\"", c);
    }

    [Theory]
    [InlineData("", "object k = 1; int r = 0; switch (k) { case int i when i > 0: r = i; break; default: r = -1; break; }")]
    [InlineData("", "object k = true; if (k is bool b) { }")]
    [InlineData("", "object k = 'c'; if (k is char c) { }")]
    [InlineData("enum E { A }", "object k = E.A; if (k is E) { }")]
    [InlineData("struct S { public int V; }", "S s = default; object k = s; if (k is S) { }")]
    [InlineData("static object F;", "F = 3;")]
    [InlineData("static int G(object o) { return 0; }", "G(5);")]
    [InlineData("static object H() { return 2; }", "H();")]
    [InlineData("", "object[] a = new object[2];")]
    [InlineData("", "int i = 3; System.ValueType v = i;")]
    [InlineData("", "object k = 1; int j = (int)k;")]
    [InlineData("class C { }", "object o = new C();")]
    public void Object_and_boxing_are_refused_with_ES0099_never_invalid_C(string members, string body)
    {
        var r = Compile(Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && (d.GetMessage().Contains("no object type") || d.GetMessage().Contains("boxing")));
        Assert.False(r.Success);
    }

    [Fact]
    public void Types_map_to_stdint()
    {
        var c = CompileOk(Program(@"static void F(byte a, sbyte b, short c, ushort d, int e, uint f) { }"));
        Assert.Contains("void Es_P_F(uint8_t a, int8_t b, int16_t c, uint16_t d, int32_t e, uint32_t f)", c);
    }

    [Fact]
    public void Overloads_get_distinct_names()
    {
        var c = CompileOk(Program("static int F(int a) { return a; } static int F(short a) { return a; }", "F(1); F((short)2);"));
        Assert.Contains("Es_P_F_int", c);
        Assert.Contains("Es_P_F_short", c);
    }

    [Fact]
    public void Unsupported_constructs_report_ES0099()
    {
        var r = Compile(Program("static void F() { goto end; end: ; }"));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Fact]
    public void String_literals_escape_correctly()
    {
        var c = CompileOk(Program("", "Dos.PutStr(\"a\\\"b\\\\c\\t\\u00e9\");"));
        Assert.Contains("\"a\\\"b\\\\c\\t\\351\"", c);
    }

    // ---- end to end: generated C is compiled and run with the host gcc ----

    [Fact]
    public void Hello_world_runs_natively()
    {
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(CompileOk(Program("", "Dos.PutStr(\"Hello from E#!\\n\");")));
        Assert.Equal(0, code);
        Assert.Equal("Hello from E#!\n", output);
    }

    [Fact]
    public void Structs_enums_loops_and_statics_run_natively()
    {
        if (!HasGcc) return;
        const string members = @"
public enum Suit : byte { Clubs, Diamonds = 5, Hearts }
public struct Card { public Suit Suit; public int Rank; }
public struct Hand { public Card First; public Card Second; public int Count; }

const int Max = 4;
static int counter = 10;

static int Sum(int n)
{
    int total = 0;
    for (int i = 1; i <= n; i++) { total += i; }
    return total;
}

static int Score(Card c)
{
    switch (c.Suit)
    {
        case Suit.Clubs: return c.Rank;
        case Suit.Hearts: return c.Rank * 2;
        default: return 0;
    }
}

static string Pick(int v) => v > 3 ? ""big\n"" : ""small\n"";

static int Fib(int n) { return n < 2 ? n : Fib(n - 1) + Fib(n - 2); }
";
        const string body = @"
Card a = new Card { Suit = Suit.Hearts, Rank = 7 };
Hand h = default;
h.First = a;
h.Count++;
counter += Sum(Max);
int w = 0;
while (w < 3) { w++; }
do { w--; } while (w > 1);
if (Score(a) == 14 && h.Count == 1) Dos.PutStr(""score ok\n"");
else if (w == 1) Dos.PutStr(""wrong branch\n"");
else Dos.PutStr(""bad\n"");
if (counter == 20) Dos.PutStr(""counter ok\n"");
if (Fib(10) == 55) Dos.PutStr(""fib ok\n"");
Dos.PutStr(Pick(Max));
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("score ok\ncounter ok\nfib ok\nsmall\n".Replace("small", "big"), output);
    }

    [Fact]
    public void Entry_point_return_value_becomes_exit_code()
    {
        if (!HasGcc) return;
        var src = Prelude + "public static class P { [EntryPoint] public static int Main() { return 3; } }";
        var (code, _) = RunWithHostGcc(CompileOk(src));
        Assert.Equal(3, code);
    }

    private const string AtExitSource = Prelude + @"
public static class Res
{
    static int _n;
    public static void Use() { _n++; Dos.PutStr(""use\n""); }
    [AtExit] static void Bye() { Dos.PutStr(_n == 1 ? ""bye\n"" : ""bad\n""); }
}
public static class OnlyExit { [AtExit] static void Never() { Dos.PutStr(""never\n""); } }
";

    [Fact]
    public void AtExit_methods_run_after_Main_when_their_class_is_used()
    {
        var c = CompileOk(AtExitSource + "public static class P { [EntryPoint] public static void Main() { Res.Use(); Dos.PutStr(\"main\\n\"); } }");
        Assert.DoesNotContain("Never();", c);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("use\nmain\nbye\n", output);
    }

    [Fact]
    public void AtExit_keeps_the_entry_point_return_value()
    {
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(CompileOk(AtExitSource + "public static class P { [EntryPoint] public static int Main() { Res.Use(); return 4; } }"));
        Assert.Equal(4, code);
        Assert.Equal("use\nbye\n", output);
    }

    [Fact]
    public void Frame_closes_its_timer_after_Main_only_when_used()
    {
        var used = CompileOk(Program("", "Frame.WaitVBlank();"));
        var main = used[used.IndexOf("int main(void)", StringComparison.Ordinal)..];
        Assert.Contains("Frame_CloseTimer();", main);
        Assert.DoesNotContain("CloseTimer", CompileOk(Program("", "Dos.PutStr(\"hi\\n\");")));
    }

    // ---- phase 2: arrays, spans, fixed point ----

    private static string Phase2Source =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "phase2", "Phase2.cs"));

    [Fact]
    public void Phase2_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(CompileOk(Phase2Source));
        Assert.Equal(0, code);
        Assert.Equal(Phase2Expected.Output, output);
    }

    [Fact]
    public void Properties_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "properties", "Properties.cs");
        var (code, output) = RunWithHostGcc(CompileOk(File.ReadAllText(path)));
        Assert.Equal(0, code);
        Assert.Equal(PropertiesExpected.Output, output);
    }

    [Fact]
    public void Operators_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "operators", "Operators.cs");
        var (code, output) = RunWithHostGcc(CompileOk(File.ReadAllText(path)));
        Assert.Equal(0, code);
        Assert.Equal(OperatorsExpected.Output, output);
    }

    [Fact]
    public void Screen_sample_compiles_to_graphics_library_calls()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "screen");
        var sources = new[] { "Screen.cs", "Assets.g.cs" }
            .Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var c = r.C!;
        Assert.Contains("#include <proto/intuition.h>", c);
        Assert.Contains("#include <proto/graphics.h>", c);
        Assert.Contains("OpenScreenTagList(NULL,", c);
        Assert.Contains("BltBitMapRastPort(", c);
        Assert.Contains("__chip", c);          // the card bitplanes live in chip RAM
        Assert.Equal(2112, r.Chip.Single().Bytes);
    }

    [Fact]
    public void Ref_out_and_in_parameters_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
struct V { public int A; public int B; }
static void Inc(ref int v) { v++; v += 2; }
static bool TryHalf(int n, out int half) { if (n % 2 != 0) { half = 0; return false; } half = n / 2; return true; }
static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }
static int Sum(in V v) { return v.A + v.B; }
static void Fill(out V v, int a) { v = default; v.A = a; v.B = a * 2; }
static void Twice(ref int v) { Inc(ref v); Inc(ref v); }
static int[] Arr = new int[3];
static int Field;
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
int n = 1; Inc(ref n); Check(n == 4, ""ref local"");
if (TryHalf(10, out int h)) Check(h == 5, ""out declaration in if"");
if (TryHalf(7, out var h2)) Check(false, ""odd""); else Check(h2 == 0, ""out var in else"");
if (TryHalf(3, out int h3)) { Check(false, ""no""); } else if (TryHalf(8, out int h4)) { Check(h4 == 4, ""else if out""); }
int a = 1, b = 2; Swap(ref a, ref b); Check(a == 2 && b == 1, ""swap"");
V v; Fill(out v, 5); Check(Sum(v) == 15, ""struct out and in"");
Check(Sum(new V()) == 0, ""in with a temporary"");
Arr[1] = 3; Inc(ref Arr[1]); Check(Arr[1] == 6, ""ref array element"");
Inc(ref Field); Check(Field == 3, ""ref static field"");
Twice(ref n); Check(n == 10, ""pass a ref parameter on"");
TryHalf(4, out _); TryHalf(4, out int _); Check(true, ""discards"");
V w = default; Inc(ref w.A); Check(w.A == 3, ""ref struct field"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(12, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Patterns_and_switch_expressions_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
enum Color { Red, Green, Blue }
struct Pt { public int X; public int Y; }
static int Calls;
static int Next() { Calls++; return 7; }
static int Classify(int n) => n switch { < 0 => -1, 0 => 0, > 0 and < 10 => 1, >= 10 and < 100 => 2, _ => 3 };
static int Name(Color c) => c switch { Color.Red => 1, Color.Green => 2, _ => 3 };
static int Weight(Pt p) => p switch { { X: 0, Y: 0 } => 0, { X: > 5 } => 2, { Y: var y } when y > 3 => y, _ => 1 };
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Check(Classify(-5) == -1 && Classify(0) == 0 && Classify(5) == 1 && Classify(50) == 2 && Classify(500) == 3, ""relative and logical patterns"");
Check(Name(Color.Green) == 2 && Name(Color.Blue) == 3 && Name(Color.Red) == 1, ""enum arms"");
Check(Weight(new Pt()) == 0 && Weight(new Pt { X = 9 }) == 2 && Weight(new Pt { Y = 8 }) == 8 && Weight(new Pt { Y = 1 }) == 1, ""property patterns with when and var"");
int n = 5;
Check(n is > 3 and < 9, ""is relational"");
Check(!(n is 1 or 2 or 3), ""is or"");
Check(n is not 4, ""is not"");
Check(n is int m && m == 5, ""is declaration"");
Check(Next() is 7 && Calls == 1, ""subject evaluated once"");
Pt q = new Pt { X = 3 };
if (q is { X: 3 } found && found.X == 3) Check(true, ""is property with designation"");
int s = n switch { 5 => 50, _ => 0 };
Check(s == 50, ""switch expression assigned"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(10, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Tuples_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
struct Pt { public int X; public int Y; }
static (int, int) MinMax(int a, int b) { return a < b ? (a, b) : (b, a); }
static (int Sum, int Product) Both(int a, int b) => (a + b, a * b);
static (Pt, bool) Make(int x) { Pt p = new Pt { X = x }; return (p, x > 0); }
static int Use((int, int) t) { return t.Item1 * 10 + t.Item2; }
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
var mm = MinMax(9, 4);
Check(mm.Item1 == 4 && mm.Item2 == 9, ""return and Item fields"");
var both = Both(3, 5);
Check(both.Sum == 8 && both.Product == 15, ""named elements"");
(int lo, int hi) = MinMax(7, 2);
Check(lo == 2 && hi == 7, ""deconstruction declaration"");
var (s, p) = Both(2, 6);
Check(s == 8 && p == 12, ""var deconstruction"");
int a = 1, b = 2;
(a, b) = (b, a);
Check(a == 2 && b == 1, ""swap"");
(int, int) t = (3, 4);
Check(Use(t) == 34 && Use((5, 6)) == 56, ""tuple parameters"");
var (pt, ok) = Make(5);
Check(pt.X == 5 && ok, ""tuple holding a struct"");
(int first, _) = MinMax(8, 1);
Check(first == 1, ""discard in deconstruction"");
long big = 0; int small = 0;
(big, small) = (5, 6);
Check(big == 5 && small == 6, ""conversions in assignment"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(9, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Local_functions_that_capture_nothing_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
static int Run(int n)
{
    int Fact(int k) { return k <= 1 ? 1 : k * Fact(k - 1); }
    int Twice(int k) => k * 2;
    static int Square(int k) { return k * k; }
    return Fact(n) + Twice(n) + Square(n);
}
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Check(Run(4) == 24 + 8 + 16, ""local functions"");
int Add(int a, int b) { return a + b; }
Check(Add(2, 3) == 5, ""local function in the entry point"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("ok local functions\nok local function in the entry point\n", output);
    }

    [Fact]
    public void Out_variable_declared_in_an_if_condition_stays_in_scope_after_the_if()
    {
        if (!HasGcc) return;
        const string members = @"
static bool TryHalf(int n, out int half) { half = n / 2; return n % 2 == 0; }
static int Run(int n)
{
    if (!TryHalf(n, out int h)) return -1;
    int twice = h * 2;
    if (TryHalf(h, out int q) && q > 0) twice += q;
    return twice + h + q;
}
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Check(Run(7) == -1, ""early return"");
Check(Run(8) == 8 + 2 + 4 + 2, ""variables used after their if"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("ok early return\nok variables used after their if\n", output);
    }

    [Fact]
    public void Heap_classes_and_static_dispatch_interfaces_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
class Node
{
    public int Value;
    public Node Next;
    public Node(int v) { Value = v; }
    public int Sum() { int s = Value; if (Next != null) s += Next.Sum(); return s; }
}
class Counter
{
    public int Count { get; set; }
    public void Hit() { Count++; }
    public Counter Self() { return this; }
    public static int Made;
    public static Counter Make() { Made++; return new Counter(); }
}
interface IScorer { int Score(int card); }
struct DoubleScorer : IScorer { public int Score(int card) { return card * 2; } }
class FlatScorer : IScorer
{
    public int Bonus;
    public int Score(int card) { return card + Bonus; }
}
static int Total<T>(ref T scorer, int n) where T : IScorer
{
    int sum = 0;
    for (int i = 1; i <= n; i++) sum += scorer.Score(i);
    return sum;
}
static Node Head;
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Node a = new Node(1);
a.Next = new Node(2);
a.Next.Next = new Node(3);
Check(a.Sum() == 6, ""linked objects and methods"");
Node b = a;
Check(b == a && a.Next != null && a.Next.Next.Next == null && a.Next.Next.Next is null, ""references compare and null"");
b.Value = 10;
Check(a.Value == 10, ""assignment copies the reference, not the object"");
Head = a;
Check(Head.Sum() == 15, ""static field holding an object"");
Counter c = new Counter { Count = 5 };
c.Hit(); c.Self().Hit();
Check(c.Count == 7, ""properties, this and object initializer"");
Counter made = Counter.Make();
Check(Counter.Made == 1 && made.Count == 0, ""static members of a class"");
Node[] nodes = new Node[3];
nodes[1] = new Node(7);
Check(nodes[0] == null && nodes[1].Value == 7, ""array of references starts null"");
DoubleScorer ds = default;
FlatScorer fs = new FlatScorer { Bonus = 100 };
Check(Total(ref ds, 3) == 12 && Total(ref fs, 3) == 306, ""interface constraint: struct and class, resolved statically"");
Heap.Free(ref a);
Check(a == null, ""Heap.Free clears the variable"");
Heap.Free(ref a);
Check(a == null, ""freeing null again is harmless"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal(string.Concat(new[] { "linked objects and methods", "references compare and null", "assignment copies the reference, not the object",
            "static field holding an object", "properties, this and object initializer", "static members of a class", "array of references starts null",
            "interface constraint: struct and class, resolved statically", "Heap.Free clears the variable", "freeing null again is harmless" }.Select(n => "ok " + n + "\n")), output);
    }

    [Fact]
    public void String_interpolation_and_concatenation_format_into_static_buffers()
    {
        if (!HasGcc) return;
        const string members = @"
static void Say(string s) { Dos.PutStr(s); Dos.PutStr(""\n""); }
static string Name(int i) { return i == 0 ? ""zero"" : ""many""; }
";
        const string body = @"
int n = 42;
string name = ""Ann"";
byte small = 7;
uint big = 4000000000;
Say($""Hi {name}, n={n,5}|{n,-5}|{n:D5}|{255:X}|{-7}|{true}|{'x'}"");
Say($""{small} {big} {int.MinValue} {-5:D3} {0xBEEF:x} {Name(0)}-{Name(1)}"");
Say(""a"" + n + ""b"" + name + '!');
Say($""no holes"");
Say($""{{braces}} {n}"");
string first = $""one {1}"";
string second = $""two {2}"";
Say(first + ""/"" + second);
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("Hi Ann, n=   42|42   |00042|FF|-7|True|x\n" +
                     "7 4000000000 -2147483648 -005 beef zero-many\n" +
                     "a42bAnn!\n" +
                     "no holes\n" +
                     "{braces} 42\n" +
                     "one 1/two 2\n", output);
    }

    [Fact]
    public void Interface_typed_values_dispatch_dynamically_to_classes()
    {
        if (!HasGcc) return;
        const string members = @"
interface IRules
{
    int Score(int card);
    bool Allowed(int card, int limit);
}
class Flat : IRules
{
    public int Score(int card) { return card; }
    public bool Allowed(int card, int limit) { return card <= limit; }
}
class Doubler : IRules
{
    public int Calls;
    public int Score(int card) { Calls++; return card * 2; }
    public bool Allowed(int card, int limit) { return true; }
}
class Table
{
    public IRules Rules;
    public int Play(int card) { return Rules.Allowed(card, 5) ? Rules.Score(card) : -1; }
}
static IRules Pick(bool fancy) { if (fancy) return new Doubler(); return new Flat(); }
static int Total(IRules r, int n) { int t = 0; for (int i = 1; i <= n; i++) t += r.Score(i); return t; }
static IRules Chosen;
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
IRules a = Pick(false);
IRules b = Pick(true);
Check(Total(a, 3) == 6 && Total(b, 3) == 12, ""the same code runs different classes"");
IRules same = a;
Check(a != null && a != b && a == same, ""interface values compare by object"");
IRules none = null;
Check(none == null && none is null && !(a is null), ""null interface value"");
Table t = new Table();
t.Rules = b;
Check(t.Play(3) == 6 && t.Play(9) == 18, ""interface field of a class"");
t.Rules = a;
Check(t.Play(3) == 3 && t.Play(9) == -1, ""switching the object behind a field"");
Chosen = new Doubler();
Check(Chosen.Score(4) == 8, ""interface in a static field"");
IRules fresh = default;
Check(fresh == null, ""default is null"");
Heap.Free(ref a);
Check(a == null, ""Heap.Free on an interface value"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal(string.Concat(new[] { "the same code runs different classes", "interface values compare by object", "null interface value",
            "interface field of a class", "switching the object behind a field", "interface in a static field", "default is null",
            "Heap.Free on an interface value" }.Select(n => "ok " + n + "\n")), output);
    }

    [Fact]
    public void Unsupported_class_and_interface_forms_are_reported()
    {
        var inherit = Compile(Program("class B : System.Random { }"));
        Assert.Contains("ES0007", ErrorIds(inherit));
        var virt = Compile(Program("class A { public virtual int F { get; set; } }"));
        Assert.Contains("ES0007", ErrorIds(virt));
        var boxing = Compile(Program("interface I { int F(); } struct S : I { public int F() { return 1; } } static int Use(I x) { return x.F(); }", "S s = default; Use(s);"));
        Assert.Contains("ES0099", ErrorIds(boxing));
        var property = Compile(Program("interface I { int P { get; } }"));
        Assert.Contains("ES0009", ErrorIds(property));
        var generic = Compile(Program("class Box<T> { public T V; }"));
        Assert.Contains("ES0008", ErrorIds(generic));
    }

    [Fact]
    public void Lambdas_and_method_groups_become_function_pointers()
    {
        if (!HasGcc) return;
        const string members = @"
delegate int Combine(int a, int b);
struct Card { public int Rank; public int Suit; }
static int Triple(int x) { return x * 3; }
static int CountIf(System.ReadOnlySpan<Card> cards, System.Func<Card, bool> test)
{
    int n = 0;
    for (int i = 0; i < cards.Length; i++) if (test(cards[i])) n++;
    return n;
}
static int Fold(System.ReadOnlySpan<int> xs, int start, Combine f)
{
    int acc = start;
    for (int i = 0; i < xs.Length; i++) acc = f(acc, xs[i]);
    return acc;
}
static void Each(System.Span<int> xs, System.Action<int> act) { for (int i = 0; i < xs.Length; i++) act(xs[i]); }
static int Sum;
static void Add(int v) { Sum += v; }
static int Apply(System.Func<int, int> f, int v) { return f.Invoke(v); }
static System.Func<int, int> Pick(bool dbl) { if (dbl) return x => x * 2; return Triple; }
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Card[] hand = new Card[4];
for (int i = 0; i < 4; i++) { hand[i].Rank = i + 1; hand[i].Suit = i % 2; }
Check(CountIf(hand, c => c.Suit == 1) == 2, ""expression lambda as a predicate"");
Check(CountIf(hand, (Card c) => { return c.Rank > 2; }) == 2, ""block lambda"");
int[] xs = new int[3]; xs[0] = 1; xs[1] = 2; xs[2] = 3;
Check(Fold(xs, 0, (a, b) => a + b) == 6, ""user delegate type with two parameters"");
Check(Fold(xs, 1, (a, b) => a * b) == 6, ""second lambda gets its own function"");
Sum = 0;
Each(xs, Add);
Check(Sum == 6, ""method group converted to Action"");
Each(xs, v => Sum += v * 10);
Check(Sum == 66, ""lambda that assigns a static field"");
System.Func<int, int> f = Triple;
Check(f(4) == 12 && Apply(f, 5) == 15, ""delegate variable, call and Invoke"");
Check(Pick(true)(7) == 14 && Pick(false)(7) == 21, ""delegate returned from a method"");
System.Func<int> zero = () => 0;
Check(zero() == 0, ""lambda without parameters"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal(string.Concat(new[] { "expression lambda as a predicate", "block lambda", "user delegate type with two parameters",
            "second lambda gets its own function", "method group converted to Action", "lambda that assigns a static field",
            "delegate variable, call and Invoke", "delegate returned from a method", "lambda without parameters" }.Select(n => "ok " + n + "\n")), output);
    }

    [Fact]
    public void Lambda_that_captures_a_local_and_is_returned_or_stored_is_a_heap_closure()
    {
        // Before issue #41 both were ES0099; they are heap closures now (run in HeapClosureTests).
        var r = Compile(Program("static System.Func<int, int> Make(int k) { return x => x + k; }", "System.Func<int, int> f = Make(3); Dos.PutStr(f(1) == 4 ? \"y\" : \"n\"); Heap.Free(ref f);"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("es_closure_retain", r.C);
        r = Compile(Program("static System.Func<int, int> Held;", "int k = 3; Held = x => x + k; Heap.Free(ref Held);"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        // A borrowed (stack) closure assigned to a local declared earlier is still not supported.
        r = Compile(Program("static int Call(System.Func<int> f) => f();", "int k = 3; System.Func<int> g = null; g = () => k; Dos.PutStr(Call(g) == 3 ? \"y\" : \"n\");"));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Theory]
    [InlineData("int[] a = new int[3]; var q = System.Linq.Enumerable.Where(a, x => x > 1);")]            // written as a plain call
    [InlineData("int[] a = new int[3]; var q = a.Where(x => x > 1); Dos.PutStr(q.Count() == 0 ? \"y\" : \"n\");")]   // a chain that is kept
    [InlineData("int[] a = new int[3]; int n = a.Where(x => { int y = x; y++; return y > 1; }).Count();")]      // a statement lambda
    [InlineData("System.ReadOnlySpan<int> a = new int[3]; int n = a.Sum();")]                             // not an array
    [InlineData("int[] a = new int[3]; var s = a.OrderBy(x => x);")]                                      // a sorted sequence that is kept
    [InlineData("string[] a = new string[3]; int n = a.OrderBy(x => x).Count();")]                        // a sort key that is not a number
    [InlineData("int[] a = new int[3]; int n = (from x in a let d = x * 2 orderby x select d).Count();")] // a let that would not survive the sort
    public void Linq_forms_that_cannot_be_loops_report_an_error(string body)
    {
        var r = Compile("using System.Linq;\n" + Program("", body));
        Assert.True(!r.Success, "should not compile");
    }

    [Fact]
    public void Local_function_that_captures_a_local_reports_ES0099()
    {
        var r = Compile(Program("", "int x = 3; int Get() { return x; } Dos.PutStr(Get() == 3 ? \"y\" : \"n\");"));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Fact]
    public void Interpolated_strings_format_long_ulong_and_enums()
    {
        if (!HasGcc) return;
        const string members = @"
enum Mode { Idle, Run = 5, Stop }
enum Big : long { A = 1, B = 5000000000 }
static void Say(string s) { Dos.PutStr(s); Dos.PutStr(""\n""); }
";
        const string body = @"
long a = -5000000000; ulong b = 18446744073709551615; long c = 0;
Mode m = Mode.Run; Mode odd = (Mode)42; Big big = Big.B;
Say($""{a} {b} {c} {a,22}|{a:D12}|{b:X}"");
Say($""{m} {Mode.Stop} {odd} {big} {m,6}|{m:D}|{(Mode)0}"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("-5000000000 18446744073709551615 0            -5000000000|-005000000000|FFFFFFFFFFFFFFFF\n" +
                     "Run Stop 42 B    Run|5|Idle\n", output);
    }

    [Fact]
    public void Fix_multiply_and_divide_match_the_64_bit_definition_natively()
    {
        if (!HasGcc) return;
        const string body = @"
int[] v = { 65536, -65536, 98304, -98304, 1, -1, 32767, 12345678, -12345678, 2147483647, -2147483647 - 1, 40000, 7, 262144, -3 };
int sum = 0;
for (int i = 0; i < 15; i++)
    for (int j = 0; j < 15; j++)
    {
        Fix a = Fix.FromRaw(v[i]); Fix b = Fix.FromRaw(v[j]);
        sum = sum * 31 + (a * b).Raw;
        if (b.Raw != 0) sum = sum * 31 + (a / b).Raw;
    }
Dos.PutInt(sum);
";
        var (code, output) = RunWithHostGcc(CompileOk(Program("", body)));
        Assert.Equal(0, code);
        Assert.Equal("37706025", output.Trim());   // computed independently (exact integer arithmetic), including quotients that overflow 32 bits
    }

    [Fact]
    public void Static_constructors_and_this_initializers_work_natively()
    {
        if (!HasGcc) return;
        const string members = @"
static class Table
{
    public static int[] Squares = new int[5];
    public static int Count = 1;
    static Table() { for (int i = 0; i < Squares.Length; i++) Squares[i] = i * i; Count = Squares.Length; }
}
struct Box
{
    public int W; public int H; public int Tag;
    public Box(int w, int h) { W = w; H = h; Tag = 1; }
    public Box(int side) : this(side, side) { Tag += 10; }
    public Box(bool unit) : this() { W = unit ? 1 : 0; }
}
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Check(Table.Count == 5 && Table.Squares[4] == 16, ""static constructor runs before Main"");
Box b = new Box(3);
Check(b.W == 3 && b.H == 3 && b.Tag == 11, ""this(...) initializer runs first"");
Box u = new Box(true);
Check(u.W == 1 && u.H == 0 && u.Tag == 0, ""this() initializer"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.Equal("ok static constructor runs before Main\nok this(...) initializer runs first\nok this() initializer\n", output);
    }

    [Fact]
    public void List_of_structs_grows_inserts_and_removes_natively()
    {
        if (!HasGcc) return;
        const string members = @"
public struct Item { public int Id; public int Weight; }
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
static int Total(System.Span<Item> items) { int t = 0; foreach (Item it in items) t += it.Weight; return t; }
";
        const string body = @"
Vec<Item> list = default;
for (int i = 0; i < 10; i++) { Item it = new Item { Id = i, Weight = i * 2 }; list.Add(it); }
Check(list.Count == 10 && list.Capacity >= 10, ""grows from nothing"");
Check(list[3].Id == 3 && list[9].Weight == 18, ""indexer read"");
Item changed = list[2]; changed.Weight = 100; list[2] = changed;
Check(list[2].Weight == 100, ""indexer write"");
list.Insert(0, new Item { Id = 77 });
Check(list.Count == 11 && list[0].Id == 77 && list[1].Id == 0, ""insert at the front"");
list.RemoveAt(0);
Check(list.Count == 10 && list[0].Id == 0, ""remove keeps order"");
list.SwapRemoveAt(0);
Check(list.Count == 9 && list[0].Id == 9, ""swap remove"");
Check(list[50].Id == 0 && !list.RemoveAt(50) && !list.Insert(99, changed), ""out of range is harmless"");
Check(Total(list.AsSpan()) == 186, ""span over the items"");
list.Destroy();
Check(list.Count == 0, ""destroy"");
";
        var (code, output) = RunWithHostGcc(CompileOk(Program(members, body)));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(9, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Null_passed_as_a_span_is_the_empty_span()
    {
        if (!HasGcc) return;
        var c = CompileOk(Program(
            "static int Len(System.ReadOnlySpan<byte> s) { return s.Length; }",
            "if (Len(null) == 0) Dos.PutStr(\"empty\\n\");"));
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("empty\n", output);
    }

    [Fact]
    public void Cards_sample_compiles_to_masked_blits_and_input_polling()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "cards");
        var sources = new[] { "HigherLower.cs", "Program.cs", "Assets.g.cs" }
            .Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("BltMaskBitMapRastPort(", r.C!);
        Assert.Contains("GetMsg(", r.C!);
        Assert.Contains("ReplyMsg(", r.C!);
        Assert.Equal(44800, r.Chip.Sum(c => c.Bytes));
    }

    [Fact]
    public void Rummy_sample_compiles_for_m68k()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "rummy");
        var sources = new[] { "Game.cs", "Program.cs", "Assets.g.cs" }
            .Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("BltMaskBitMapRastPort(", r.C!);
    }

    [Fact]
    public void Sound_sample_compiles_for_m68k_and_calls_audio_device()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "sound", "Sound.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("OpenDevice(", r.C!);
        Assert.Contains("BeginIO(", r.C!);
        Assert.Contains("#include <clib/alib_protos.h>", r.C!);
    }

    [Fact]
    public void Collections_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "collections", "Collections.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(CollectionsExpected.Output, output);
    }

    [Fact]
    public void Inherit_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "inherit", "Inherit.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(InheritExpected.Output, output);
    }

    [Fact]
    public void Lambdas_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "lambdas", "Lambdas.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(LambdasExpected.Output, output);
    }

    [Fact]
    public void Refs_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "refs", "Refs.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(RefsExpected.Output, output);
    }

    [Fact]
    public void Linq_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "linq", "Linq.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(LinqExpected.Output, output);
    }

    [Fact]
    public void Assets_sample_compiles_for_m68k_with_chip_data_for_images_and_the_sample()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "assets");
        var sources = new[] { "AssetsSample.cs", "Assets.g.cs" }
            .Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("__chip", r.C!);
        Assert.Contains("BeginIO(", r.C!);
    }

    [Fact]
    public void Aga_sample_compiles_for_m68k_with_a_24_bit_palette_call()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "aga");
        var sources = new[] { "Aga.cs", "Assets.g.cs" }
            .Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("SetRGB32(", r.C!);
        Assert.Contains("OpenLibrary(", r.C!);
    }

    [Fact]
    public void Rtg_sample_compiles_for_m68k_with_a_private_cybergraphics_base()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "rtg", "Rtg.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Rtg.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        // A program must still start on a machine without cybergraphics.library: the startup code's base CyberGfxBase is renamed away.
        Assert.Contains("#define CyberGfxBase es_CyberGfxBase", r.C!);
        Assert.Contains("struct Library *es_CyberGfxBase;", r.C!);
        Assert.Contains("IsCyberModeID(", r.C!);
        Assert.Contains("BestCModeIDTagList(", r.C!);
    }

    [Fact]
    public void Controls_sample_compiles_for_m68k_with_private_lowlevel_and_timer_bases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "controls", "Controls.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Controls.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        // lowlevel.library is on disk: a program must start without it, so libnix must not see LowLevelBase.
        Assert.Contains("#define LowLevelBase es_LowLevelBase", r.C!);
        Assert.Contains("struct Library *es_LowLevelBase;", r.C!);
        Assert.Contains("#define TimerBase es_TimerBase", r.C!);
        Assert.Contains("struct Device *es_TimerBase;", r.C!);
        Assert.Contains("#include <proto/lowlevel.h>", r.C!);
        Assert.Contains("#include <proto/timer.h>", r.C!);
        Assert.Contains("ReadJoyPort(", r.C!);
        Assert.Contains("ReadEClock(", r.C!);
    }

    [Theory]
    [InlineData("pal", 50)]
    [InlineData("ntsc", 60)]
    [InlineData("exec50", 50)]   // neither display flag set (hosted AROS): ExecBase.VBlankFrequency decides
    [InlineData("exec60", 60)]
    [InlineData("stuck", 50)]   // a WaitTOF that does not wait (AmigaOS 4 in QEMU): timer.device paces the frames (DoIO advances the shim's clock)
    [InlineData("stuckwork", 50)]   // the same with 0.7 frame of work between waits: the sticky broken-display flag keeps 50 frames at about a second
    public void Frame_timing_runs_natively_against_a_shimmed_timer_and_graphics(string mode, int fps)
    {
        bool pal = fps == 50;
        if (!HasGcc) return;
        string body = @"
Clock.Open();
Dos.PutStr(Frame.FramesPerSecond + "" "" + Frame.FrameMicroseconds + ""\n"");
Frame.WaitVBlank(); Frame.ResetCount();
long t0 = Clock.Ticks;
for (int i = 0; i < Frame.FramesPerSecond; i++) { Dos.Delay(WORK); Frame.WaitVBlank(); }
int ms = Clock.Milliseconds(t0, Clock.Ticks);
FrameTimer t = new FrameTimer(50, 5);
t.Begin();
int steps = 0;
for (int i = 0; i < 10; i++) { Frame.WaitVBlank(); steps += t.Update(); }
Dos.Delay(50);
int stall = t.Update();
int after = t.Update();
Dos.PutStr(Frame.Count + "" "" + ms + "" "" + steps + "" "" + stall + "" "" + after + "" "" + Frame.PacedFrames + ""\n"");
Clock.Close();";
        body = body.Replace("Dos.Delay(WORK); ", mode == "stuckwork" ? "Dos.Delay(1); " : "");   // in stuckwork Delay(1) is 0.7 frame of simulated work
        var c = CompileOk(Program("", body), Targets.M68k.ToOptions());
        // A shim of timer.device and graphics.library: an E clock at 709379 Hz that Delay and WaitTOF advance by a frame (1/50 or 1/60 s).
        string shim =
            "#include <stdint.h>\n#include <stdlib.h>\n#include <string.h>\n" +
            $"static const int es_shim_pal = {(pal ? 1 : 0)};\n" +
            "static uint64_t es_shim_clock;\n" +
            "struct Library; struct Device;\n" +
            "static inline void *OpenLibrary(const char *n, uint32_t v) { (void)n; (void)v; static unsigned char gb[1024]; memset(gb, 0, sizeof gb); " + (mode == "pal" ? "memset(gb, 0x04, sizeof gb);" : mode == "ntsc" ? "memset(gb, 0x01, sizeof gb);" : $"gb[530] = {fps};") + " return gb; }\n" +
            "static inline void CloseLibrary(void *l) { (void)l; }\n" +
            "static inline int OpenDevice(const char *n, uint32_t u, void *io, uint32_t f) { (void)n; (void)u; (void)io; (void)f; return 0; }\n" +
            "static inline void CloseDevice(void *io) { (void)io; }\n" +
            "static inline uint32_t ReadEClock(void *dest) { uint32_t *p = (uint32_t *)dest; p[0] = (uint32_t)(es_shim_clock >> 32); p[1] = (uint32_t)es_shim_clock; return 709379; }\n" +
            (mode.StartsWith("stuck") ? "static inline void WaitTOF(void) { }\n" : "static inline void WaitTOF(void) { es_shim_clock += es_shim_pal ? 709379 / 50 : 709379 / 60; }\n") +
            "static inline void *CreateMsgPort(void) { static int port; return &port; }\n" +
            "static inline void DeleteMsgPort(void *p) { (void)p; }\n" +
            "static inline int DoIO(void *io) { const unsigned char *b = (const unsigned char *)io; uint32_t cmd = *(const uint16_t *)(b + 28), s = *(const uint32_t *)(b + 32), us = *(const uint32_t *)(b + 36);\n" +
            "  if (cmd == 9) { es_shim_clock += ((uint64_t)s * 1000000 + us) * 709379 / 1000000; }\n  return 0;\n}\n" +
            "static inline void Delay(int32_t t) { es_shim_clock += " + (mode == "stuckwork" ? "(uint64_t)t * 709379 * 7 / 500" : "(uint64_t)t * 709379 / 50") + "; }\n" +
            "struct Device *TimerBase;\n";
        var headers = new System.Collections.Generic.Dictionary<string, string>
        {
            ["proto/timer.h"] = shim,
            ["proto/graphics.h"] = "",
        };
        var (code, output) = RunWithHostGcc(c, headers);
        Assert.Equal(0, code);
        var lines = output.Split('\n');
        Assert.Equal($"{fps} {1000000 / fps}", lines[0]);
        var parts = lines[1].Split(' ');
        Assert.Equal($"{fps + 10}", parts[0]);                           // the counter has the second's frames and ten more
        Assert.InRange(int.Parse(parts[1]), 990, 1010);                  // the shimmed 709379 Hz clock gives close to, not exactly, 1000 ms
        Assert.InRange(int.Parse(parts[2]), 8, 10);                      // ten frames owe about ten 1/50 s steps
        Assert.Equal("5", parts[3]);                                     // a one second stall is capped
        Assert.Equal("0", parts[4]);                                     // and the backlog is dropped
        if (mode == "stuckwork") Assert.InRange(int.Parse(parts[5]), fps + 5, fps + 10);   // all but the first few (before the flag sets) were padded
        else Assert.Equal(mode == "stuck" ? $"{fps + 10}" : "0", parts[5]);  // every wait after the first was paced by the timer, or none
    }

    [Fact]
    public void Dates_sample_compiles_for_m68k_with_a_private_utility_base()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "dates", "Dates.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Dates.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("#define UtilityBase es_UtilityBase", r.C!);
        Assert.Contains("struct Library *es_UtilityBase;", r.C!);
        Assert.Contains("Amiga2Date(", r.C!);
        Assert.Contains("Date2Amiga(", r.C!);
        Assert.Contains("Stricmp(", r.C!);
        Assert.Contains("DateStamp(", r.C!);
    }

    /// <summary>
    /// A stand-in for the system headers an Amiga build includes (proto/intuition.h pulls in exec and graphics): the
    /// structs keep their real tag names, so an E# type whose C name were just 'Window' or 'Node' would clash here.
    /// </summary>
    private const string SystemStructsShim =
        "#ifndef SHIM_INTUITION_H\n#define SHIM_INTUITION_H\n#include <stdint.h>\n" +
        "struct Node { struct Node *ln_Succ; struct Node *ln_Pred; uint8_t ln_Type; int8_t ln_Pri; char *ln_Name; };\n" +
        "struct List { struct Node *lh_Head; struct Node *lh_Tail; struct Node *lh_TailPred; uint8_t lh_Type; };\n" +
        "struct Task { struct Node tc_Node; };\nstruct Message { struct Node mn_Node; uint16_t mn_Length; };\n" +
        "struct Rectangle { int16_t MinX, MinY, MaxX, MaxY; };\nstruct Hook { struct Node h_MinNode; };\n" +
        "struct Image { int16_t LeftEdge; };\nstruct Border { int16_t LeftEdge; };\nstruct Gadget { struct Gadget *NextGadget; };\n" +
        "struct Menu { struct Menu *NextMenu; };\nstruct Screen { struct Screen *NextScreen; };\n" +
        "struct Window { struct Window *NextWindow; struct Screen *WScreen; };\n" +
        "static inline void CurrentTime(uint32_t *s, uint32_t *m) { *s = 1; *m = 2; }\n#endif\n";

    [Fact]
    public void Types_named_like_system_structs_and_same_named_types_in_two_namespaces_compile_against_the_system_headers_and_run()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "names", "Names.cs");
        var c = CompileOk(File.ReadAllText(path), new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } });
        Assert.Contains("#include <proto/intuition.h>", c);
        Assert.Contains("typedef struct Es_NamesSample_Window Es_NamesSample_Window;", c);
        Assert.Contains("typedef struct Es_NamesSample_Left_Item Es_NamesSample_Left_Item;", c);
        Assert.Contains("typedef struct Es_NamesSample_Right_Item Es_NamesSample_Right_Item;", c);
        Assert.Contains("Es_NamesSample_Box__NamesSample_Left_Point", c);
        Assert.Contains("} Es_ESharp_Fix;", c);   // the embedded library is prefixed as well
        foreach (var system in new[] { "Window", "Gadget", "Screen", "Node", "List", "Menu", "Image", "Task", "Message", "Rectangle", "Border" })
            Assert.DoesNotContain($"struct {system} ", c);
        var (code, output) = RunWithHostGcc(c, new System.Collections.Generic.Dictionary<string, string> { ["proto/intuition.h"] = SystemStructsShim });
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(12, output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Fact]
    public void Same_named_types_in_different_namespaces_get_their_own_C_names()
    {
        if (!HasGcc) return;
        var src = Program("", "Dos.PutStr($\"{new A.Window().Id() + new B.Window().Id()} {A.Tools.N + B.Tools.N}\\n\");")
            + "\nnamespace A { public class Window { public int Id() { return 1; } } public static class Tools { public static int N = 10; } }"
            + "\nnamespace B { public class Window { public int Id() { return 2; } } public static class Tools { public static int N = 20; } }";
        var c = CompileOk(src);
        Assert.Contains("int32_t Es_A_Window_Id(Es_A_Window *_this)", c);
        Assert.Contains("int32_t Es_B_Window_Id(Es_B_Window *_this)", c);
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("3 30\n", output);
    }

    [Fact]
    public void Types_whose_mangled_C_names_meet_report_ES0099()
    {
        // namespace A_B, class C and namespace A, class B_C both become Es_A_B_C.
        var r = Compile(Program("", "") + "\nnamespace A_B { public class C { } }\nnamespace A { public class B_C { } }");
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Fact]
    public void Keys_sample_compiles_for_m68k_with_a_private_keymap_base()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "keys", "Keys.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Keys.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("#define KeymapBase es_KeymapBase", r.C!);
        Assert.Contains("struct Library *es_KeymapBase;", r.C!);
        Assert.Contains("#include <proto/keymap.h>", r.C!);
        Assert.Contains("MapRawKey(", r.C!);
        Assert.Contains("MapANSI(", r.C!);
    }

    [Fact]
    public void Fonts_sample_compiles_for_m68k_with_a_private_diskfont_base()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "fonts", "Fonts.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Fonts.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("#define DiskfontBase es_DiskfontBase", r.C!);
        Assert.Contains("struct Library *es_DiskfontBase;", r.C!);
        Assert.Contains("#include <proto/diskfont.h>", r.C!);
        Assert.Contains("OpenDiskFont(", r.C!);
        Assert.Contains("OpenFont(", r.C!);
        Assert.Contains("Text(", r.C!);
    }

    [Fact]
    public void Iff_sample_compiles_for_m68k_with_a_private_iffparse_base_and_the_built_in_fallback()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "iff", "Iff.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Iff.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("#define IFFParseBase es_IFFParseBase", r.C!);
        Assert.Contains("struct Library *es_IFFParseBase;", r.C!);
        Assert.Contains("#include <proto/iffparse.h>", r.C!);
        Assert.Contains("AllocIFF(", r.C!);
        Assert.Contains("ParseIFF(", r.C!);
        Assert.Contains("PushChunk(", r.C!);
        Assert.Contains("WriteChunkBytes(", r.C!);
        Assert.Contains("ReadChunkBytes(", r.C!);
    }

    [Theory]
    [InlineData("os4")]
    [InlineData("aros")]
    [InlineData("sdl")]
    public void Iff_sample_compiles_for_every_target(string target)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "iff", "Iff.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Iff.cs", File.ReadAllText(path)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal(target != "sdl", r.C!.Contains("PushChunk("));
    }

    [Fact]
    public void Asl_sample_compiles_for_m68k_with_a_private_asl_base()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "asl", "Asl.cs");
        var r = EsCompiler.Compile(new[] { new SourceFile("Asl.cs", File.ReadAllText(path)) }, Targets.M68k.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("#define AslBase es_AslBase", r.C!);
        Assert.Contains("struct Library *es_AslBase;", r.C!);
        Assert.Contains("#include <proto/asl.h>", r.C!);
        Assert.Contains("AllocAslRequest(", r.C!);
        Assert.Contains("AslRequest(", r.C!);
        Assert.Contains("FreeAslRequest(", r.C!);
    }

    [Theory]
    [InlineData("m68k")]
    [InlineData("aros")]
    [InlineData("aros-i386")]
    public void Gui_sample_compiles_with_a_private_gadtools_base_and_releases_in_order(string target)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "gui", "Gui.cs");
        var options = Targets.Find(target)!.ToOptions();
        var r = EsCompiler.Compile(new[] { new SourceFile("Gui.cs", File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var c = r.C!;
        Assert.Contains("#define GadToolsBase es_GadToolsBase", c);
        Assert.Contains("#include <proto/gadtools.h>", c);
        Assert.Contains("GT_GetIMsg(", c);
        Assert.Contains("GT_ReplyIMsg(", c);
        Assert.Contains("LockPubScreen(", c);
        // Close order: window, gadgets, visual info, screen unlock.
        int close = c.IndexOf("CloseWindow(", StringComparison.Ordinal);
        int free = c.IndexOf("FreeGadgets(", close, StringComparison.Ordinal);
        int vi = c.IndexOf("FreeVisualInfo(", free, StringComparison.Ordinal);
        int unlock = c.IndexOf("UnlockPubScreen(", vi, StringComparison.Ordinal);
        Assert.True(close >= 0 && free > close && vi > free && unlock > vi, "teardown order");
    }

    [Theory]
    [InlineData("m68k")]
    [InlineData("aros")]
    [InlineData("aros-i386")]
    public void GtWindow_Run_with_a_handler_emits_no_null_forgiving_operator(string target)
    {
        var src = @"
using ESharp;
using ESharp.Platform;
namespace T;
public sealed class H : IGuiHandler
{
    public int N;
    public void Clicked(GtGadget gadget) { N++; }
    public void Changed(GtGadget gadget) { N += 2; }
}
public static unsafe class P
{
    [EntryPoint]
    public static void Main()
    {
        GtWindow win = new GtWindow { Title = ""t"" };
        GtText t = new GtText { Label = ""x"" };
        win.Add(t);
        H h = new H();
        if (win.Open()) { win.Run(h); win.Close(); }
    }
}";
        var options = Targets.Find(target)!.ToOptions();
        var r = EsCompiler.Compile(new[] { new SourceFile("P.cs", src) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains("Clicked(", r.C!);
        Assert.DoesNotContain("Source!", r.C!);
        Assert.DoesNotMatch(@"\w!\s*[,;)]", r.C!);
    }

    [Fact]
    public void A_library_base_class_calls_through_its_own_base_variable_that_SetBase_fills()
    {
        if (!HasGcc) return;
        var src = Prelude + @"
[LibraryBase(""widget"", ""WidgetBase"", ""struct Library"")]
public static unsafe class Widget
{
    public static extern void SetBase(void* library);
    [AmigaLib(""widget"", -30)] public static extern int Scale(int x);
}
public static unsafe class P
{
    [EntryPoint]
    public static void Main()
    {
        int[] fake = new int[2];
        fixed (int* lib = fake)
        {
            fake[0] = 3;
            Widget.SetBase(lib);
            Dos.PutStr($""{Widget.Scale(7)} "");
            fake[0] = 5;
            Dos.PutStr($""{Widget.Scale(7)}\n"");
            Widget.SetBase(null);
        }
    }
}";
        var c = CompileOk(src, new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } });
        Assert.Contains("#define WidgetBase es_WidgetBase", c);
        // The shim reads the base by its usual name, as a real proto header does; the rename must reach it.
        var shim = new System.Collections.Generic.Dictionary<string, string>
        {
            ["proto/widget.h"] = "struct Library;\nextern struct Library *WidgetBase;\n"
                + "static inline int Scale(int x) { return *(int *)WidgetBase * x; }\n",
        };
        var (code, output) = RunWithHostGcc(c, shim);
        Assert.Equal(0, code);
        Assert.Equal("21 35\n", output);
    }

    [Fact]
    public void Property_assignment_used_as_a_value_reports_ES0099()
    {
        var r = Compile(Program(
            "struct S { int _v; public int V { get { return _v; } set { _v = value; } } }",
            "S s = default; int x = (s.V = 3);"));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Fact]
    public void Language_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "language", "Language.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(LanguageExpected.Output, output);
    }

    [Fact]
    public void Generics_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "generics", "Generics.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var (code, output) = RunWithHostGcc(CompileOk(File.ReadAllText(path), options));
        Assert.Equal(0, code);
        Assert.Equal(GenericsExpected.Output, output);
    }

    [Fact]
    public void Memory_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "memory", "Memory.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var (code, output) = RunWithHostGcc(CompileOk(File.ReadAllText(path), options));
        Assert.Equal(0, code);
        Assert.Equal(MemoryExpected.Output, output);
    }

    [Fact]
    public void Chipcheck_sample_compiles_and_runs_natively_without_a_failed_check()
    {
        if (!HasGcc) return;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "chipcheck", "ChipCheck.cs");
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var (code, output) = RunWithHostGcc(CompileOk(File.ReadAllText(path), options));
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(6, output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Fact]
    public void Arrays_become_c_arrays_and_spans_become_pointer_and_length()
    {
        var c = CompileOk(Program(
            "static int[] T = { 1, 2, 3 }; static int Sum(Span<int> s) { return s.Length > 0 ? s[0] : 0; }",
            "int[] a = new int[4]; Sum(a);").Replace("using ESharp;", "using System;\nusing ESharp;"));
        Assert.Contains("static int32_t Es_P_T[3] = { 1, 2, 3 };", c);
        Assert.Contains("int32_t a[4] = {0};", c);
        Assert.Contains("typedef struct { int32_t *ptr; int32_t length; } ES_Span_int;", c);
        Assert.Contains("s.ptr[0]", c);
    }

    [Fact]
    public void Operators_on_structs_become_functions()
    {
        var c = CompileOk(Program("", "Fix a = Fix.FromInt(2); Fix b = a * a + a;"));
        Assert.Contains("Es_ESharp_Fix_op_Multiply_ESharp_Fix_ESharp_Fix(a, a)", c);
        Assert.Contains("Es_ESharp_Fix_op_Addition(", c);
    }

    [Fact]
    public void Struct_instance_methods_take_a_pointer_and_constructors_return_the_struct()
    {
        var c = CompileOk(Program(
            "struct S { public int V; public S(int v) { V = v; } public int Get() { return V; } }",
            "S s = new S(3); s.Get();"));
        Assert.Contains("Es_P_S Es_P_S_new(int32_t v)", c);
        Assert.Contains("int32_t Es_P_S_Get(Es_P_S *_this)", c);
        Assert.Contains("_this->V", c);
        Assert.Contains("Es_P_S_Get(&s)", c);
    }

    [Fact]
    public void Calls_on_temporaries_work_on_a_copy()
    {
        // Was ES0099; the value is now copied into a C compound literal (see ClassFeatureTests for more cases).
        var r = Compile(Program(
            "struct S { public int V; public int Get() { return V; } } static S Make() { return default; }",
            "Make().Get();"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
    }

    [Fact]
    public void This_initializer_in_a_struct_is_accepted()
    {
        var r = Compile(Program("struct S { public int V; public S(int v) : this() { V = v; } }"));
        Assert.DoesNotContain("ES0099", ErrorIds(r));
    }

    [Fact]
    public void Bounds_checks_pass_in_range_and_exit_20_out_of_range()
    {
        if (!HasGcc) return;
        const string members = "static int Get(Span<int> s, int i) { return s[i]; }";
        string P(string body) => Program(members, body).Replace("using ESharp;", "using System;\nusing ESharp;");
        string Arr(string idx) => "int[] a = new int[4]; Dos.PutStr(\"start\\n\"); int k = " + idx + "; a[k] = 1; Dos.PutStr(\"after\\n\");";
        var opts = new CompileOptions { BoundsChecks = true };

        var ok = RunWithHostGcc(CompileOk(P(Arr("3")), opts));
        Assert.Equal(0, ok.exitCode);
        Assert.Equal("start\nafter\n", ok.output);

        var bad = RunWithHostGcc(CompileOk(P(Arr("4")), opts));
        Assert.Equal(20, bad.exitCode);
        Assert.Equal("start\n", bad.output);

        var span = RunWithHostGcc(CompileOk(P(
            "int[] a = new int[4]; Span<int> s = a; Dos.PutStr(\"start\\n\"); if (Get(s, 9) == 1) Dos.PutStr(\"after\\n\");"), opts));
        Assert.Equal(20, span.exitCode);

        Assert.DoesNotContain("ES_IDX", CompileOk(P(Arr("3"))));
    }

    /// <summary>
    /// On AROS the bounds and out-of-memory messages go through dos.library, not stdio's stderr: stderr is posixc.library's, and on
    /// hosted i386 AROS a program with posixc.library open that makes a Zune Application crashed in the C library's exit code. The
    /// AROS helpers run here against a small dos.library shim that writes to stdout.
    /// </summary>
    [Theory]
    [InlineData("aros")]
    [InlineData("aros-i386")]
    public void Aros_error_messages_use_dos_library_not_stderr(string target)
    {
        var opts = Targets.Find(target)!.ToOptions(debug: true);
        string c = CompileOk(Program("public class Box { public int V; }",
            "int[] a = new int[4]; Box b = new Box(); b.V = a[1]; Dos.PutInt(b.V);"), opts).Replace("\r\n", "\n");   // AppendLine is CRLF on Windows
        int start = c.IndexOf("#ifdef __AROS__\n#include <proto/dos.h>\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "the AROS error helpers");
        int end = c.IndexOf("#else\n", start, StringComparison.Ordinal);
        string aros = c.Substring(start, end - start);
        Assert.Contains("ErrorOutput()", aros);
        Assert.DoesNotContain("stderr", aros);
        Assert.Contains("es_errstr(\"out of memory\\n\")", c);
        Assert.Contains("es_errstr(\" out of range 0..\"); es_errint((long)n - 1);", c);
        if (!HasGcc) return;
        string helpers = aros.Replace("#ifdef __AROS__\n#include <proto/dos.h>\n", "");
        var run = RunWithHostGcc(@"#include <stdio.h>
typedef void *BPTR;
typedef const unsigned char *CONST_STRPTR;
static BPTR ErrorOutput(void) { return NULL; }
static BPTR Output(void) { return (BPTR)stdout; }
static void FPuts(BPTR f, CONST_STRPTR s) { fputs((const char *)s, (FILE *)f); }
static void Flush(BPTR f) { fflush((FILE *)f); }
" + helpers + @"
int main(void)
{
    es_errstr(""f.cs(""); es_errint(12); es_errstr(""): index ""); es_errint(-7); es_errstr("" out of range 0..""); es_errint(0); es_errint(-2147483647L - 1);
    es_errstr(""\n"");
    return 0;
}
");
        Assert.Equal(0, run.exitCode);
        Assert.Equal("f.cs(12): index -7 out of range 0..0-2147483648\n", run.output);
    }

    [Fact]
    public void Tag_writer_builds_tag_lists_and_refuses_a_list_that_does_not_fit()
    {
        if (!HasGcc) return;
        const string body = @"
nuint[] a = new nuint[7];
fixed (nuint* p = a)
{
    var t = new TagWriter(p, 7);
    t.Add(0x80000023u, 320); t.Add(0x80000038u, true); t.Add(5u, -1);
    Dos.PutStr(t.End() ? ""ok "" : ""no ""); Dos.PutInt(t.Count);
    for (int i = 0; i < 7; i++) { Dos.PutStr("" ""); Dos.PutInt((int)(uint)p[i]); }
    Dos.PutStr(""\n"");
}
nuint[] b = new nuint[6];
fixed (nuint* q = b)
{
    var u = new TagWriter(q, 6);
    u.Add(1u, 1); u.Add(2u, 2); u.Add(3u, 3);
    Dos.PutStr(u.End() ? ""ok "" : ""no ""); Dos.PutInt((int)q[0]); Dos.PutStr(""\n"");
}";
        var src = Program("", body).Replace("public static void Main()", "public static unsafe void Main()");
        var (code, output) = RunWithHostGcc(CompileOk(src, Targets.M68k.ToOptions()));
        Assert.Equal(0, code);
        Assert.Equal("ok 3 -2147483613 320 -2147483592 1 5 -1 0\nno 0\n", output);
    }

    [Fact]
    public void Tag_list_has_its_own_storage_ends_with_tag_done_and_refuses_overflow()
    {
        if (!HasGcc) return;
        const string members = @"
static unsafe int Dump(nuint* p, int slots) { int sum = 0; for (int i = 0; i < slots; i++) { Dos.PutStr("" ""); Dos.PutInt((int)(uint)p[i]); } Dos.PutStr(""\n""); return sum; }
";
        const string body = @"
TagList t = new TagList();
t.Add(0x80000023u, 320); t.Add(0x80000038u, true); t.Add(5u, -1);
nuint* p = t.End();
Dos.PutStr(p != null ? ""ok "" : ""no ""); Dos.PutInt(t.Count);
Dump(p, 7);
TagList u = new TagList();
for (int i = 0; i < TagList.MaxPairs; i++) u.Add(1u, i);
nuint* q = u.End();
Dos.PutStr(q != null ? ""full ok "" : ""full no ""); Dos.PutInt(u.Count); Dos.PutStr("" ""); Dos.PutInt((int)(uint)q[2 * TagList.MaxPairs]); Dos.PutStr(""\n"");
u.Add(1u, 99);
Dos.PutStr(u.End() == null ? ""over null\n"" : ""over ok\n"");
TagList v = new TagList();
v.Add(7u, 8);
Dos.PutInt((int)(uint)v.End()[0]); Dos.PutStr("" ""); Dos.PutInt((int)(uint)v.End()[1]); Dos.PutStr("" ""); Dos.PutInt((int)(uint)v.End()[2]); Dos.PutStr(""\n"");";
        var src = Program(members, body).Replace("public static void Main()", "public static unsafe void Main()");
        var (code, output) = RunWithHostGcc(CompileOk(src, Targets.M68k.ToOptions()));
        Assert.Equal(0, code);
        Assert.Equal("ok 3 -2147483613 320 -2147483592 1 5 -1 0\nfull ok 15 0\nover null\n7 8 0\n", output);
    }

    [Fact]
    public void Debug_define_poisons_arena_on_reset()
    {
        if (!HasGcc) return;
        const string body = "Arena a = new Arena(64, true); byte* p = a.Alloc(8, 4); p[0] = 1; a.Reset(); " +
            "if (p[0] == 0xDD) Dos.PutStr(\"poisoned\\n\"); a.Destroy();";
        var src = Program("", body).Replace("public static void Main()", "public static unsafe void Main()");
        var debug = RunWithHostGcc(CompileOk(src, Targets.M68k.ToOptions(debug: true)));
        Assert.Equal("poisoned\n", debug.output);
        var release = RunWithHostGcc(CompileOk(src, Targets.M68k.ToOptions()));
        Assert.Equal("", release.output);
    }

    [Fact]
    public void Generic_structs_and_methods_are_monomorphised()
    {
        const string members = @"
struct Pair<T> { public T A; public T B; public Pair(T a, T b) { A = a; B = b; } public T First() { return A; } }
static T Max<T>(T a, T b) where T : unmanaged { return Cmp(a, b) ? a : b; }
static bool Cmp<T>(T a, T b) where T : unmanaged { return false; }
static int Twice<T>(Pair<T> p) where T : unmanaged { return 2; }
";
        const string body = @"
Pair<int> p = new Pair<int>(3, 4);
var q = new Pair<Fix>(Fix.FromInt(1), Fix.FromInt(2));
int m = Max(p.First(), 9);
if (p.First() == 3 && Fix.ToInt(q.B) == 2 && Twice(p) == 2 && m == 9) Dos.PutStr(""ok\n"");
";
        var c = CompileOk(Program(members, body));
        Assert.Contains("Pair__int", c);
        Assert.Contains("Pair__ESharp_Fix", c);
        Assert.DoesNotContain("Pair_T", c);
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void Generic_copies_go_before_an_else_branch_at_the_end_of_the_class()
    {
        // The copies of Pick<T> are added at the end of P, whose closing brace has #else, disabled text and #endif in front of it.
        const string members = @"
static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;
#if AMIGA
static int Mode() => 1;
#else
static int Mode() => 2;
#endif
";
        var c = CompileOk(Program(members, "if (Pick(false, 3, 4) == 4 && Mode() == 1) Dos.PutStr(\"ok\\n\");"));
        if (!HasGcc) return;
        Assert.Equal((0, "ok\n"), RunWithHostGcc(c));
    }

    // ---- ref locals, ref returns and foreach over the GetEnumerator pattern (issue #12) ----

    [Fact]
    public void Ref_locals_and_ref_returns_write_through_natively()
    {
        if (!HasGcc) return;
        const string members = @"
public struct Point { public int X; public int Y; }
static Point gp;
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
static ref int Slot(System.Span<int> a, int i) { return ref a[i]; }
static ref int Pick(ref Point p, bool x) => ref x ? ref p.X : ref p.Y;
static ref Point At(System.Span<Point> ps, int i) { return ref ps[i]; }
static ref int GlobalY() { return ref gp.Y; }
static ref readonly int Peek(System.Span<int> a) { return ref a[0]; }
static void Bump(ref int v) { v += 100; }
";
        const string body = @"
int x = 1;
ref int r = ref x;
r = 2;
Check(x == 2, ""ref local writes through"");
r++;
r += 10;
Check(x == 13, ""++ and += through a ref local"");
int y = 10;
r = ref y;
r = 11;
Check(x == 13 && y == 11, ""ref local pointed somewhere else"");
int[] arr = new int[4];
ref int e = ref arr[2];
e = 7;
Check(arr[2] == 7, ""ref local to an array element"");
Slot(arr, 1) = 5;
Slot(arr, 1) += 2;
Check(arr[1] == 7 && Slot(arr, 1) == 7, ""assignment to a ref return (array element)"");
ref int s = ref Slot(arr, 3);
s = 9;
Check(arr[3] == 9, ""ref local from a ref return"");
Point p = default;
Pick(ref p, true) = 4;
Pick(ref p, false) = 6;
Check(p.X == 4 && p.Y == 6, ""ref return into a struct field"");
Point[] pts = new Point[3];
At(pts, 1).X = 8;
Check(pts[1].X == 8, ""field of a ref-returned struct"");
ref Point q = ref pts[2];
q.Y = 12;
q.X = q.Y + 1;
Check(pts[2].Y == 12 && pts[2].X == 13, ""ref local to a struct element"");
Bump(ref Slot(arr, 0));
Check(arr[0] == 100, ""ref return passed as a ref argument"");
GlobalY() = 3;
Check(gp.Y == 3, ""ref return of a static field"");
ref readonly int ro = ref Peek(arr);
arr[0] = 55;
Check(ro == 55, ""ref readonly sees later writes"");
System.Span<int> sp = arr;
foreach (ref int v in sp) v *= 2;
Check(arr[0] == 110 && arr[3] == 18, ""foreach by ref over a span"");
System.ReadOnlySpan<int> rs = arr;
int rsum = 0;
foreach (ref readonly int v in rs) rsum += v;
Check(rsum == 156, ""foreach by ref readonly over a read-only span"");
";
        var c = CompileOk(Program(members, body));
        Assert.Contains("int32_t *Es_P_Slot(", c);
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(14, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Foreach_over_enumerator_pattern_runs_natively()
    {
        if (!HasGcc) return;
        const string members = @"
public struct Countdown { public int From; public CountdownEnum GetEnumerator() { CountdownEnum e = default; e.Left = From + 1; return e; } }
public struct CountdownEnum { public int Left; public bool MoveNext() { Left--; return Left > 0; } public int Current { get { return Left; } } }
public struct Evens { public int Below; public EvensEnum GetEnumerator() { return new EvensEnum(Below); } }
public struct EvensEnum
{
    private int _below;
    public EvensEnum(int below) { _below = below; Current = -2; }
    public int Current { get; private set; }
    public bool MoveNext() { Current = Current + 2; return Current < _below; }
}
public class Steps
{
    public int N;
    public Steps(int n) { N = n; }
    public StepsEnum GetEnumerator() { return new StepsEnum(N); }
}
public class StepsEnum
{
    private int _n;
    private int _i;
    public StepsEnum(int n) { _n = n; _i = 0; }
    public bool MoveNext() { _i++; return _i <= _n; }
    public int Current { get { return _i * 3; } }
}
static Countdown Make(int from) { Countdown c = default; c.From = from; return c; }
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";
        const string body = @"
Vec<int> v = default;
for (int i = 1; i <= 5; i++) v.Add(i * 10);
int sum = 0;
foreach (int x in v) sum += x;
Check(sum == 150, ""foreach over a Vec"");
int seen = 0;
foreach (var x in v) { if (x == 20) continue; if (x == 40) break; seen += x; }
Check(seen == 40, ""continue and break over a Vec"");
Countdown c3 = default;
c3.From = 4;
int n = 0;
foreach (int k in c3) { if (k == 2) continue; n = n * 10 + k; }
Check(n == 431, ""user struct enumerator with continue"");
int cd = 0;
foreach (int k in Make(3)) cd = cd * 10 + k;
Check(cd == 321, ""enumerable returned by a call"");
long big = 0;
foreach (long k in c3) big += k;
Check(big == 10, ""element widened to long"");
int pairs = 0;
foreach (int a in c3) foreach (int b in v) { if (b > 20) break; pairs++; }
Check(pairs == 8, ""nested foreach with break in the inner loop"");
Evens ev = default;
ev.Below = 9;
int es = 0;
foreach (int k in ev) es += k;
Check(es == 20, ""enumerator with an auto-property Current"");
Steps st = new Steps(4);
int ss = 0;
foreach (int k in st) { if (k == 9) break; ss += k; }
Check(ss == 9, ""class enumerable and class enumerator with break"");
v.Destroy();
int none = 0;
foreach (int x in v) none++;
Check(none == 0, ""empty Vec"");
";
        var c = CompileOk(Program(members, body));
        Assert.Contains("_MoveNext(&_e", c);   // a struct enumerator is a local on the stack
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(9, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Ref_conditional_works_as_value_assignment_target_and_argument()
    {
        if (!HasGcc) return;
        const string members = @"
static int s_a = 1; static int s_b = 2;
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
static int G(in int v) { return v; }
";
        const string body = @"
bool flag = false;
int v = flag ? ref s_a : ref s_b;
Check(v == 2, ""ref conditional as a value"");
(flag ? ref s_a : ref s_b) = 5;
Check(s_a == 1 && s_b == 5, ""ref conditional as an assignment target"");
int[] arr = new int[2]; arr[0] = 3; arr[1] = 4;
Check(G(flag ? ref arr[0] : ref arr[1]) == 4, ""ref conditional as an in argument"");
";
        var c = CompileOk(Program(members, body));
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(3, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData("public unsafe struct S { int* _p; public ref int this[int i] { get { return ref _p[i]; } } }", "S s = default; int v = s[0];")]
    [InlineData("public interface ISeq { E GetEnumerator(); } public struct E { public int Current { get { return 0; } } public bool MoveNext() { return false; } } public class C : ISeq { public E GetEnumerator() { return default; } }",
        "ISeq q = new C(); foreach (int x in q) { }")]
    [InlineData("public unsafe struct E { int* _p; public ref int Current { get { return ref *_p; } } public bool MoveNext() { return false; } } public struct L { public E GetEnumerator() { return default; } }",
        "L l = default; foreach (int x in l) { }")]
    [InlineData("public abstract class B { public abstract ref int Get(); }", "")]
    [InlineData("public ref struct H { public ref int R; }", "")]
    [InlineData("public ref struct H { public ref int R; }", "H h = default; h.R = 4;")]
    public void Unsupported_ref_and_foreach_forms_report_ES0099(string members, string body)
    {
        var r = Compile(Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    // ---- #49: string equality by characters, null-forgiving operator, base/derived comparisons ----

    private const string CheckMember = @"
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";

    private static void AssertAllOk(string members, string body, int expected)
    {
        var c = CompileOk("using System.Linq;\n" + Program(CheckMember + members, body));
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.True(!output.Contains("FAIL") && output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == expected, output);
    }

    [Fact]
    public void String_equality_compares_characters_with_csharp_null_rules()
    {
        if (!HasGcc) return;
        const string members = @"
static string s_null;
static string Name(int n) => $""card{n}"";
static string Lit() => ""card7"";
static bool Eq(string a, string b) => a == b;
static bool Ne(string a, string b) => a != b;
";
        const string body = @"
string built = Name(7);
string lit = Lit();
Check(built == ""card7"" && lit == built && !(built != lit), ""equal contents at different addresses"");
Check(built != ""card8"" && built != ""card"" && ""card"" != built && Ne(""ab"", ""abc""), ""different contents"");
Check(lit == ""card7"" && Eq(lit, lit), ""interned literals and the same pointer"");
string none = null;
Check(Eq(none, s_null) && !Ne(none, null), ""null equals null"");
Check(!Eq(none, ""x"") && !Eq(""x"", none) && Ne(none, """") && Ne("""", none), ""null is not equal to a string, not even the empty one"");
Check(none == null && lit != null && null != lit && !(lit == null) && none == default, ""comparisons with the null constant"");
Check(string.Equals(built, ""card7"") && lit.Equals(built) && !lit.Equals(""card"") && !string.Equals(none, lit) && string.Equals(none, s_null), ""string.Equals"");
";
        AssertAllOk(members, body, 7);
        var c = CompileOk(Program(CheckMember + members, body));
        Assert.Contains("es_streq", c);
        Assert.DoesNotContain("strcmp", c);
    }

    [Fact]
    public void Switch_and_patterns_on_strings_match_by_characters()
    {
        if (!HasGcc) return;
        const string members = @"
static string Copy(string s) => $""{s}"";
static int Kind(string s)
{
    switch (s)
    {
        case ""hearts"":
        case ""diamonds"": return 1;
        case ""spades"": return 2;
        case null: return 9;
        default: return 0;
    }
}
static int Rank(string s) => s switch { ""ace"" => 14, ""king"" => 13, null => -1, _ => 0 };
static int Fall(string s)
{
    int n = 0;
    switch (s) { case ""a"": n = 1; break; case ""b"": n = 2; break; }
    return n;
}
public class Named { public string Name; }
";
        const string body = @"
Check(Kind(Copy(""hearts"")) == 1 && Kind(Copy(""diamonds"")) == 1 && Kind(Copy(""spades"")) == 2 && Kind(Copy(""clubs"")) == 0 && Kind(null) == 9, ""switch statement on a string"");
Check(Fall(Copy(""b"")) == 2 && Fall(Copy(""c"")) == 0, ""switch with break and no default"");
Check(Rank(Copy(""king"")) == 13 && Rank(Copy(""ace"")) == 14 && Rank(Copy(""two"")) == 0 && Rank(null) == -1, ""switch expression on a string"");
string q = Copy(""queen"");
Check(q is ""queen"" && q is not ""king"" && !(q is null) && (q is ""jack"" or ""queen""), ""is with string constants"");
Named n = new Named { Name = Copy(""Ann"") };
Check(n is { Name: ""Ann"" } && !(n is { Name: ""Bob"" }), ""property pattern with a string"");
";
        AssertAllOk(members, body, 5);
    }

    [Fact]
    public void String_comparisons_in_linq_and_lambdas_use_characters()
    {
        if (!HasGcc) return;
        const string members = @"
static string Copy(string s) => $""{s}"";
static int CountIf(System.ReadOnlySpan<string> xs, System.Func<string, bool> f) { int n = 0; for (int i = 0; i < xs.Length; i++) if (f(xs[i])) n++; return n; }
";
        const string body = @"
string[] words = new string[3];
words[0] = Copy(""red""); words[1] = Copy(""green""); words[2] = Copy(""red"");
string red = Copy(""red"");
Check(words.Contains(""green"") && !words.Contains(""blue"") && words.Contains(red), ""Contains on strings"");
Check(words.Count(w => w == ""red"") == 2 && words.Any(w => w == red) && words.Where(w => w != ""red"").Count() == 1, ""== in LINQ lambdas"");
Check(CountIf(words, w => w == red) == 2, ""== in a delegate lambda"");
";
        AssertAllOk(members, body, 3);
    }

    [Fact]
    public void Null_forgiving_operator_emits_only_its_operand()
    {
        if (!HasGcc) return;
        const string members = @"
public class Node { public int Value; public Node Next; public int Twice() { return Value * 2; } }
public class Chain
{
    private Node _last;
    public Node First;
    public void Add(Node n) { if (_last == null) First = n; else _last!.Next = n; _last = n!; }
}
public struct Counter { public int N; public void Bump() { N++; } }
static int Read(Node n) { return n.Value; }
static void Set(ref int v, int x) { v = x; }
static void Get(out int v) { v = 7; }
";
        const string body = @"
Chain c = new Chain();
c.Add(new Node { Value = 1 });
c.Add(new Node { Value = 2 });
Node first = c.First!;
Check(first!.Next!.Value == 2 && c.First!.Next!.Next == null, ""member access through !"");
Check(Read(first!) == 1 && first!.Twice() == 2 && (first!).Next!.Twice() == 4, ""! on arguments and receivers"");
Node n = null;
n = first!;
Check(n == first, ""! in an assignment"");
int v = 0;
Set(ref v!, 5);
Check(v == 5, ""! on a ref argument"");
Get(out v!);
Check(v == 7, ""! on an out argument"");
Counter k = default;
k!.Bump();
Check(k.N == 1, ""! on a struct receiver"");
string s = null;
Check(s! == null, ""! on a string"");
";
        var c = CompileOk(Program(CheckMember + members, body));
        Assert.DoesNotContain("!.", c);
        Assert.DoesNotContain("!)", c);
        AssertAllOk(members, body, 7);
    }

    [Fact]
    public void Null_forgiving_operator_applies_an_implicit_conversion_once()
    {
        if (!HasGcc) return;
        const string members = @"
public interface IScore { int Score(); }
public class Card : IScore { public int Rank; public int Score() { return Rank * 10; } }
public class Ace : Card { }
static int Use(IScore s) { return s.Score(); }
static int Sum(System.Span<int> xs) { int n = 0; for (int i = 0; i < xs.Length; i++) n += xs[i]; return n; }
static int First(System.ReadOnlySpan<int> xs) { return xs[0]; }
static int Raw(Fix f) { return f.Raw; }
static int[] Arr = new int[3];
static string[] Names = new string[2];
static string Copy(string s) => $""{s}"";
";
        const string body = @"
Card c = new Card { Rank = 4 };
IScore s = c!;
Check(s.Score() == 40 && Use(c!) == 40 && Use((c!)) == 40, ""! where a class becomes an interface"");
IScore none = null!;
Check(none == null, ""null! as an interface"");
Arr[0] = 1; Arr[1] = 2; Arr[2] = 3;
System.Span<int> sp = Arr!;
Check(sp.Length == 3 && Sum(Arr!) == 6 && Sum(sp!) == 6 && First(sp!) == 1, ""! where an array becomes a span"");
Names[0] = Copy(""aa""); Names[1] = Copy(""bb"");
Check(Names!.Contains(""bb"") && !Names!.Contains(""cc""), ""! on an array receiver of Contains"");
int n = 3;
Fix b = n!;
Check(b.Raw == 3 * 65536 && Raw(n!) == 3 * 65536, ""! where a user-defined implicit conversion applies"");
Ace a = new Ace { Rank = 2 };
Card asCard = a!;
Check(asCard.Rank == 2 && Use(a!) == 20, ""! where a derived class becomes its base or an interface"");
IScore p = (c);
System.Span<int> ps = (Arr);
Fix pf = (n);
IScore pn = (null);
Check(p.Score() == 40 && Use((c)) == 40 && Use(((c!))) == 40 && ps.Length == 3 && Sum((Arr)) == 6 && pf.Raw == 3 * 65536 && pn == null, ""parentheses where a conversion applies"");
";
        var c = CompileOk("using System.Linq;\n" + Program(CheckMember + members, body));
        Assert.DoesNotContain("op_Implicit_ESharp_Fix(Es_ESharp_Fix_op_Implicit", c);
        AssertAllOk(members, body, 7);
    }

    [Fact]
    public void Is_with_a_named_constant_is_a_constant_pattern()
    {
        if (!HasGcc) return;
        const string members = @"
const string K = ""abc"";
const string Nothing = null;
const int Seven = 7;
public enum Suit { Hearts, Spades }
const Suit Trump = Suit.Spades;
static string Copy(string s) => $""{s}"";
";
        const string body = @"
string s = Copy(""abc"");
string t = Copy(""abd"");
Check(s is K && !(t is K) && t is not K && !(s is not K), ""string is a named constant, by characters"");
string none = null;
Check(!(none is K) && none is Nothing && !(s is Nothing), ""null subject and a null constant"");
int n = 7;
Suit u = Suit.Spades;
Check(n is Seven && !(n is not Seven) && u is Trump && !(Suit.Hearts is Trump), ""int and enum named constants"");
";
        AssertAllOk(members, body, 3);
    }

    [Fact]
    public void Conditions_build_without_parentheses_warnings()
    {
        // gcc and clang -Wall -Werror: no doubled parentheses around a comparison, one pair kept around an assignment.
        if (!HasGcc) return;
        const string members = @"
static int _n = 3;
static bool More() { _n--; return _n > 0; }
";
        const string body = @"
int k = 0;
bool b;
if (b = More()) k++;
while (b = More()) k++;
Check(k == 2 && !b, ""assignment as a condition"");
int m = 1;
if ((m <<= 2) == 4) m++;
Check(m == 5, ""shift-assign inside a comparison"");
int i = 0;
while ((i == 0)) i++;
do { i++; } while ((i < 3));
for (int j = 0; (j < 2); j++) i++;
Check(i == 5, ""doubled parentheses in loop conditions"");
";
        AssertAllOk(members, body, 3);
    }

    [Fact]
    public void Base_and_derived_references_compare_without_warnings()
    {
        if (!HasGcc) return;
        const string members = @"
public class Shape { public int Id; }
public class Square : Shape { }
public class Circle : Shape { }
public interface IHas { int Get(); }
public class Holder : IHas { public int Get() { return 3; } }
public class Special : Holder { }
static Shape Pick(bool b, Square s, Circle c) => b ? s : c;
";
        const string body = @"
Square sq = new Square { Id = 1 };
Circle ci = new Circle { Id = 2 };
Shape sh = sq;
Check(sh == sq && sq == sh && !(sh != sq) && ci != sh && sh != ci, ""base and derived references"");
Shape none = null;
Check(none != sq && sq != none && none == null, ""base null against derived"");
Check(Pick(true, sq, ci) == sq && Pick(false, sq, ci) == ci, ""derived classes through ?:"");
Shape p = sh == null ? ci : sq;
Check(p == sq, ""conditional with a derived branch"");
Shape q = none ?? ci;
Check(q == ci && (sh ?? ci) == sq, ""?? with base and derived"");
Shape r = null;
r ??= sq;
r ??= ci;
Check(r == sq, ""??= keeps the first value"");
string t = null;
Check((t ?? ""dflt"") == ""dflt"", ""?? on a string"");
Special sp = new Special();
Holder h = sp;
IHas ih = sp;
Check(h == sp && ih == sp && sp == ih && ih == h, ""interface and class references to the same object"");
";
        AssertAllOk(members, body, 8);
    }

    [Theory]
    [InlineData("", "var a = (1, \"x\"); var b = (1, \"x\"); bool e = a == b;")]
    [InlineData("public record R(string Name);", "R a = new R(\"x\"); R b = new R(\"x\"); bool e = a == b;")]
    [InlineData("", "object o = \"x\"; bool e = o is \"x\";")]
    [InlineData("", "string s = \"x\"; object o = s; bool e = s.Equals(o);")]
    [InlineData("", "string[] a = new string[2]; string m = a.Max();")]
    [InlineData("", "string[] a = new string[2]; string m = a.Min(x => x);")]
    [InlineData("", "int? a = null; int b = a ?? 3;")]
    public void String_and_record_equality_forms_that_cannot_be_compiled_report_ES0099(string members, string body)
    {
        var r = Compile("using System.Linq;\n" + Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
    }
}
