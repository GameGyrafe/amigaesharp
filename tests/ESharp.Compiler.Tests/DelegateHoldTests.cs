using ESharp.Target.Host;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Issue #175: shapes the delegate copy check (#171) refused, now built with the list held while code of the program runs: a delegate
/// variable passed to a borrowed parameter, called with arguments that run code, given to an inlined LINQ operator, the target of a
/// += / -= whose operand runs code, and d is { }. Each program runs on dotnet and as C under AddressSanitizer, LeakSanitizer and UBSan,
/// with the same output, including adversarial versions where the callee, the argument or the operand does += / -= / Heap.Free on the
/// same delegate. Also the two holes #176 left for a variable that is only freed: a call through a borrowed parameter and through an
/// inlined LINQ operator.
/// </summary>
public class DelegateHoldTests
{
    private const string Check = @"static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }";

    private static string OkLines(params string[] names) => string.Concat(names.Select(n => "ok " + n + "\n"));

    /// <summary>The program on dotnet (the host build of Runtime and Platform) and its C under the sanitizers: the output of both. Returns the C.</summary>
    private static string RunBoth(string members, string body, string expected)
    {
        var src = "using System.Linq;\n" + Program(members + Check, body);
        var compilation = CSharpCompilation.Create("holds", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences().Append(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using (var ms = new MemoryStream())
        {
            var emit = compilation.Emit(ms);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            var asm = System.Reflection.Assembly.Load(ms.ToArray());
            using var console = HostConsole.Capture();
            asm.GetType("P")!.GetMethod("Main")!.Invoke(null, null);
            Assert.Equal(expected, console.Output);
        }
        var c = CompileOk(src);
        if (!HasGcc) return c;
        var (code, output) = HasSanitizers ? RunSanitized(c) : RunWithHostGcc(c);
        Assert.Equal(expected, output);
        Assert.Equal(0, code);
        return c;
    }

    private static string Fn(string c, string head)
    {
        var start = c.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, head);
        return c.Substring(start, c.IndexOf("\n}\n", start, StringComparison.Ordinal) - start);
    }

    private const string Common = @"
static int log;
static void A() { log = log * 10 + 1; }
static void B() { log = log * 10 + 2; }
static void C() { log = log * 10 + 3; }
static System.Action MakeD(int k) { return () => log = log * 10 + k; }
";

    private const string WithField = Common + "static System.Action Fld;\n";

    private const string BorrowedMembers = WithField + @"
static void Fire(System.Action a) { a(); }
static void FireGrow(System.Action a) { Fld += C; a(); }
static void FireFree(System.Action a) { Heap.Free(ref Fld); a(); }
static void FireSub(System.Action a) { Fld -= Fld; a(); }
static void FireBoth(System.Action a, System.Action b) { Heap.Free(ref Fld); a(); b(); }
static int FireCount(System.Action a) { Heap.Free(ref Fld); a(); return 5; }
static void Run(System.Action a, System.Action g) { g(); a(); }
static int Bump() { Fld += C; return 7; }
static void Mixed(int k, System.Action a) { log = log * 10 + k; a(); }
static void Mixed2(System.Action a, int k) { log = log * 10 + k; a(); }
public class H { public System.Action X; public void Run(System.Action a) { X += C; a(); } }
public delegate void Runner(System.Action a);
static void Setup() { Heap.Free(ref Fld); Fld = MakeD(1); System.Action x = MakeD(2); Fld += x; Heap.Free(ref x); }
static void Local()
{
    System.Action d = MakeD(1); System.Action x = MakeD(2); d += x; Heap.Free(ref x);
    Run(d, () => { d += C; });
    Check(log == 12, ""a local given to a borrowed parameter, grown by another argument inside the call"");
    log = 0; d(); Check(log == 123, ""the local has the new entry afterwards"");
    Runner loc = a => { d -= d; a(); };
    log = 0; loc(d); Check(log == 123 && d == null, ""a lambda empties the local it was given"");
    Heap.Free(ref d);
}
";

    /// <summary>(a) Fire(d): the callee (or another argument's code) does += / -= / Heap.Free on d, and the list it was given still runs.</summary>
    [Fact]
    public void A_delegate_given_to_a_borrowed_parameter_is_held_until_the_call_returns()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
Setup(); log = 0; Fire(Fld); Check(log == 12, ""a field given to a borrowed parameter"");
log = 0; FireGrow(Fld); Check(log == 12, ""the callee grows the field: the list it was given runs"");
log = 0; Fld(); Check(log == 123, ""the field has the new entry"");
Setup(); log = 0; FireFree(Fld); Check(log == 12 && Fld == null, ""the callee frees the field"");
Setup(); log = 0; FireSub(Fld); Check(log == 12 && Fld == null, ""the callee empties the field with -="");
Setup(); log = 0; FireBoth(Fld, Fld); Check(log == 1212, ""the field given twice, freed inside"");
Setup(); log = 0; int r = FireCount(Fld); Check(r == 5 && log == 12, ""a call whose value is read"");
Setup(); log = 0; Mixed(Bump(), Fld); Check(log == 7123, ""an argument before it runs first and grows the field"");
Setup(); log = 0; Mixed2(Fld, Bump()); Check(log == 712, ""an argument after it grows the field: the list loaded first runs"");
Setup(); log = 0; Heap.Free(ref Fld);
var h = new H(); h.X = MakeD(4); System.Action hx = MakeD(5); h.X += hx; Heap.Free(ref hx);
log = 0; h.Run(h.X); Check(log == 45, ""an instance method grows the field of its object"");
Heap.Free(ref h.X); Heap.Free(ref h);
Runner run = a => { Heap.Free(ref Fld); a(); };
Setup(); log = 0; run(Fld); Check(log == 12 && Fld == null, ""a delegate call frees the field it was given"");
log = 0; Local();
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        var c = RunBoth(BorrowedMembers, body, OkLines("a field given to a borrowed parameter", "the callee grows the field: the list it was given runs",
            "the field has the new entry", "the callee frees the field", "the callee empties the field with -=", "the field given twice, freed inside",
            "a call whose value is read", "an argument before it runs first and grows the field",
            "an argument after it grows the field: the list loaded first runs", "an instance method grows the field of its object",
            "a delegate call frees the field it was given",
            "a local given to a borrowed parameter, grown by another argument inside the call", "the local has the new entry afterwards",
            "a lambda empties the local it was given", "nothing leaks"));
        Assert.Contains("es_frame_hold(_ha", c);
    }

