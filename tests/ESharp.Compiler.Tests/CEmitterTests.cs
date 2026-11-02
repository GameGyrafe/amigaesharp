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
        Assert.Contains("P_Main();", c);
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

    [Fact]
    public void Types_map_to_stdint()
    {
        var c = CompileOk(Program(@"static void F(byte a, sbyte b, short c, ushort d, int e, uint f) { }"));
        Assert.Contains("void P_F(uint8_t a, int8_t b, int16_t c, uint16_t d, int32_t e, uint32_t f)", c);
    }

    [Fact]
    public void Overloads_get_distinct_names()
    {
        var c = CompileOk(Program("static int F(int a) { return a; } static int F(short a) { return a; }", "F(1); F((short)2);"));
        Assert.Contains("P_F_int", c);
        Assert.Contains("P_F_short", c);
    }

    [Fact]
    public void Unsupported_constructs_report_ES0099()
    {
        var r = Compile(Program("static void F() { int x = 1; ref int r = ref x; r = 2; }"));
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
        var inherit = Compile(Program("class A { public int X; } class B : A { }"));
        Assert.Contains("ES0007", ErrorIds(inherit));
        var virt = Compile(Program("class A { public virtual int F() { return 1; } }"));
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
    public void Lambda_that_captures_a_local_reports_ES0099()
    {
        var r = Compile(Program("static int Run(System.Func<int, int> f) { return f(1); }", "int k = 3; Dos.PutStr(Run(x => x + k) == 4 ? \"y\" : \"n\");"));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Fact]
    public void Local_function_that_captures_a_local_reports_ES0099()
    {
        var r = Compile(Program("", "int x = 3; int Get() { return x; } Dos.PutStr(Get() == 3 ? \"y\" : \"n\");"));
        Assert.Contains("ES0099", ErrorIds(r));
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
    public void Arrays_become_c_arrays_and_spans_become_pointer_and_length()
    {
        var c = CompileOk(Program(
            "static int[] T = { 1, 2, 3 }; static int Sum(Span<int> s) { return s.Length > 0 ? s[0] : 0; }",
            "int[] a = new int[4]; Sum(a);").Replace("using ESharp;", "using System;\nusing ESharp;"));
        Assert.Contains("static int32_t P_T[3] = { 1, 2, 3 };", c);
        Assert.Contains("int32_t a[4] = {0};", c);
        Assert.Contains("typedef struct { int32_t *ptr; int32_t length; } ES_Span_int;", c);
        Assert.Contains("s.ptr[0]", c);
    }

    [Fact]
    public void Operators_on_structs_become_functions()
    {
        var c = CompileOk(Program("", "Fix a = Fix.FromInt(2); Fix b = a * a + a;"));
        Assert.Contains("Fix_op_Multiply_Fix_Fix(a, a)", c);
        Assert.Contains("Fix_op_Addition(", c);
    }

    [Fact]
    public void Struct_instance_methods_take_a_pointer_and_constructors_return_the_struct()
    {
        var c = CompileOk(Program(
            "struct S { public int V; public S(int v) { V = v; } public int Get() { return V; } }",
            "S s = new S(3); s.Get();"));
        Assert.Contains("P_S P_S_new(int32_t v)", c);
        Assert.Contains("int32_t P_S_Get(P_S *_this)", c);
        Assert.Contains("_this->V", c);
        Assert.Contains("P_S_Get(&s)", c);
    }

    [Fact]
    public void Calls_on_temporaries_report_ES0099()
    {
        var r = Compile(Program(
            "struct S { public int V; public int Get() { return V; } } static S Make() { return default; }",
            "Make().Get();"));
        Assert.Contains("ES0099", ErrorIds(r));
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
        Assert.Contains("Pair__Fix", c);
        Assert.DoesNotContain("Pair_T", c);
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("ok\n", output);
    }
}
