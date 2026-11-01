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
        var c = CompileOk(Program("static int F(int a) { return a; } static int F(short a) { return a; }"));
        Assert.Contains("P_F_int", c);
        Assert.Contains("P_F_short", c);
    }

    [Fact]
    public void Unsupported_constructs_report_ES0099()
    {
        var r = Compile(Program("static void F() { var s = \"a\" + \"b\"; string t = s + s; }"));
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
    public void Property_assignment_used_as_a_value_reports_ES0099()
    {
        var r = Compile(Program(
            "struct S { int _v; public int V { get { return _v; } set { _v = value; } } }",
            "S s = default; int x = (s.V = 3);"));
        Assert.Contains("ES0099", ErrorIds(r));
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
    public void Constructor_initializers_report_ES0099()
    {
        var r = Compile(Program("struct S { public int V; public S(int v) : this() { V = v; } }"));
        Assert.Contains("ES0099", ErrorIds(r));
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
