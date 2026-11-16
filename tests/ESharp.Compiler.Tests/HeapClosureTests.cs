using ESharp.Target.Host;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Heap closures and events (issue #41): stored capturing lambdas, compiled to C and run with the host gcc.</summary>
public class HeapClosureTests
{
    private static string Check => @"static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }";

    private static string OkLines(params string[] names) => string.Concat(names.Select(n => "ok " + n + "\n"));

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "closures", "Closures.cs");

    [Fact]
    public void Closures_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        ClosuresSample.Program.Main();
        Assert.Equal(ClosuresExpected.Output, console.Output);
    }

    [Fact]
    public void Closures_sample_compiles_and_runs_natively_with_the_same_output_as_dotnet()
    {
        if (!HasGcc) return;
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(SamplePath, File.ReadAllText(SamplePath)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(ClosuresExpected.Output, output);
    }

    [Fact]
    public void Closures_sample_frees_everything_under_address_and_leak_sanitizer()
    {
        if (!HasSanitizers) return;
        var options = new CompileOptions { Defines = new[] { "AMIGA", "ESHARP_HOSTC" } };
        var r = EsCompiler.Compile(new[] { new SourceFile(SamplePath, File.ReadAllText(SamplePath)) }, options);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var (code, output) = RunSanitized(r.C!);
        Assert.Equal(ClosuresExpected.Output, output);
        Assert.Equal(0, code);
    }

    private static (int code, string output) Run(string members, string body, bool sanitize = true)
    {
        var c = CompileOk(Program(members + Check, body));
        return sanitize && HasSanitizers ? RunSanitized(c) : RunWithHostGcc(c);
    }

    [Fact]
    public void Returned_and_stored_closures_outlive_their_method()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int, int> Held;
static System.Func<int, int> Make(int k) { return x => x + k; }
static System.Func<int> Counter(int n) { int c = n; return () => ++c; }
static void Store() { int k = 3; Held = x => x * k; k = 4; }
";
        const string body = @"
int base0 = Heap.LiveClosures();
System.Func<int, int> f = Make(3);
Check(f(1) == 4, ""a returned closure keeps its parameter"");
System.Func<int> a = Counter(10), b = Counter(20);
a(); a();
Check(a() == 13 && b() == 21, ""two closures from the same method keep separate variables"");
Store();
Check(Held(5) == 20, ""a closure stored in a static sees the last value of its variable"");
Check(Heap.LiveClosures() == base0 + 4, ""each stored closure is counted"");
Heap.Free(ref f); Heap.Free(ref a); Heap.Free(ref b); Heap.Free(ref Held);
Check(f == null && Held == null, ""Heap.Free nulls the delegate"");
Check(Heap.LiveClosures() == base0, ""freeing the closures frees their frames"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a returned closure keeps its parameter", "two closures from the same method keep separate variables",
            "a closure stored in a static sees the last value of its variable", "each stored closure is counted",
            "Heap.Free nulls the delegate", "freeing the closures frees their frames"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Frames_are_dropped_on_return_break_and_continue()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int>[] Fs = new System.Func<int>[8];
static int Fill(int stopAt)
{
    int made = 0;
    for (int i = 0; i < 8; i++)
    {
        int j = i;
        if (j == 1) continue;
        if (j == stopAt) return made;
        Fs[made++] = () => j * j;
        if (j == 5) break;
    }
    return made;
}
";
        const string body = @"
int base0 = Heap.LiveClosures();
int n = Fill(99);
Check(n == 5 && Fs[0]() == 0 && Fs[1]() == 4 && Fs[4]() == 25, ""closures made before break and after continue"");
Check(Heap.LiveClosures() == base0 + 5, ""one closure per kept iteration"");
for (int i = 0; i < n; i++) Heap.Free(ref Fs[i]);
n = Fill(3);
Check(n == 2 && Fs[1]() == 4, ""a return from inside the loop"");
for (int i = 0; i < n; i++) Heap.Free(ref Fs[i]);
Check(Heap.LiveClosures() == base0, ""nothing left over"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("closures made before break and after continue", "one closure per kept iteration",
            "a return from inside the loop", "nothing left over"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Nested_stored_lambdas_reach_the_outer_frames()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int, System.Func<int>> Make(int b) { return k => () => k + b; }
static System.Func<int, System.Func<int>> Plain() => k => () => k * 2;
";
        const string body = @"
int base0 = Heap.LiveClosures();
System.Func<int, System.Func<int>> mk = Make(100);
System.Func<int> one = mk(1), two = mk(2);
Check(one() == 101 && two() == 102, ""an inner closure over the outer lambda's parameter and the method's"");
Heap.Free(ref mk);
Check(one() == 101, ""the inner closure keeps the outer frames after the outer closure is freed"");
Heap.Free(ref one); Heap.Free(ref two);
System.Func<int, System.Func<int>> p = Plain();
System.Func<int> six = p(3);
Check(six() == 6, ""a lambda without captures that returns a closure"");
Heap.Free(ref six); Heap.Free(ref p);
Check(Heap.LiveClosures() == base0, ""all frames freed"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("an inner closure over the outer lambda's parameter and the method's",
            "the inner closure keeps the outer frames after the outer closure is freed", "a lambda without captures that returns a closure", "all frames freed"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Events_add_remove_raise_and_free()
    {
        if (!HasGcc) return;
        const string members = @"
public class Src
{
    public event System.Action<int, int> Pair;
    public event System.Action Ping;
    public int Raised;
    public void Fire() { Raised++; Pair?.Invoke(P.Arg(), P.Arg()); }
    public void Pong() { if (Ping != null) Ping(); }
    public void Drop() { Heap.Free(ref Pair); Heap.Free(ref Ping); }
}
public class Derived : Src
{
    public void Hook(System.Action a) { Ping += Twice; }
    static void Twice() { }
}
static int Next;
public static int Arg() { return ++Next; }
";
        const string body = @"
int base0 = Heap.LiveClosures();
Src s = new Src();
int sum = 0, order = 0;
s.Pair += (a, b) => { sum += a * 10 + b; order = order * 10 + 1; };
s.Pair += (a, b) => { order = order * 10 + 2; };
s.Fire();
Check(sum == 12 && order == 12 && Next == 2, ""handlers run in order, the arguments are evaluated once"");
int pings = 0;
System.Action h = () => pings++;
s.Ping += h; s.Ping += h;
s.Pong();
Check(pings == 2, ""the same handler added twice runs twice"");
s.Ping -= h;
s.Pong();
Check(pings == 3, ""-= removes one of them"");
s.Ping -= h;
s.Pong();
Check(pings == 3, ""and then the other"");
Heap.Free(ref h);
Derived d = new Derived();
int hits = 0;
d.Ping += () => hits++;
d.Hook(null);
d.Pong();
Check(hits == 1, ""an event of the base class used through a derived class"");
d.Drop(); Heap.Free(ref d);
s.Drop(); Heap.Free(ref s);
Check(Heap.LiveClosures() == base0, ""the events released their handlers"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("handlers run in order, the arguments are evaluated once", "the same handler added twice runs twice",
            "-= removes one of them", "and then the other", "an event of the base class used through a derived class", "the events released their handlers"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Constructors_accessors_and_this()
    {
        if (!HasGcc) return;
        const string members = @"
public class Base { public int Seen; }
public class Btn : Base
{
    public System.Action OnClick;
    public System.Func<int> Report;
    int _scale = 2;
    public Btn(int id) { OnClick = () => Seen += id * _scale; }
    public int Factor { set { Report = () => value * Seen; } }
    public System.Func<int> Auto { get; set; }
    public void Free() { Heap.Free(ref OnClick); Heap.Free(ref Report); }
}
";
        const string body = @"
Btn b = new Btn(7);
b.OnClick();
b.OnClick();
Check(b.Seen == 28, ""a constructor's parameter and this, captured by a stored lambda"");
b.Factor = 3;
Check(b.Report() == 84, ""a setter's value captured by a stored lambda"");
int z = 9;
b.Auto = () => z + 1;
z = 10;
Check(b.Auto() == 11, ""a closure stored in an auto-property"");
System.Func<int> alias = b.Auto;
Heap.Free(ref alias);
b.Free();
Heap.Free(ref b);
Check(Heap.LiveClosures() == 0, ""freed"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a constructor's parameter and this, captured by a stored lambda", "a setter's value captured by a stored lambda", "a closure stored in an auto-property", "freed"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Conditional_invoke_on_a_delegate()
    {
        if (!HasGcc) return;
        const string body = @"
int n = 0;
System.Action<int> f = null;
f?.Invoke(5);
Check(n == 0, ""?.Invoke on null does nothing"");
System.Action<int> g = v => n += v;
g?.Invoke(5);
Check(n == 5, ""?.Invoke calls a delegate"");
";
        var (code, output) = Run("", body);
        Assert.Equal(OkLines("?.Invoke on null does nothing", "?.Invoke calls a delegate"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void A_leaked_closure_shows_in_LiveClosures_and_under_the_leak_sanitizer()
    {
        if (!HasGcc) return;
        const string members = "static System.Action Kept;";
        const string body = @"
int base0 = Heap.LiveClosures();
int n = 0;
Kept = () => n++;
Check(Heap.LiveClosures() > base0, ""a stored closure that is not freed is counted"");
Kept = null;   // dropped without Heap.Free: the frame is unreachable, which the leak sanitizer reports at exit
";
        var c = CompileOk(Program(members + Check, body));
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal(OkLines("a stored closure that is not freed is counted"), output);
        if (!HasSanitizers) return;
        (code, output) = RunSanitized(c);
        Assert.NotEqual(0, code);
        Assert.Contains("LeakSanitizer", output);
    }

    [Theory]
    [InlineData("static System.Func<int>[] Fs = new System.Func<int>[3];", "int[] xs = new int[3]; foreach (int x in xs) Fs[0] = () => x;")]            // a foreach variable
    [InlineData("static System.Action Kept; static void Keep(System.Action a) { Kept = () => a(); }", "")]                                       // captures a borrowed parameter
    [InlineData("static System.Func<int> Kept; static void Run(System.Action a) { a(); }", "int k = 1; Run(() => { Kept = () => k; });")]     // through a borrowed lambda
    [InlineData("public class E { public event System.Action Ev; public void Clear() { Ev = null; } }", "")]                                  // assigning an event
    [InlineData("public class E { public event System.Action Ev { add { } remove { } } }", "")]                                               // event accessors
    [InlineData("public class E { public event System.Func<int> Ev; public int Raise() => Ev(); }", "")]                                       // an event that returns a value
    [InlineData("public struct S { public event System.Action Ev; }", "")]                                                                     // an event in a struct
    [InlineData("static System.Action Kept;", "int k = 0; System.Action a = () => k++; System.Action b = null; b = () => k--; Kept = a;")]    // a stack closure assigned to an earlier local
    public void Unsupported_closure_forms_report_ES0099(string members, string body)
    {
        var r = Compile(Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Theory]
    [InlineData("static System.Action Kept; static void Keep(System.Action a) { Kept = a; }", "stored")]
    [InlineData("static System.Action Keep(System.Action a) { return a; }", "returned")]
    [InlineData("static System.Action Keep(System.Action a) => a;", "returned")]
    [InlineData("public class B { public event System.Action Ev; public void Add(System.Action a) { Ev += a; } }", "added to an event")]
    [InlineData("static void Keep(System.Action a) { Heap.Free(ref a); }", "freed")]
    [InlineData("static void Take([Owned] System.Action a) { } static void Keep(System.Action a) { Take(a); }", "[Owned]")]
    public void Keeping_a_borrowed_delegate_parameter_is_ES0012(string members, string words)
    {
        var r = Compile(Program(members));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0012");
        Assert.Contains(words, d.GetMessage());
    }

    [Theory]
    [InlineData("static System.Action Kept; static void Keep([Owned] System.Action a) { Kept = a; }")]
    [InlineData("static void Call(System.Action a) { a(); Pass(a); } static void Pass(System.Action a) { System.Action b = a; b(); }")]
    [InlineData("public class B { System.Action _h; public System.Action H { get => _h; set => _h = value; } }")]
    public void Owned_and_borrowed_uses_are_not_ES0012(string members)
    {
        var r = Compile(Program(members));
        Assert.DoesNotContain(r.Diagnostics, x => x.Id == "ES0012");
    }

    [Fact]
    public void Stored_closures_that_are_never_freed_are_ES0013()
    {
        var r = Compile(Program(@"
public class B { public event System.Action Ev; public void Fire() { Ev?.Invoke(); } }
static System.Func<int> Kept;
static void Wire(B b) { int n = 0; System.Action h = () => n++; b.Ev += h; Kept = () => n; }", ""));
        var warnings = r.Diagnostics.Where(x => x.Id == "ES0013").Select(x => x.GetMessage()).ToList();
        Assert.Contains(warnings, m => m.Contains("'h'"));
        Assert.Contains(warnings, m => m.Contains("'Kept'"));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));   // a warning, not an error

        r = Compile(Program(@"
public class B { public event System.Action Ev; public void Drop() { Heap.Free(ref Ev); } }
static System.Func<int> Kept;
static void Wire(B b) { int n = 0; System.Action h = () => n++; b.Ev += h; Heap.Free(ref h); b.Ev += () => n--; Kept = () => n; Heap.Free(ref Kept); }", ""));
        Assert.DoesNotContain(r.Diagnostics, x => x.Id == "ES0013");
    }
}

/// <summary>What samples/closures prints.</summary>
internal static class ClosuresExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "an event handler that captures a local",
        "the handler and the method share the captured local",
        "two handlers run",
        "-= removes a handler kept in a variable",
        "Heap.Free sets the delegate to null",
        "an event with an argument calls every handler",
        "raising an event with no handlers does nothing",
        "returned closures keep their own variables",
        "an expression-bodied method returns a closure over its parameter",
        "a closure stored in a field sees later changes",
        "a stored closure writes the method's variable",
        "an [Owned] parameter keeps the closure",
        "a borrowed lambda shares a variable with a stored one",
        "each loop iteration gets its own captured variable",
        "a stored lambda that uses this",
        "a raise uses a snapshot: removal and addition wait for the next raise",
        "the removed handler is gone and the added one runs",
        "a static event",
        "Heap.Free on an event drops every handler",
        "every stored closure was freed",
    };
}
