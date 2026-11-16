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

    // ---- review probes (PR #83): each runs under AddressSanitizer/LeakSanitizer when available, or must not compile.

    private static (int code, string output) RunProgram(string source)
    {
        var c = CompileOk(source);
        return HasSanitizers ? RunSanitized(c) : RunWithHostGcc(c);
    }

    private const string Probe_coalesce = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; System.Action a = () => x++; Held = a ?? Other; }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Held!(); Heap.Free(ref Held); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_coalasg = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; System.Action a = () => x++; Held ??= a; }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Held!(); Heap.Free(ref Held); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_switchx = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static bool Flag = true;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; System.Action a = () => x++; Held = Flag switch { true => a, _ => Other }; }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Held!(); Heap.Free(ref Held); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_p2 = @"using ESharp;
using ESharp.Platform;
public class Btn
{
    public event System.Action? Clicked;
    public void Click() { Clicked?.Invoke(); }
    public void Dispose() { Heap.Free(ref Clicked); }
}
public class Derived : Btn { }
public class Counter
{
    public int N;
    public System.Action? Inc;
    public Counter(int start) { N = start; Inc = () => N++; }
    public System.Action Bump => () => N += 10;
    public void Dispose() { Heap.Free(ref Inc); }
}
public static class P
{
    static int Hits;
    static System.Action MakeHit(int k) { return () => Hits += k; }
    static System.Action? Stat;
    static P() { int s = 7; Stat = () => Hits += s; }
    static void Check(bool ok, string n) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(n); Dos.PutStr(""\n""); }
    static System.Action? G;
    static void Gotos(int n) { for (int i = 0; i < n; i++) { int v = i; G = () => Hits += v; if (i < n - 1) { Heap.Free(ref G); continue; } } }
    static void Sw(int k)
    {
        switch (k)
        {
            case 1:
            {
                int v = 100;
                G = () => Hits += v;
                break;
            }
            default:
                return;
        }
    }
    [EntryPoint]
    public static void Main()
    {
        int b0 = Heap.LiveClosures();
        var b = new Derived();
        b.Clicked += MakeHit(1);      // a call result: the event retains again
        b.Click();
        Check(Hits == 1, ""call result handler ran"");
        b.Dispose();
        Heap.Free(ref b);
        Dos.PutStr(""live after dispose: ""); Dos.PutStr(Heap.LiveClosures() == b0 ? ""0\n"" : ""LEAK\n"");
        var c = new Counter(5);
        c.Inc!(); 
        Check(c.N == 6, ""ctor closure captures this"");
        var bump = c.Bump; bump(); Heap.Free(ref bump);
        Check(c.N == 16, ""property getter closure"");
        c.Dispose(); Heap.Free(ref c);
        Stat!(); Check(Hits == 8, ""static ctor closure""); Heap.Free(ref Stat);
        Hits = 0; Gotos(3); G!(); Check(Hits == 2, ""loop continue frame""); Heap.Free(ref G);
        Hits = 0; Sw(1); G!(); Check(Hits == 100, ""switch section block""); Heap.Free(ref G);
        // self removal and removing another during a raise
        var d = new Btn();
        System.Action? h1 = null;
        System.Action? h2 = null;
        int calls = 0;
        h1 = () => { calls++; d.Clicked -= h1; d.Clicked -= h2; };
        h2 = () => { calls += 10; };
        d.Clicked += h1; d.Clicked += h2;
        d.Click();
        Check(calls == 11, ""removed during raise still runs (snapshot)"");
        d.Click();
        Check(calls == 11, ""removed handlers gone"");
        Heap.Free(ref h1); Heap.Free(ref h2);
        d.Dispose(); Heap.Free(ref d);
        Dos.PutStr(""live at end: ""); Dos.PutStr(Heap.LiveClosures() == 0 ? ""0\n"" : ""LEAK\n"");   // Stat (made before b0 was read) is freed by now
    }
}
";

    private const string Probe_p3 = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static int Hits;
    static System.Action? G;
    static void Check(bool ok, string n) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(n); Dos.PutStr(""\n""); }
    static System.Func<System.Func<int>> Nested(int a)
    {
        int b = a * 2;
        return () => { int c = b + 1; return () => a + b + c; };
    }
    static void Loop(int n)
    {
        int i = 0;
        while (i < n)
        {
            int v = i;
            if (v == 1) { i++; continue; }
            if (v == 3) break;
            System.Action t = () => Hits += v * 100;
            if (G == null) G = t; else Heap.Free(ref t);
            i++;
        }
    }
    static System.Func<int> Ret(int p) { System.Func<int> f = () => p; if (p > 0) return f; Heap.Free(ref f); return () => -1; }
    [EntryPoint]
    public static void Main()
    {
        var mk = Nested(1);
        var inner = mk();
        Check(inner() == 1 + 2 + 3, ""nested stored lambdas"");
        Heap.Free(ref inner); Heap.Free(ref mk);
        Loop(5); G!(); Check(Hits == 0, ""loop frames""); Heap.Free(ref G);
        var r1 = Ret(4); var r2 = Ret(0); Check(r1() == 4 && r2() == -1, ""early return""); Heap.Free(ref r1); Heap.Free(ref r2);
        Check(Heap.LiveClosures() == 0, ""no leaks"");
    }
}
";

    private const string Probe_p7 = @"using ESharp;