    private const string FreedMembers = @"
static int log;
static System.Action G2;
static System.Func<int, bool> Pr;
static System.Func<int, int> Sel;
static System.Action MakeFreer() { int n = 5; return () => { Heap.Free(ref G2); log = log * 10 + n; }; }
static System.Func<int, bool> MakePr() { int lim = 2; return x => { Heap.Free(ref Pr); log = log * 10 + x; return x < lim; }; }
static System.Func<int, int> MakeSel() { int k = 10; return x => { Heap.Free(ref Sel); return x * k; }; }
static void RunIt(System.Action a) { a(); }
static int[] Arr = { 1, 2, 3 };
static int Early() { foreach (var v in Arr.Where(Pr)) { if (v == 1) return v; } return 0; }
";

    /// <summary>
    /// The holes #176 left for a variable that is only freed: called through a borrowed parameter (RunIt(G2)) and through an inlined LINQ
    /// operator (arr.Count(Pr)), where the running closure frees the variable and then reads what it captured.
    /// </summary>
    [Fact]
    public void A_freed_only_delegate_is_held_through_a_borrowed_parameter_and_an_inlined_LINQ_operator()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
int[] arr = { 1, 2, 3 };
G2 = MakeFreer(); log = 0; RunIt(G2); Check(log == 5 && G2 == null, ""a closure that frees its variable, run through a borrowed parameter"");
Pr = MakePr(); log = 0; int n = arr.Count(Pr); Check(n == 1 && log == 123 && Pr == null, ""Count: every element still goes to the predicate it was given"");
Sel = MakeSel(); int s = arr.Select(Sel).Sum(); Check(s == 60 && Sel == null, ""Select then Sum"");
Pr = MakePr(); log = 0; int k = 0; foreach (var v in arr.Where(Pr)) k += v; Check(k == 1 && log == 123, ""foreach over Where"");
Pr = MakePr(); log = 0; Check(Early() == 1 && log == 1, ""a return out of the foreach body drops the hold"");
Heap.Free(ref Pr);
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        var c = RunBoth(FreedMembers, body, OkLines("a closure that frees its variable, run through a borrowed parameter",
            "Count: every element still goes to the predicate it was given", "Select then Sum", "foreach over Where",
            "a return out of the foreach body drops the hold", "nothing leaks"));
        Assert.Contains("cleanup(es_frame_drop))) = _q", Fn(c, "P_Early("));
    }

    private const string LinqMembers = Common + @"
static System.Func<int, bool> Q;
static System.Action Hook;
static System.Func<int, bool> MakeQ(int k) { return x => { log = log * 10 + k; Hook(); return x > 1; }; }
static void SetupQ() { Heap.Free(ref Q); Q = MakeQ(1); System.Func<int, bool> y = MakeQ(2); Q += y; Heap.Free(ref y); }
";

    /// <summary>A multicast predicate with += / -= given to an inlined LINQ operator; an entry empties or grows it while the loop runs.</summary>
    [Fact]
    public void A_delegate_with_plus_equals_given_to_an_inlined_LINQ_operator_is_held_for_the_loop()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
