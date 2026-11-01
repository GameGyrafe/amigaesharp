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
}