using ESharp.Platform;
public class Btn { public event System.Action? Clicked; public void Click() { Clicked?.Invoke(); } public void Dispose() { Heap.Free(ref Clicked); } }
public static class P
{
    static System.Action? Field;
    static int Hits;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make()
    {
        System.Action a = () => { };
        System.Action b = () => a();
        Field = b;
    }
    [EntryPoint]
    public static void Main()
    {
        int b0 = Heap.LiveClosures();
        var btn = new Btn();
        int k = 2;
        btn.Clicked += k > 1 ? () => Hits += k : () => Hits -= k;
        btn.Click();
        btn.Dispose();
        Dos.PutStr(Heap.LiveClosures() == b0 ? ""no leak\n"" : ""LEAK conditional +=\n"");
        Heap.Free(ref btn);
        Make(); Smash(); Dos.PutStr(""done\n"");
    }
}
";

    [Theory]
    [InlineData("coalesce")]
    [InlineData("coalasg")]
    [InlineData("switchx")]
    public void Delegate_locals_kept_through_coalesce_and_switch_are_heap_closures(string probe)
    {
        if (!HasGcc) return;
        var source = probe switch { "coalesce" => Probe_coalesce, "coalasg" => Probe_coalasg, _ => Probe_switchx };
        var (code, output) = RunProgram(source);
        Assert.Equal("done\n", output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Review_probe_handlers_from_calls_constructors_getters_loops_and_switch_sections()
    {
        if (!HasGcc) return;
        var (code, output) = RunProgram(Probe_p2);
        Assert.Equal("ok call result handler ran\nlive after dispose: 0\nok ctor closure captures this\nok property getter closure\nok static ctor closure\n" +
            "ok loop continue frame\nok switch section block\nok removed during raise still runs (snapshot)\nok removed handlers gone\nlive at end: 0\n", output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Review_probe_nested_lambdas_loop_frames_and_early_returns()
    {
        if (!HasGcc) return;
        var (code, output) = RunProgram(Probe_p3);
        Assert.Equal("ok nested stored lambdas\nok loop frames\nok early return\nok no leaks\n", output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Adding_a_conditional_of_new_lambdas_hands_its_reference_to_the_event()
    {
        if (!HasGcc) return;
        var (code, output) = RunProgram(Probe_p7);
        Assert.Equal("no leak\ndone\n", output);
        Assert.Equal(0, code);
    }

    private const string Probe_p4 = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static void Keep(System.Action a) { System.Action t = a; Held = t; }
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; Keep(() => x++); }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Held!(); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_p6 = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action Held = () => { };
    static void Put<T>(ref T slot, T x) { slot = x; }
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; Put(ref Held, () => x++); }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Held(); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_p5 = @"using ESharp;
using ESharp.Platform;
public class Box<T> { public T V = default!; public void Set(T x) { V = x; } }
public static class P
{
    static Box<System.Action> B = new Box<System.Action>();
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; B.Set(() => x++); }
    [EntryPoint]
    public static void Main() { Make(); Smash(); B.V(); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_vec = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static Vec<System.Action> list = new Vec<System.Action>(4, false);
    static void Fill() { int x = 41; list.Add(() => x++); }
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; Dos.PutStr(a+b+c+d > 0 ? """" : ""z""); }
    [EntryPoint]
    public static void Main() { Fill(); Smash(); list[0](); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_tuple = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static (System.Action, int) Pair;
    static bool Flag = true;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; Pair = (() => x++, 1); }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Pair.Item1(); Dos.PutStr(""done\n""); }
}
";

    private const string Probe_tuple2 = @"using ESharp;
using ESharp.Platform;
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static (System.Action, int) Pair;
    static bool Flag = true;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; System.Action a = () => x++; Pair = (a, 1); }
    [EntryPoint]
    public static void Main() { Make(); Smash(); Pair.Item1(); Dos.PutStr(""done\n""); }
}
";

    [Theory]
    [InlineData("p4", "ES0012")]     // a borrowed parameter copied into a local, then stored
    [InlineData("p6", "ES0014")]     // a capturing lambda passed as a generic T that the callee stores
    [InlineData("p5", null)]         // a generic class holding a delegate (ES0008 and more)
    [InlineData("vec", "ES0014")]    // Vec<System.Action>
    [InlineData("tuple", "ES0099")]  // a tuple holding a lambda
    [InlineData("tuple2", "ES0099")] // a tuple holding a delegate local
    public void Review_probes_that_must_not_compile(string probe, string? id)
    {
        var source = probe switch { "p4" => Probe_p4, "p6" => Probe_p6, "p5" => Probe_p5, "vec" => Probe_vec, "tuple" => Probe_tuple, _ => Probe_tuple2 };
        var r = Compile(source);
        Assert.False(r.Success, "should not compile");
        if (id != null) Assert.Contains(id, ErrorIds(r));
    }

    [Theory]
    [InlineData("static System.Action? Held; static void Keep(System.Action a) { System.Action t = a; System.Action u = t; Held = u; }", "through the local")]
    [InlineData("static System.Action? Held; static void Keep(System.Action a) { System.Action t = () => a(); Held = t; }", "captured by a stored lambda")]
    [InlineData("static System.Action? Held; static void Keep(System.Action a) { Held ??= a; }", "stored")]
    [InlineData("static System.Action? Held; static void Keep(System.Action a, bool f) { Held = f ? a : null; }", "stored")]
    public void ES0012_follows_locals_coalesce_and_generic_arguments(string members, string words)
    {
        var r = Compile(Program(members));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0012" && d.GetMessage().Contains(words));
    }

    [Fact]
    public void Adding_a_call_result_to_an_event_does_not_leak()
    {
        if (!HasGcc) return;
        const string members = @"
public class B { public event System.Action Ev; public void Fire() { Ev?.Invoke(); } public void Drop() { Heap.Free(ref Ev); } }
static int Hits;
static System.Action Make(int k) { return () => Hits += k; }
";
        const string body = @"
B b = new B();
int k = 3;
b.Ev += Make(1);
b.Ev += k > 1 ? Make(2) : Make(3);
b.Ev += k switch { 3 => () => Hits += k * 100, _ => null };
b.Fire();
Check(Hits == 303, ""handlers from a call, a conditional of calls and a switch expression"");
b.Drop(); Heap.Free(ref b);
Check(Heap.LiveClosures() == 0, ""the event held the only references"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("handlers from a call, a conditional of calls and a switch expression", "the event held the only references"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void A_delegate_local_in_an_unrecognised_position_is_made_a_heap_closure_not_left_on_the_stack()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Action? Held;
static bool Seen;
static void Make() { int x = 41; System.Action a = () => x++; Seen = a is not null; Held = a; }
";
        const string body = @"
Make();
Held!();
Check(Heap.LiveClosures() == 1, ""the local was made a heap closure"");
Heap.Free(ref Held);
";
        var r = Compile(Program(members + Check, body));
        if (!r.Success) { Assert.Contains("ES0099", ErrorIds(r)); return; }   // or rejected outright: never a stack closure
        var (code, output) = HasSanitizers ? RunSanitized(r.C!) : RunWithHostGcc(r.C!);
        Assert.Equal(OkLines("the local was made a heap closure"), output);
        Assert.Equal(0, code);
    }

    // ---- re-review probes (PR #83, second round)

    private const string Probe2_getter = @"using ESharp;
using ESharp.Platform;
public class Btn { public event System.Action? Clicked; public void Click() { Clicked?.Invoke(); } public void Dispose() { Heap.Free(ref Clicked); } }
public class Holder
{
    System.Action? _h;
    public void Make() { int x = 41; _h = () => x++; }
    public System.Action GetH() { return _h!; }
    public void Dispose() { Heap.Free(ref _h); }
}
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        var h = new Holder(); h.Make();
        var b = new Btn();
        b.Clicked += h.GetH();
        b.Click();
        b.Dispose();      // event releases its reference
        h.Dispose(); Heap.Free(ref h); Heap.Free(ref b);
        Dos.PutStr(Heap.LiveClosures() == 0 ? ""clean\n"" : ""LIVE MISMATCH\n"");
    }
}
";
    private const string Probe2_lamret = @"using ESharp;
using ESharp.Platform;
public class Btn { public event System.Action? Clicked; public void Click() { Clicked?.Invoke(); } public void Dispose() { Heap.Free(ref Clicked); } }
public class Holder { public System.Action? Run; public System.Action Q { get; set; } public void Dispose() { Heap.Free(ref Run); } }
public delegate System.Action Wrap(System.Action a);
public delegate System.Action Mk();
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static bool Flag = true;
    static int Hits;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Keep(System.Action p) { System.Func<System.Action> f = () => p; Held = f(); } static void Make() { int y = 41; Keep(() => Hits = ++y); }
    [EntryPoint]
    public static void Main() { Other = null; Flag = Flag; Hits = 0; Make(); Smash(); Held!(); Dos.PutStr(Hits == 42 ? ""ran\n"" : ""BAD VALUE\n""); Heap.Free(ref Held); Dos.PutStr(Heap.LiveClosures() == 0 ? ""clean\n"" : ""LIVE LEAK\n""); }
}
";
    private const string Probe2_mkd = @"using ESharp;
using ESharp.Platform;
public class Btn { public event System.Action? Clicked; public void Click() { Clicked?.Invoke(); } public void Dispose() { Heap.Free(ref Clicked); } }
public class Holder { public System.Action? Run; public System.Action Q { get; set; } public void Dispose() { Heap.Free(ref Run); } }
public delegate System.Action Wrap(System.Action a);
public delegate System.Action Mk();
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static bool Flag = true;
    static int Hits;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Keep(System.Action p) { Mk f = () => p; Held = f(); } static void Make() { int y = 41; Keep(() => Hits = ++y); }
    [EntryPoint]
    public static void Main() { Other = null; Flag = Flag; Hits = 0; Make(); Smash(); Held!(); Dos.PutStr(Hits == 42 ? ""ran\n"" : ""BAD VALUE\n""); Heap.Free(ref Held); Dos.PutStr(Heap.LiveClosures() == 0 ? ""clean\n"" : ""LIVE LEAK\n""); }
}
";
    private const string Probe2_wrapq = @"using ESharp;
using ESharp.Platform;
public class Btn { public event System.Action? Clicked; public void Click() { Clicked?.Invoke(); } public void Dispose() { Heap.Free(ref Clicked); } }
public class Holder { public System.Action? Run; public System.Action Q { get; set; } public void Dispose() { Heap.Free(ref Run); } }
public delegate System.Action Wrap(System.Action a);
public delegate System.Action Mk();
public static class P
{
    static System.Action? Held;
    static System.Action? Other;
    static bool Flag = true;
    static int Hits;
    static void Smash() { int a = 1, b = 2, c = 3, d = 4; int[] z = new int[8]; for (int i = 0; i < 8; i++) z[i] = 0x5A5A5A5A; Dos.PutStr(a+b+c+d+z[3] > 0 ? """" : ""z""); }
    static void Make() { int x = 41; Wrap w = q => q; System.Action a = () => Hits = ++x; Held = w(a); }
    [EntryPoint]
    public static void Main() { Other = null; Flag = Flag; Hits = 0; Make(); Smash(); Held!(); Dos.PutStr(Hits == 42 ? ""ran\n"" : ""BAD VALUE\n""); Heap.Free(ref Held); Dos.PutStr(Heap.LiveClosures() == 0 ? ""clean\n"" : ""LIVE LEAK\n""); }
}
";
    private const string Probe2_pinit = @"using ESharp;
using ESharp.Platform;
public class Holder { public System.Action Q { get; set; } = () => { }; public System.Action F = () => { }; }
public static class P
{
    static System.Action S { get; set; } = () => Dos.PutStr(""s\n"");
    [EntryPoint]
    public static void Main() { var h = new Holder(); h.Q(); h.F(); S(); Heap.Free(ref h); Dos.PutStr(""ran\n""); }
}
";

    [Fact]
    public void Adding_a_call_that_returns_a_kept_delegate_takes_a_reference_instead_of_stealing_one()
    {
        if (!HasGcc) return;
        var r = Compile(Probe2_getter);
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0013" && d.GetMessage().Contains("GetH"));   // the possible leak is flagged
        var (code, output) = HasSanitizers ? RunSanitized(r.C!) : RunWithHostGcc(r.C!);
        Assert.Equal("clean\n", output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Adding_a_call_that_always_returns_a_new_lambda_hands_the_reference_over()
    {
        if (!HasGcc) return;
        const string members = @"
public class B { public event System.Action Ev; public void Fire() { Ev?.Invoke(); } public void Drop() { Heap.Free(ref Ev); } }
static int Hits;
static System.Action Make(int k) => k > 0 ? () => Hits += k : Other(k);
static System.Action Other(int k) { if (k == 0) return null; return () => Hits -= k; }
";
        const string body = @"
B b = new B();
b.Ev += Make(2);
b.Ev += Make(-1);
b.Fire();
Check(Hits == 3, ""both handlers ran"");
b.Drop(); Heap.Free(ref b);
Check(Heap.LiveClosures() == 0, ""the event held the only references"");
";
        var r = Compile(Program(members + Check, body));
        Assert.DoesNotContain(r.Diagnostics, d => d.Id == "ES0013");
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("both handlers ran", "the event held the only references"), output);
        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("lamret")]   // Func<Action> f = () => p; Held = f();
    [InlineData("mkd")]      // the same with a user delegate type
    [InlineData("wrapq")]    // Wrap w = q => q; Held = w(a);
    public void A_borrowed_delegate_returned_from_an_expression_bodied_lambda_is_ES0012(string probe)
    {
        var source = probe switch { "lamret" => Probe2_lamret, "mkd" => Probe2_mkd, _ => Probe2_wrapq };
        var r = Compile(source);
        Assert.False(r.Success);
        Assert.Contains("ES0012", ErrorIds(r));
    }

    [Fact]
    public void Lambdas_in_instance_field_and_property_initializers_compile_and_run()
    {
        if (!HasGcc) return;
        var (code, output) = RunProgram(Probe2_pinit);
        Assert.Equal("s\nran\n", output);
        Assert.Equal(0, code);
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