int[] arr = { 1, 2, 3 };
Hook = () => { };
SetupQ(); log = 0; Check(arr.Count(Q) == 2 && log == 121212, ""Count with a multicast predicate (the last result counts)"");
Heap.Free(ref Hook); Hook = () => Q -= Q;
SetupQ(); log = 0; Check(arr.Count(Q) == 2 && log == 121212 && Q == null, ""an entry empties the predicate: the loop keeps the list it was given"");
Heap.Free(ref Hook); Hook = () => { System.Func<int, bool> z = MakeQ(3); Q += z; Heap.Free(ref z); };
SetupQ(); Q -= Q; Q = MakeQ(1); log = 0; Check(arr.Where(Q).Count() == 2 && log == 111, ""an entry grows the predicate: the new entries wait for the next loop"");
Heap.Free(ref Hook); Hook = () => { };
Heap.Free(ref Q);
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(LinqMembers, body, OkLines("Count with a multicast predicate (the last result counts)",
            "an entry empties the predicate: the loop keeps the list it was given", "an entry grows the predicate: the new entries wait for the next loop",
            "nothing leaks"));
    }

    private const string ArgMembers = @"
static int log;
static System.Action<int> G;
static System.Func<int, int> F;
static System.Action<int> Changed;
static System.Action<int, int> P2;
static int getterRuns, next;
static System.Action<int> MakeG(int k) { return x => log = log * 10 + k + x; }
static System.Func<int, int> MakeF() { int n = 10; return x => x + n; }
static int Grow() { System.Action<int> y = MakeG(5); G += y; Heap.Free(ref y); return 1; }
static int Arg() { Heap.Free(ref F); return 4; }
static int Score { get { getterRuns++; System.Action<int> y = MakeG(7); Changed += y; Heap.Free(ref y); return 3; } }
static int Next() { return ++next; }
static int Empty() { G -= G; return 2; }
";

    /// <summary>(b) d(Get()), d.Invoke(...) and d?.Invoke(Score): d is loaded and held first, then the arguments run in order, as in C#.</summary>
    [Fact]
    public void A_call_whose_arguments_run_code_loads_and_holds_the_delegate_first()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
G = MakeG(1); System.Action<int> y = MakeG(2); G += y; Heap.Free(ref y);
log = 0; G(Grow()); Check(log == 23, ""an argument grows the delegate: the list loaded first runs"");
log = 0; G(0); Check(log == 125, ""the new entry runs next time"");
log = 0; G.Invoke(Empty()); Check(log == 347 && G == null, "".Invoke with an argument that empties the delegate"");
F = MakeF(); int r = F(Arg()); Check(r == 14 && F == null, ""an argument frees a Func: the call still runs and reads its closure"");
Changed = MakeG(1); getterRuns = 0; log = 0; Changed?.Invoke(Score); Check(log == 4 && getterRuns == 1, ""?.Invoke with a property that has a body"");
log = 0; Changed?.Invoke(0); Check(log == 17, ""the property's entry runs next time"");
Heap.Free(ref Changed); getterRuns = 0; Changed?.Invoke(Score); Check(getterRuns == 0, ""?.Invoke on null runs no argument"");
P2 = (int a, int b) => log = log * 100 + a * 10 + b; System.Action<int, int> p = (int a, int b) => { }; P2 += p; Heap.Free(ref p);
log = 0; next = 0; P2(Next(), Next()); Check(log == 12, ""two arguments that run code run in order"");
Heap.Free(ref P2);
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(ArgMembers, body, OkLines("an argument grows the delegate: the list loaded first runs", "the new entry runs next time",
            ".Invoke with an argument that empties the delegate", "an argument frees a Func: the call still runs and reads its closure",
            "?.Invoke with a property that has a body", "the property's entry runs next time", "?.Invoke on null runs no argument",
            "two arguments that run code run in order", "nothing leaks"));
    }

    private const string CompoundMembers = WithField + @"
