using System.Collections.Immutable;
using ESharp.Analyzers;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Array fields and field initializers in classes, lists of class references, members on temporaries, nested classes, interface methods
/// with ref/out and span parameters, static initializers that call code, Span.Empty, C keywords as names, Substring, and type tests on
/// interface values. Each program is compiled to C, built with the host gcc and run.
/// </summary>
public class ClassFeatureTests
{
    private static string Messages(CompileResult r, string id) =>
        string.Join("\n", r.Diagnostics.Where(d => d.Id == id).Select(d => d.GetMessage()));

    /// <summary>Runs <paramref name="checks"/> (calls of Check(bool, name)) and expects every check to pass.</summary>
    private static void RunChecks(string members, string checks, int expected)
    {
        var src = "using System;\n" + Program(members + @"
static int Ok;
static void Check(bool c, string name) { if (c) { Ok++; } else { Dos.PutStr(""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); } }",
            checks + $" Dos.PutStr(Ok == {expected} ? \"all ok\\n\" : \"some failed\\n\");");
        var c = CompileOk(src);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("all ok\n", output);
    }

    [Fact]
    public void Array_fields_of_a_class_live_in_the_object()
    {
        const string members = @"
public class Seat { public int Number; }
public class Table
{
    public Seat[] Seats = new Seat[4];
    public int[] Scores = new int[13];
    public byte[] Order = { 3, 1, 2 };
    public int Total() { int t = 0; for (int i = 0; i < Scores.Length; i++) t += Scores[i]; return t; }
}
static int Sum(Span<int> s) { int t = 0; foreach (int v in s) t += v; return t; }";
        const string body = @"
var t = new Table();
var a = new Table();
Check(t.Seats.Length == 4 && t.Scores.Length == 13 && t.Order.Length == 3, ""lengths"");
Check(t.Seats[2] == null && t.Scores[12] == 0, ""zeroed"");
Check(t.Order[0] == 3 && t.Order[1] == 1 && t.Order[2] == 2, ""initializer list"");
var s = new Seat(); s.Number = 7; t.Seats[1] = s;
t.Scores[0] = 5; t.Scores[12] = 6; a.Scores[0] = 100;
Check(t.Seats[1].Number == 7 && t.Total() == 11 && Sum(t.Scores) == 11, ""elements are the object's own"");
Check(a.Total() == 100 && a.Seats[1] == null, ""each object has its own arrays"");
int n = 0; foreach (Seat x in t.Seats) if (x != null) n++;
Check(n == 1, ""foreach over an array field"");";
        RunChecks(members, body, 6);
    }

    [Fact]
    public void Instance_field_initializers_run_in_CSharp_order()
    {
        // C#: field initializers of the class, then the base constructor (base initializers, base body), then the body.
        const string members = @"
static int Step;
static int Next() { return ++Step; }
public class Root { public int RootInit = Next(); public int RootBody; public Root() { RootBody = Next(); } }
public class Mid : Root { public int MidInit = Next(); public int[] Marks = { 7, 8 }; }
public class Leaf : Mid { public int LeafInit = Next(); public int LeafBody; public int Auto { get; set; } = 42; public Leaf() { LeafBody = Next(); } }
public class Plain { public int A = 5; public string Name = ""plain""; }
public class Other : Mid { public Other(int x) : base() { LeafLike = x; } public Other() : this(9) { } public int LeafLike; public int Own = Next(); }";
        const string body = @"
Step = 0;
var l = new Leaf();
Check(l.LeafInit == 1 && l.MidInit == 2 && l.RootInit == 3 && l.RootBody == 4 && l.LeafBody == 5, ""order"");
Check(l.Auto == 42 && l.Marks[1] == 8, ""auto-property and array initializers"");
var p = new Plain();
Check(p.A == 5 && p.Name == ""plain"", ""class without a constructor"");
Step = 0;
var o = new Other();
Check(o.Own == 1 && o.MidInit == 2 && o.RootInit == 3 && o.RootBody == 4 && o.LeafLike == 9, ""this(...) runs the initializers once"");";
        RunChecks(members, body, 4);
    }

    [Fact]
    public void Vec_holds_class_references()
    {
        const string members = @"
public class Card { public int Rank; public Card(int r) { Rank = r; } }";
        const string body = @"
var list = new Vec<Card>();
for (int i = 1; i <= 5; i++) list.Add(new Card(i * 10));
Check(list.Count == 5 && list[0].Rank == 10 && list[4].Rank == 50, ""add and index"");
var c3 = list[2];
Check(list.IndexOf(c3) == 2 && list.IndexOf(new Card(30)) == -1, ""IndexOf is by object"");
Check(list.Remove(c3) && list.Count == 4 && list[2].Rank == 40, ""Remove keeps the order"");
list.Insert(0, c3);
Check(list[0] == c3 && list[1].Rank == 10, ""Insert"");
int sum = 0; foreach (Card c in list) sum += c.Rank;
Check(sum == 150, ""foreach"");
list[1].Rank = 11;
Check(list[1].Rank == 11, ""a reference, not a copy"");
Check(list[99] == null, ""out of range reads null"");
Span<Card> span = list.AsSpan();
Check(span.Length == 5 && span[0] == c3, ""AsSpan"");
for (int i = 0; i < list.Count; i++) { var x = list[i]; Heap.Free(ref x); }
list.Destroy();
Check(list.Count == 0, ""Destroy"");";
        RunChecks(members, body, 9);
    }

    [Fact]
    public void Struct_members_work_on_temporaries()
    {
        const string members = @"
public struct Q { public int V; public int Twice => V * 2; public int Add(int k) { return V + k; } public void Bump() { V++; } }
static Q Make(int v) { var q = new Q(); q.V = v; return q; }
static int Calls;
static Q Counted() { Calls++; return Make(3); }";
        const string body = @"
Q[] items = new Q[3]; items[1].V = 4;
Span<Q> s = items;
ReadOnlySpan<Q> ro = items;
var v = new Vec<Q>(); v.Add(Make(6));
Check(items[1].Twice == 8 && s[1].Twice == 8 && ro[1].Twice == 8, ""property on elements"");
Check(Make(5).Twice == 10 && Make(5).Add(1) == 6, ""property and method on a call result"");
Check(v[0].Twice == 12 && v[0].Add(1) == 7, ""on a Vec element (a copy)"");
s[1].Bump();
Check(items[1].V == 5, ""a Span element changes in place"");
v[0].Bump();
Check(v[0].V == 6, ""a Vec element is a copy, as in C#"");
Calls = 0; int t = Counted().Twice;
Check(t == 6 && Calls == 1, ""the receiver is evaluated once"");
var big = new Vec<int>(); for (int i = 0; i < 10; i++) big.Add(i);
Span<int> mid = big.AsSpan().Slice(2, 3);
Span<int> tail = big.AsSpan().Slice(7);
Check(mid.Length == 3 && mid[0] == 2 && tail.Length == 3 && tail[2] == 9, ""Slice on a call result"");
big.Destroy(); v.Destroy();";
        RunChecks(members, body, 7);
    }

    [Fact]
    public void Nested_classes_compile()
    {
        const string members = @"
public class Outer
{
    public sealed class Inner { public int X; public int Twice() { return X * 2; } }
    public struct Pair { public int A, B; }
    public interface IThing { int Get(); }
    public class Thing : IThing { public int Get() { return 5; } }
    private static int Hidden = 3;
    public Inner Item;
    public Outer() { Item = new Inner(); Item.X = Hidden; }
}";
        const string body = @"
var o = new Outer();
var i = new Outer.Inner(); i.X = 4;
Outer.Pair p = new Outer.Pair(); p.A = 1; p.B = 2;
Outer.IThing t = new Outer.Thing();
Check(o.Item.X == 3 && i.Twice() == 8 && p.A + p.B == 3 && t.Get() == 5, ""nested types"");";
        RunChecks(members, body, 1);
    }

    [Fact]
    public void Interface_methods_take_ref_out_and_in_parameters()
    {
        const string members = @"
public struct Big { public int A, B, C; }
public interface IParser { bool TryParse(int input, out int value); void Bump(ref int x); int Sum(in Big b); }
public sealed class Doubler : IParser
{
    public bool TryParse(int input, out int value) { value = input * 2; return input > 0; }
    public void Bump(ref int x) { x += 10; }
    public int Sum(in Big b) { return b.A + b.B + b.C; }
}
public class Shape { public virtual bool Measure(out int area) { area = 1; return false; } }
public class Square : Shape { public int Side; public override bool Measure(out int area) { area = Side * Side; return true; } }";
        const string body = @"
IParser p = new Doubler();
int w = 1;
p.Bump(ref w);
Check(p.TryParse(3, out int v) && v == 6 && w == 11, ""out and ref"");
Check(!p.TryParse(-1, out _), ""discard"");
var big = new Big(); big.A = 1; big.B = 2; big.C = 3;
Check(p.Sum(in big) == 6 && p.Sum(big) == 6, ""in"");
var sq = new Square(); sq.Side = 4; Shape sh = sq;
Check(sh.Measure(out int area) && area == 16, ""out through a virtual method"");";
        RunChecks(members, body, 4);
    }

    [Fact]
    public void Interface_and_virtual_methods_take_spans_of_structs()
    {
        // The function tables of interfaces and virtual methods are declared after the span typedefs they use.
        const string members = @"
public struct Pt { public int X; }
public interface ISum { int Sum(ReadOnlySpan<Pt> ps); }
public sealed class Summer : ISum { public int Sum(ReadOnlySpan<Pt> ps) { int t = 0; foreach (Pt p in ps) t += p.X; return t; } }
public interface IUnused { Span<Pt> Pick(Span<Pt> ps, (Pt, int) extra); }
public class Base { public virtual int Count(ReadOnlySpan<Pt> ps) { return ps.Length; } }
public class Derived : Base { public override int Count(ReadOnlySpan<Pt> ps) { return ps.Length * 10; } }";
        const string body = @"
Pt[] a = new Pt[3]; a[0].X = 1; a[1].X = 2; a[2].X = 3;
ISum s = new Summer();
Base b = new Derived();
Check(s.Sum(a) == 6 && b.Count(a) == 30, ""span parameters through tables"");";
        RunChecks(members, body, 1);
    }

    [Fact]
    public void Static_initializers_may_call_code()
    {
        const string members = @"
public class Game { public int Seed; public int Players; public Game(int players, int seed) { Players = players; Seed = seed; } }
public struct Config { public int Players; public static Config Make() { var c = new Config(); c.Players = 4; return c; } }
static int Seven() { return 7; }
static int A = Seven();
static int B = A + 1;
static Game G = new Game(Config.Make().Players, (int)Seed);
static uint Seed = 12345u;
static Func<int, int> Twice = x => x * 2;
static int[] Table = { Seven(), 2, Seven() * 3 };
static int Later { get; set; } = Seven() * 10;
static int FromCctor;
static P() { FromCctor = A * 2; }
static int Constant = 5;";
        const string body = @"
Check(A == 7 && B == 8, ""a call, and a field that reads it"");
Check(G.Players == 4 && G.Seed == 12345, ""new of a class with a struct method call"");
Check(Twice(21) == 42, ""a delegate"");
Check(Table[0] == 7 && Table[1] == 2 && Table[2] == 21, ""array elements"");
Check(Later == 70 && FromCctor == 14 && Constant == 5, ""auto-property, static constructor after the initializers"");";
        var c = CompileOk("using System;\n" + Program(members + "\nstatic int Ok;\nstatic void Check(bool c, string name) { if (c) { Ok++; } else { Dos.PutStr(\"FAIL \"); Dos.PutStr(name); Dos.PutStr(\"\\n\"); } }",
            body + " Dos.PutStr(Ok == 5 ? \"all ok\\n\" : \"some failed\\n\");"));
        Assert.Contains("static int32_t Es_P_Constant = 5;", c);   // constants stay C initializers
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("all ok\n", output);
    }

    [Fact]
    public void Span_Empty_and_IsEmpty()
    {
        const string members = @"
static ReadOnlySpan<int> None() { return ReadOnlySpan<int>.Empty; }
static Span<byte> NoBytes() { return Span<byte>.Empty; }";
        const string body = @"
int[] a = new int[2];
Span<int> s = a;
Check(None().Length == 0 && None().IsEmpty && NoBytes().IsEmpty, ""empty"");
Check(!s.IsEmpty && s.Slice(2).IsEmpty, ""not empty"");";
        RunChecks(members, body, 2);
    }

    [Fact]
    public void C_keywords_and_predefined_macros_are_renamed()
    {
        const string members = @"
public enum Mode { auto, manual }
public struct Reg { public int register; public int signed; }
public class Box { public int unsigned; public bool inline { get; set; } }
static int Draw(bool auto, int register, int signed, int amiga) { int unsigned = 1; int @long = 2; int TRUE = 3; return (auto ? 1 : 0) + register + signed + unsigned + @long + amiga + TRUE; }";
        const string body = @"
var r = new Reg { register = 4, signed = 5 };
var b = new Box(); b.unsigned = 6; b.inline = true;
Check(Draw(true, 1, 1, 0) == 9 && Draw(auto: false, register: 2, signed: 0, amiga: 1) == 9, ""parameters and locals"");
Check(r.register + r.signed == 9 && b.unsigned == 6 && b.inline, ""fields and properties"");
string m = $""{Mode.auto}"";
Check(m == ""auto"", ""an enum member keeps its name"");";
        RunChecks(members, body, 3);
    }

    [Fact]
    public void Substring_copies_into_the_format_ring_and_clamps()
    {
        const string body = @"
string t = ""hello world"";
Check(t.Substring(0, 5) == ""hello"" && t.Substring(6) == ""world"", ""substrings"");
Check(t.Substring(6, 99) == ""world"" && t.Substring(20) == """" && t.Substring(-3, 2) == ""he"", ""clamped, not thrown"");
Check(t.Substring(3, 0).Length == 0 && t.Substring(4, 3) == ""o w"", ""lengths"");";
        RunChecks("", body, 3);
    }

    [Fact]
    public void String_search_methods_are_ordinal_and_never_throw()
    {
        const string body = @"
string t = ""hello world"";
string e = """";
Check(t.IndexOf('o') == 4 && t.IndexOf('o', 5) == 7 && t.IndexOf('h') == 0 && t.IndexOf('d') == 10, ""IndexOf char"");
Check(t.IndexOf('z') == -1 && t.IndexOf('o', 8) == -1 && e.IndexOf('a') == -1, ""IndexOf char not found"");
Check(t.IndexOf(""world"") == 6 && t.IndexOf(""hello"") == 0 && t.IndexOf(""o w"") == 4 && t.IndexOf(""lo"", 4) == -1, ""IndexOf string"");
Check(t.IndexOf(""l"", 4) == 9 && t.IndexOf(""xyz"") == -1 && t.IndexOf(""hello world!"") == -1, ""IndexOf string more"");
Check(t.IndexOf("""") == 0 && t.IndexOf("""", 11) == 11 && e.IndexOf("""") == 0 && e.IndexOf(""a"") == -1, ""empty values and strings"");
Check(t.IndexOf('o', 11) == -1 && t.IndexOf(""o"", 12) == -1 && t.IndexOf(""o"", -5) == 4, ""startIndex beyond the end is -1, below 0 is 0"");
Check(t.IndexOf(""World"", StringComparison.Ordinal) == -1 && t.IndexOf(""world"", StringComparison.Ordinal) == 6, ""Ordinal"");
Check(t.LastIndexOf('o') == 7 && t.LastIndexOf('h') == 0 && t.LastIndexOf('z') == -1 && e.LastIndexOf('a') == -1, ""LastIndexOf char"");
Check(t.LastIndexOf(""o"") == 7 && t.LastIndexOf(""hello"") == 0 && t.LastIndexOf(""world"") == 6 && t.LastIndexOf(""xx"") == -1 && t.LastIndexOf("""") == 11, ""LastIndexOf string"");
Check(t.Contains(""lo w"") && !t.Contains(""low"") && t.Contains("""") && e.Contains("""") && !e.Contains(""a"") && t.Contains('w') && !t.Contains('q'), ""Contains"");
Check(t.StartsWith(""hello"") && !t.StartsWith(""world"") && t.StartsWith("""") && !e.StartsWith(""h"") && t.StartsWith('h') && !t.StartsWith('e') && !e.StartsWith('h'), ""StartsWith"");
Check(t.EndsWith(""world"") && !t.EndsWith(""hello"") && t.EndsWith("""") && !e.EndsWith(""d"") && t.EndsWith('d') && !t.EndsWith('l') && !e.EndsWith('d'), ""EndsWith"");
Check(t.StartsWith(""hello world"") && !t.StartsWith(""hello world!"") && t.EndsWith(""hello world"") && !t.EndsWith(""!hello world"") && t.StartsWith(""h"", StringComparison.Ordinal), ""whole string and longer values"");";
        RunChecks("", body, 13);
    }

    [Fact]
    public void String_search_with_other_comparisons_is_ES0099()
    {
        var r = Compile("using System;\n" + Program("", "string t = \"abc\"; int i = t.IndexOf(\"B\", StringComparison.OrdinalIgnoreCase);"));
        Assert.Contains("ES0099", ErrorIds(r));
        Assert.Contains("string.IndexOf", Messages(r, "ES0099"));
    }

    [Fact]
    public void Unmapped_library_members_are_ES0099_not_link_errors()
    {
        var call = Compile("using System;\n" + Program("", "string t = \"abc\"; string u = t.Trim();"));
        Assert.Contains("ES0099", ErrorIds(call));
        Assert.Contains("string.Trim", Messages(call, "ES0099"));

        var prop = Compile("using System;\n" + Program("", "int n = Array.MaxLength;"));
        Assert.Contains("ES0099", ErrorIds(prop));
    }

    [Fact]
    public void Type_tests_on_interface_values()
    {
        const string members = @"
public interface IShape { int Area(); }
public sealed class Sq : IShape { public int Side; public int Area() { return Side * Side; } }
public class Circle : IShape { public virtual int Area() { return 3; } }
public class Ring : Circle { public override int Area() { return 2; } }
public class Blob { }
static string Name(IShape s) => s switch { Sq q => ""square "" + q.Side, Ring => ""ring"", Circle => ""circle"", null => ""none"", _ => ""other"" };";
        const string body = @"
var sq = new Sq(); sq.Side = 3;
IShape s = sq;
IShape c = new Circle();
Circle asCircle = new Ring();
IShape r = asCircle;             // converted while typed as the base class
IShape none = null;
Check(s is Sq && !(c is Sq) && !(none is Sq), ""is"");
Check(s is Sq q && q.Side == 3 && q.Area() == 9, ""is with a binding"");
Check(r is Ring && r is Circle && c is Circle && !(c is Ring), ""derived classes, also through a base-typed conversion"");
Check((s as Sq) == sq && (c as Sq) == null && (r as Ring) != null, ""as"");
Check(Name(s) == ""square 3"" && Name(r) == ""ring"" && Name(c) == ""circle"" && Name(none) == ""none"", ""switch patterns"");
Check(s is Sq { Side: 3 } && !(s is Sq { Side: 4 }), ""property pattern"");";
        RunChecks(members, body, 6);
    }

    [Fact]
    public void Analyzer_accepts_ESharp_library_generics_from_a_referenced_assembly()
    {
        // A project made by 'esharp new' references ESharp.Platform as an assembly: Heap.Free<T> is then not in source.
        const string src = "using ESharp.Platform;\npublic class Foo { }\npublic static class P { public static void Run() { Foo f = new Foo(); Heap.Free(ref f); } }";
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileNameWithoutExtension(p) is "System.Private.CoreLib" or "System.Runtime")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ESharp.Platform.Heap).Assembly.Location));
        var compilation = CSharpCompilation.Create("host", new[] { CSharpSyntaxTree.ParseText(src) }, trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var found = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SubsetAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        Assert.DoesNotContain(found, d => d.Id == "ES0008");

        // A generic method of the .NET library is still reported.
        var other = CSharpCompilation.Create("host2",
            new[] { CSharpSyntaxTree.ParseText("public static class P { public static void Run() { int[] a = System.Array.Empty<int>(); } }") }, trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var flagged = other.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SubsetAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        Assert.Contains(flagged, d => d.Id == "ES0008");
    }

    [Fact]
    public void Host_memory_fake_is_thread_safe()
    {
        // Two xunit tests that use Vec at the same time used to corrupt the fake's block table.
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                var v = new ESharp.Platform.Vec<int>();
                v.Add(i);
                v.Add(i + 1);
                v.Destroy();
            }
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
    }

    [Theory]
    [InlineData(76, -320)]
    [InlineData(9, 0)]
    [InlineData(-7, 3)]
    [InlineData(0, 0)]
    [InlineData(400, 1)]
    public void Relative_input_moves_add_up_on_both_axes(int dx, int dy)
    {
        var steps = ESharp.Cli.Xtest.RelSteps(dx, dy);
        Assert.Equal(dx, steps.Sum(s => s.Dx));
        Assert.Equal(dy, steps.Sum(s => s.Dy));
        Assert.All(steps, s => Assert.True(Math.Abs(s.Dx) <= 5 && Math.Abs(s.Dy) <= 5 || steps.Count == 1));
        if (dx == 76 && dy == -320) Assert.Contains(steps, s => s.Dx != 0);   // the short axis is not dropped
    }
}