static System.Action Make() { System.Action x = MakeD(2); Fld += x; Heap.Free(ref x); return MakeD(3); }
static System.Action Pick() { Fld += C; return A; }
static System.Action FreeAndMake() { Heap.Free(ref Fld); return MakeD(4); }
static bool Bump2() { Fld += C; return true; }
static System.Action Plain() { return MakeD(6); }
static System.Action GrowVia(System.Action g) { g(); return MakeD(3); }
static void LocalCompound()
{
    System.Action d = MakeD(1);
    System.Action grow = () => { System.Action x = MakeD(2); d += x; Heap.Free(ref x); };
    d += GrowVia(grow); log = 0; d(); Check(log == 13, ""a local: the operand's += on it is lost"");
    Heap.Free(ref d);
}
";

    /// <summary>(c) d += Make() and d -= Pick(): C#'s order, the old value loaded and held, then the operand; a += the operand makes is lost, as in dotnet.</summary>
    [Fact]
    public void A_compound_whose_operand_runs_code_holds_the_old_value()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
Fld = MakeD(1); Fld += Make(); log = 0; Fld(); Check(log == 13, ""the operand grows the field: that entry is lost, as in dotnet"");
Heap.Free(ref Fld); Fld = A; Fld += B; Fld -= Pick(); log = 0; Fld(); Check(log == 2, ""-= with an operand that grows the field"");
Heap.Free(ref Fld); Fld = MakeD(1); Fld += FreeAndMake(); log = 0; Fld(); Check(log == 14, ""the operand frees the field"");
Heap.Free(ref Fld); Fld = MakeD(1); Fld += (Bump2() ? Fld : null); log = 0; Fld(); Check(log == 113, ""the operand grows the field, then is the field"");
Heap.Free(ref Fld); Fld = MakeD(5); Fld += Plain(); log = 0; Fld(); Check(log == 56, ""a call that returns a new lambda"");
Heap.Free(ref Fld);
LocalCompound();
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(CompoundMembers, body, OkLines("the operand grows the field: that entry is lost, as in dotnet", "-= with an operand that grows the field",
            "the operand frees the field", "the operand grows the field, then is the field", "a call that returns a new lambda",
            "a local: the operand's += on it is lost", "nothing leaks"));
    }

    /// <summary>(d) d is { } and d is not { }: pointer tests that keep nothing.</summary>
    [Fact]
    public void Property_patterns_without_clauses_are_null_tests()
    {
        const string body = @"
System.Action d = MakeD(1); System.Action x = MakeD(2); d += x; Heap.Free(ref x);
bool some = d is { }, none = d is not { }, disc = d is { } _;
Check(some && !none && disc, ""d is { }, d is not { } and d is { } _"");
d -= d; Check(!(d is { }) && d is not { }, ""after -= d: null"");
Heap.Free(ref d);
";
        RunBoth(Common, body, OkLines("d is { }, d is not { } and d is { } _", "after -= d: null"));
    }

    [Theory]
    // A borrowed argument whose call shape is not covered: an in parameter, a named argument, a receiver that runs code,
    // a constructor, and a delegate call with code before the held argument.
    [InlineData("static void Use(in System.Action a) { a(); }", "Fld = A; Fld += B; Use(in Fld);")]
    [InlineData("static void Use(System.Action a) { a(); }", "Fld = A; Fld += B; Use(a: Fld);")]
    [InlineData("public class R { public void Use(System.Action a) { a(); } } static R Get() { return new R(); }", "Fld = A; Fld += B; Get().Use(Fld);")]
    [InlineData("public class R { public R(System.Action a) { a(); } }", "Fld = A; Fld += B; var r = new R(Fld);")]
    [InlineData("static int N() { return 1; }", "System.Action<int, System.Action> run = (int k, System.Action a) => a(); Fld = A; Fld += B; run(N(), Fld);")]
    // An [Owned] argument with code next to it, a designation, and a LINQ step whose earlier link runs code.
    [InlineData("static int N() { return 1; } static void Take(int k, [Owned] System.Action a) { Heap.Free(ref a); }", "Fld = A; Fld += B; Take(N(), Fld);")]
    [InlineData("", "Fld = A; Fld += B; if (Fld is { } e) e();")]
    [InlineData("static System.Func<int, bool> Q; static int N() { return 1; }", "Q = (int x) => x > 0; Q += (int x) => x > 1; int[] arr = { 1, 2 }; int n = arr.Take(N()).Count(Q);")]
    public void Shapes_not_covered_stay_ES0099(string members, string body)
    {
        var r = Compile("using System.Linq;\n" + Program(WithField + members, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>A variable that no += / -= / Heap.Free can release keeps the C it had: no hold for a borrowed argument or a call.</summary>
    [Fact]
    public void A_delegate_nothing_releases_is_passed_and_called_as_before()
    {
        var c = CompileOk(Program(@"
static int log;
static System.Action<int> Keep = (int x) => log += x;
static void Fire(System.Action<int> a) { a(1); }
static int N() { return 2; }
static void Use() { Fire(Keep); Keep(N()); }
", "Use();"));
        Assert.DoesNotContain("es_frame_hold(", Fn(c, "P_Use(void)\n{"));
    }
}
