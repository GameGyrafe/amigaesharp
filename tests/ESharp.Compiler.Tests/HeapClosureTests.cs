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
    public void Compound_assignment_on_a_delegate_is_multicast_as_in_dotnet()
    {
        if (!HasGcc) return;
        const string members = @"
static int log;
static void Note(int d) { log = log * 10 + d; }
static void A() { Note(1); }
static void B() { Note(2); }
static void C() { Note(3); }
static int One() { Note(1); return 1; }
static int Two() { Note(2); return 2; }
static System.Action Field;
static System.Action Auto { get; set; }
static System.Func<int, int> Mul;
class Holder { public System.Action H; public System.Action P { get; set; } }
static void AddTo(ref System.Action r) { }
";
        const string body = @"
int base0 = Heap.LiveClosures();
System.Action a = A;
a += B;
a += C;
log = 0; a();
Check(log == 123, ""+= calls in order"");
a -= B;
log = 0; a();
Check(log == 13, ""-= removes one"");
a += A; a += B; a += A;
log = 0; a();
Check(log == 13121, ""the same method twice"");
a -= A;
log = 0; a();
Check(log == 1312, ""-= removes the last match"");
a -= C; a -= B; a -= A; a -= B;
log = 0; a();
Check(log == 1, ""one entry left"");
a -= A;
Check(a == null, ""removing the last one leaves null"");
a -= A;
Check(a == null, ""removing from null is a no-op"");
a += null;
Check(a == null, ""adding null is a no-op"");
a += A; a += null; a -= null;
log = 0; a();
Check(log == 1, ""null operands change nothing"");
a -= A;

int n = 0;
System.Action c = () => n += 1;
c += () => n += 10;
c += () => n += 100;
c();
Check(n == 111, ""capturing lambdas run in order"");
System.Action keep = () => n += 1000;
c += keep;
c();
Check(n == 1222, ""a kept variable is added"");
c -= keep;
c();
Check(n == 1333, ""-= of the variable removes it"");
Heap.Free(ref keep);
Check(Heap.LiveClosures() > base0, ""entries still hold closures"");
Heap.Free(ref c);
Check(c == null, ""Heap.Free nulls a multicast delegate"");
Check(Heap.LiveClosures() == base0, ""nothing leaks"");

System.Func<int> f = One;
f += Two;
log = 0;
Check(f() == 2 && log == 12, ""a Func gives the last result"");
f -= Two;
log = 0;
Check(f() == 1 && log == 1, ""after -= the first one is a plain delegate again"");
f -= One;
Check(f == null, ""Func back to null"");

Field += A; Field += B;
log = 0; Field();
Check(log == 12, ""a static field"");
Field -= A;
log = 0; Field();
Check(log == 2, ""-= on a static field"");
Field -= B;
Check(Field == null, ""static field null again"");
Auto += A; Auto += C;
log = 0; Auto();
Check(log == 13, ""a static auto-property"");
Auto -= A; Auto -= C;
Check(Auto == null, ""auto-property null again"");

Holder h = new Holder();
int hits = 0;
h.H += () => hits += 1;
h.H += () => hits += 2;
h.H += A;
log = 0; h.H();
Check(hits == 3 && log == 1, ""an instance field"");
h.P += B; h.P += C;
log = 0; h.P();
Check(log == 23, ""an instance auto-property"");
Heap.Free(ref h.H);
Check(h.H == null, ""freed field"");
h.P -= B; h.P -= C;
Check(h.P == null, ""auto-property back to null"");
Heap.Free(ref h);

Mul += x => x * 2;
Mul += x => x + 1;
Check(Mul(5) == 6, ""arguments reach every entry and the last result wins"");
Heap.Free(ref Mul);

System.Action m = A;
m += B;
m += C;
System.Action both = A;
both += B;
both += both;
log = 0; both();
Check(log == 1212, ""a multicast delegate added to itself"");
log = 0; m();
Check(log == 123, ""another multicast delegate is not changed"");
System.Action sub = A;
sub += B;
both -= sub;
log = 0; both();
Check(log == 12, ""-= of a multicast delegate removes the run"");
Heap.Free(ref both); Heap.Free(ref m); Heap.Free(ref sub);
Check(Heap.LiveClosures() == base0, ""still nothing leaks"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("+= calls in order", "-= removes one", "the same method twice", "-= removes the last match", "one entry left",
            "removing the last one leaves null", "removing from null is a no-op", "adding null is a no-op", "null operands change nothing",
            "capturing lambdas run in order", "a kept variable is added", "-= of the variable removes it", "entries still hold closures",
            "Heap.Free nulls a multicast delegate", "nothing leaks", "a Func gives the last result",
            "after -= the first one is a plain delegate again", "Func back to null", "a static field", "-= on a static field",
            "static field null again", "a static auto-property", "auto-property null again", "an instance field", "an instance auto-property",
            "freed field", "auto-property back to null", "arguments reach every entry and the last result wins",
            "a multicast delegate added to itself", "another multicast delegate is not changed",
            "-= of a multicast delegate removes the run", "still nothing leaks"), output);
        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("static void F(ref System.Action r) { r += () => { }; }", "")]
    [InlineData("class H { System.Action _a; public System.Action P { get { return _a; } set { _a = value; } } }", "H h = new H(); h.P += () => { };")]
    [InlineData("delegate void D(ref int x); static void F(D d) { d += Inc; } static void Inc(ref int x) { x++; }", "")]
    public void Compound_assignment_on_a_delegate_that_cannot_be_done_is_ES0099(string members, string body)
    {
        var r = Compile(Program(members, body));
        Assert.Contains("ES0099", ErrorIds(r));
    }

    [Theory]
    [InlineData("static System.Action S; static void B() { }", "S = () => { }; System.Action d = S; d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action F = () => { }; static void B() { }", "System.Action d = null; d = F; d += B;", "given a copy of another delegate")]
    [InlineData("static void B() { } static void Q1(System.Action p) { p += B; }", "", "borrowed delegate parameter")]
    [InlineData("static void B() { } static void Q2(System.Action s) { System.Action d = s; d -= B; }", "", "given a copy of another delegate")]
    [InlineData("static void B() { } static System.Action H; static void Q3([Owned] System.Action s) { H = s; H += B; }", "", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { } static void GetOut(out System.Action o) { o = S; }", "System.Action d; GetOut(out d); d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { } static void GetOut(out System.Action o) { o = S; }", "GetOut(out System.Action d); d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { } static void Set(ref System.Action r) { r = S; }", "System.Action d = null; Set(ref d); d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { }", "System.Action d = null; d ??= S; d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { }", "if (S is System.Action d) { d += B; }", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { }", "switch (S) { case System.Action d: d += B; break; }", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { }", "System.Action d = null; ref System.Action r = ref d; r = S; d += B;", "given a copy of another delegate")]
    [InlineData("static System.Action S = () => { }; static void B() { }", "System.Action d = null; int i; (d, i) = (S, 1); d += B;", "given a copy of another delegate")]
    public void Compound_assignment_on_a_delegate_that_shares_or_borrows_a_reference_is_ES0099(string members, string body, string message)
    {
        var r = Compile(Program(members, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(message));
    }

    private const string CopyMembers = "static int log; static void A() { log = log * 10 + 1; } static void B() { log = log * 10 + 2; } static System.Action Fld; static System.Action Fld2 = A; static void Take(out System.Action o) { o = null; }";

    private const string Copied = "in a way that may copy it";

    [Theory]
    [InlineData("System.Action d = A; System.Action e = d; d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = null; e = d; d -= B; e();")]
    [InlineData("System.Action d = null; d ??= A; System.Action e = null; e ??= d; d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = (System.Action)d; System.Action f = e; d += B; f();")]
    [InlineData("System.Action d = A; System.Action e = new System.Action(d); d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = d as System.Action; d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = true ? d : null; d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = d; for (int i = 0; i < 2; i++) { e(); d += B; }")]
    [InlineData("System.Action d = A; System.Action e = d; d += B; System.Action g = () => e(); g();")]
    [InlineData("System.Action d = A; Fld = d; d += B;")]
    [InlineData("System.Action d = A; System.Action[] arr = { d }; d += B; arr[0]();")]
    [InlineData("System.Action d = A; Take(out Fld); Fld = d; d -= B; Fld();")]
    [InlineData("System.Action d = A; if (d is System.Action f) { d += B; f(); }")]
    [InlineData("System.Action d = A; Fld = d; for (int i = 0; i < 2; i++) { d += B; }")]
    [InlineData("Fld = A; System.Action e = Fld; Fld += B; e();")]
    [InlineData("System.Action d = A; System.Action f = null; System.Action e = null; e = f = d; d += B; e();")]
    [InlineData("System.Action d = A; System.Action f = null; System.Action e = (f = d); d += B; e();")]
    [InlineData("System.Action d = A; System.Action f = null; System.Action e = true ? (f = d) : null; d += B; e();")]
    [InlineData("System.Action d = A; System.Action e = (d += A); d += B; e();")]
    [InlineData("System.Func<System.Action> g = null; System.Action d = A; g = () => d; var e = g(); d += B; e();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d += (e = d); e();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d -= (e ??= d); e();")]
    [InlineData("System.Action d = A; d += B; d -= (Fld = d); Fld();")]
    [InlineData("System.Action d = A; System.Action e = null; d -= (e = d); e();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d += e = d; e();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; System.Action f = null; d -= (e = (f = d)); f();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d -= (true ? (e = d) : null); e();")]
    [InlineData("System.Action d = A; d += B; System.Action[] arr = new System.Action[1]; d -= (arr[0] = d); arr[0]();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d -= (d is System.Action f ? (e = f) : null); e();")]
    [InlineData("System.Action d = A; d += B; System.Action e = null; d += (e = d) == null ? A : B; e();")]
    [InlineData("Fld = A; Fld += B; System.Action e = null; Fld -= (e = Fld); e();")]
    [InlineData("System.Action d = A; d += B; bool same = (d += B) == d;")]
    // Copies taken after the last += or never used afterwards: refused too, the rule is flow-insensitive.
    [InlineData("System.Action d = A; d += B; System.Action e = d; e();")]
    [InlineData("System.Action d = A; System.Action e = d; e(); d += B;")]
    [InlineData("System.Action d = A; System.Action e = d; e(); d += B; e = null;")]
    [InlineData("System.Action d = A; System.Action e = null; if (log == 0) { e = d; e(); } else { d += B; }")]
    [InlineData("System.Action d = A; d += B; if (d is System.Action f) f();")]
    [InlineData("System.Action d = A; d += B; System.Action x = d; log = 1; d = null; d += B; x();")]
    [InlineData("System.Action d = A; d += B; System.Action x = d; d = A; d += B; x();")]
    [InlineData("System.Action d = A; d += B; System.Action x = (log == 0 ? d : null); d = null; d += B; x();")]
    [InlineData("Fld = A; Fld += B; System.Action ef = Fld; ef();")]
    [InlineData("for (int i = 0; i < 2; i++) { System.Action d = A; System.Action e = d; e(); d += B; }")]
    public void A_compound_assignment_on_a_delegate_that_is_copied_is_ES0099(string body)
    {
        var r = Compile(Program(CopyMembers, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(Copied));
    }

    private const string ReviewMembers = "static void C() { log = log * 10 + 3; } static void Call2(System.Action a, System.Action b) { a(); } static void Run(System.Action a, bool x) { a(); } class H2 { public H2(System.Action a, bool b) { a(); } } static void Ab(bool x) { log = log * 10 + 1; } static void Bb(bool x) { log = log * 10 + 2; }";

    [Theory]
    // A copy or a += / -= in a switch section's when guard runs before the later sections.
    [InlineData("int k = 0; System.Action d = A; d += B; System.Action e = null; switch (k) { case 0 when (e = d) == null: break; default: d += C; e(); break; }")]
    [InlineData("int k = 0; System.Action d = A; d += B; System.Action e = d; switch (k) { case 0 when (d += C) == null: break; default: e(); break; }")]
    [InlineData("int k = 0; System.Action d = A; d += B; System.Action e = d; switch (k) { case 0 when (d -= B) == null: break; case 0: e(); break; }")]
    [InlineData("int k = 0; Fld = A; Fld += B; System.Action e = null; switch (k) { case 0 when (e = Fld) == null: break; default: Fld += C; e(); break; }")]
    [InlineData("int k = 0; Fld = A; Fld += B; System.Action e = Fld; switch (k) { case 0 when (Fld += C) == null: break; default: e(); break; }")]
    [InlineData("int k = 0; Fld = A; Fld += B; System.Action e = Fld; switch (k) { case 0 when (Fld -= B) == null: break; case 0: e(); break; }")]
    // A copy passed in an earlier argument than a += / -= in the same call: every argument is evaluated before the callee runs.
    [InlineData("System.Action d = A; d += B; System.Action e = d; Call2(e, d += C);")]
    [InlineData("System.Action d = A; d += B; System.Action e = d; Run(e, (d += C) == null);")]
    [InlineData("System.Action d = A; d += B; System.Action e = d; new H2(e, (d += C) == null);")]
    [InlineData("System.Action d = A; d += B; System.Action e = d; System.Action f = e; Call2(f, d -= B);")]
    [InlineData("System.Action<bool> d = Ab; d += Bb; System.Action<bool> e = d; e((d += Bb) == null);")]
    [InlineData("Fld = A; Fld += B; System.Action e = Fld; Run(e, (Fld += C) == null);")]
    [InlineData("System.Action d = A; d += B; Call2(d, d += B);")]
    public void The_shapes_of_re_review_2_are_ES0099(string body)
    {
        var r = Compile(Program(CopyMembers + ReviewMembers, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(Copied));
    }

    [Theory]
    [InlineData("static System.Action Get() => Fld;", "Fld = A; Fld += A; var e = Get(); Fld += B; e();")]
    [InlineData("static System.Action Get() { return Fld; }", "Fld = A; Fld += A; var e = Get(); Fld += B; e();")]
    [InlineData("static System.Action Prop => Fld;", "Fld = A; Fld += A; var e = Prop; Fld += B; e();")]
    [InlineData("static System.Func<int> FI; static System.Func<int> GetI() => FI; static int One() => 1;", "FI = One; FI += One; var e = GetI(); FI += One; e();")]
    [InlineData("class C { public System.Action X; public System.Action Get() => X; }", "var c = new C(); c.X = A; c.X += A; var e = c.Get(); c.X += B; e();")]
    [InlineData("static void Other() { Fld += B; }", "Fld = A; Other(); System.Action e = Fld; e();")]
    [InlineData("static void Other() { Fld += B; System.Action e = Fld; e(); }", "Other();")]
    [InlineData("static void Other() { Fld += B; Fld += B; System.Action e = () => { }; e = Fld; } static void Third() { Fld -= B; }", "")]
    [InlineData("static void Pp([Owned] System.Action d) { System.Action e = d; d += B; e(); }", "")]
    public void A_copy_returned_or_taken_in_another_method_or_a_parameter_is_ES0099(string members, string body)
    {
        var r = Compile(Program(CopyMembers + members, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(Copied));
    }

    [Theory]
    [InlineData("static void Use(System.Action a) { Fld += B; a(); }", "Fld = A; Fld += B; Use(Fld);")]
    [InlineData("static void Use(System.Action a) { Fld -= B; a(); }", "Fld = A; Fld += B; Use(Fld);")]
    [InlineData("static void Use(in System.Action a) { Fld += B; a(); }", "Fld = A; Fld += B; Use(in Fld);")]
    [InlineData("class H { public System.Action X; public void Run(System.Action a) { X += B; a(); } }", "var h = new H(); h.X = A; h.X += B; h.Run(h.X);")]
    [InlineData("static void Use(System.Action a, System.Action g) { g(); a(); }", "System.Action d = A; d += B; Use(d, () => { d += B; });")]
    // Passing the variable to a call is a copy now, also where no += can run inside the call (the earlier run tests).
    [InlineData("static void Use(System.Action a) { a(); }", "System.Action d = A; Use(d); d += B; Use(d);")]
    [InlineData("static void Use(System.Action a) { a(); }", "Fld2 = A; Fld2 += B; Use(Fld2); Fld2 += B; Use(Fld2);")]
    public void A_delegate_passed_to_a_call_is_ES0099(string members, string body)
    {
        var r = Compile(Program(CopyMembers + members, body));
        Assert.Single(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(Copied));
    }

    private const string ConversionMembers = "public class Bx { public static implicit operator int(Bx b) { Fld += B; return 1; } public static implicit operator System.Action(Bx b) { Fld += B; return A; } }";

    [Theory]
    // A call of the variable whose arguments run code: C may load the variable before the arguments.
    [InlineData("static System.Action<int> G; static int Grow() { G += (int x) => { }; return 1; }", "G = (int x) => { }; G += (int x) => { }; G(Grow());")]
    // A += / -= whose operand runs code: C may load the old list before the operand.
    [InlineData("static System.Action Make() { Fld += B; return A; }", "Fld = A; Fld += Make();")]
    // A user-defined implicit conversion that the argument, operand or += applies runs code too.
    [InlineData(ConversionMembers, "System.Action<int> F1 = (int x) => { }; F1 += (int x) => { }; Bx bx = new Bx(); F1(bx);")]
    [InlineData(ConversionMembers, "System.Action<int, int> F2 = (int x, int y) => { }; F2 += (int x, int y) => { }; Bx bx = new Bx(); F2(3, bx);")]
    [InlineData(ConversionMembers, "Fld = A; Fld += B; Bx bx = new Bx(); Fld += bx;")]
    [InlineData(ConversionMembers, "System.Action d = A; d += B; Bx bx = new Bx(); bool same = bx == d; if (same) log = 1;")]
    public void A_read_next_to_an_argument_or_operand_that_runs_code_is_ES0099(string members, string body)
    {
        var r = Compile(Program(CopyMembers + members, body));
        Assert.Single(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains("an argument or operand that runs code"));
    }

    private const string Review3Members = @"
static void C() { log = log * 10 + 3; }
public class Box { public System.Action F; }
public class Outer { public Box In; }
public class Box2 { public System.Action Q { get; set; } }
static System.Action Keep;
static void Look(Box b) { if (b is { F: var e }) Keep = e; }
static void Grow(Box b) { b.F += B; }
static Box2 b1 = new Box2(), b2 = new Box2(); static int n;
static Box2 Cur { get { n++; return n == 1 ? b1 : b2; } }
static Box keep = new Box();
static Box Fresh { get { Box r = keep; keep = new Box(); return r; } }
public class Bx { public static int operator +(Bx b, System.Action a) { Fld += C; a(); return 1; } }
static System.Action Hook;
public class Bh { public static int operator +(Bh b, System.Action a) { Hook(); a(); return 1; } }
public class Bs { public Bs(System.Action a) { Fld += C; a(); } }
public class Dv : Bs { public Dv() : base(Fld) { } }
public class Bs2 { public Bs2(System.Action a, int k) { Fld += C; a(); } public Bs2() : this(Fld, 1) { } }
";

    [Theory]
    // Property patterns copy the delegate.
    [InlineData("Box b = new Box(); b.F = A; b.F += B; if (b is { F: var e }) { b.F += C; e(); }")]
    [InlineData("Box b = new Box(); b.F = A; b.F += B; System.Action e = b switch { { F: var f } => f, _ => null }; b.F += C; e();")]
    [InlineData("Box b = new Box(); b.F = A; b.F += B; switch (b) { case { F: System.Action e }: b.F += C; e(); break; }")]
    [InlineData("Outer o = new Outer(); o.In = new Box(); o.In.F = A; o.In.F += B; if (o is { In.F: var e }) { o.In.F += C; e(); }")]
    [InlineData("Box b = new Box(); b.F = A; b.F += B; Look(b); Grow(b); Keep();")]
    // The move only follows this, types, locals, parameters and fields: a property link may run code or answer with another object.
    [InlineData("b1.Q = A; b1.Q += B; System.Action x = Cur.Q; Cur.Q = null; b1.Q += C; x();")]
    [InlineData("keep.F = A; keep.F += B; Box first = keep; System.Action x = Fresh.F; Fresh.F = null; first.F += C; x();")]
    // Calls that are not written as an invocation: user-defined operators and constructor initializers.
    [InlineData("Fld = A; Fld += B; int k = new Bx() + Fld;")]
    [InlineData("System.Action d = A; d += B; Hook = () => d += C; int k = new Bh() + d;")]
    [InlineData("Fld = A; Fld += B; new Dv();")]
    [InlineData("Fld = A; Fld += B; new Bs2();")]
    public void The_shapes_of_re_review_3_are_ES0099(string body)
    {
        var r = Compile(Program(CopyMembers + Review3Members, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(Copied));
    }

    [Fact]
    public void The_copy_check_reports_once_per_variable_and_names_the_copy()
    {
        var r = Compile(Program(CopyMembers, "System.Action d = A;\nSystem.Action e = d; d += B; d += A; d -= B; e();"));
        var found = Assert.Single(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains(Copied));
        Assert.Contains("line ", found.GetMessage());
    }

    [Fact]
    public void Compound_assignments_without_copies_run_clean()
    {
        if (!HasGcc) return;
        const string members = CopyMembers + @"
static void AddB() => Fld += B;
static void M9() { System.Action d = A; d += B; d += d; d(); Heap.Free(ref d); }
static void M10() { System.Action d = A; d += B; d -= d; if (d == null) log = 7; }
static void M11() { System.Action d = A; d += B; d += (log == 0 ? d : A); d(); Heap.Free(ref d); }
public class Box { public System.Action F; }
static void M12() { Box o = new Box(); o.F = A; o.F += B; System.Action x = o.F; o.F = null; o.F += B; x(); o.F(); Heap.Free(ref x); Heap.Free(ref o.F); Heap.Free(ref o); }
static void M16() { System.Action d = A; d += B; if (d != null && !(d is null)) d.Invoke(); d?.Invoke(); Heap.Free(ref d); }
static void M13() { System.Action d = A; System.Action add = () => d += B; add(); add(); d(); Heap.Free(ref d); Heap.Free(ref add); }
static void M14() { System.Action d = A; for (int i = 0; i < 2; i++, d += B) { } d(); Heap.Free(ref d); }
static void M15() { System.Action d = A; d += B; System.Action x = d; d = null; d += B; x(); d(); Heap.Free(ref x); Heap.Free(ref d); }
";
        const string body = @"
int base0 = Heap.LiveClosures();
log = 0; M9(); Check(log == 1212, ""d += d"");
log = 0; M10(); Check(log == 7, ""d -= d"");
log = 0; M11(); Check(log == 1212, ""d += (c ? d : A)"");
log = 0; M12(); Check(log == 122, ""a move through a local's field"");
log = 0; M16(); Check(log == 1212, ""null tests, Invoke and ?.Invoke"");
log = 0; Fld = A; AddB(); AddB(); Fld(); Heap.Free(ref Fld); Check(log == 122, ""a += in an expression-bodied void method"");
log = 0; M13(); Check(log == 122, ""a += as the body of a void lambda"");
log = 0; M14(); Check(log == 122, ""a += in a for incrementor"");
log = 0; M15(); Check(log == 122, ""a move (copy, then null the source) before a +="");
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        var (code, output) = Run(members, body);

        Assert.Equal(OkLines("d += d", "d -= d", "d += (c ? d : A)", "a move through a local's field",
            "null tests, Invoke and ?.Invoke", "a += in an expression-bodied void method", "a += as the body of a void lambda",
            "a += in a for incrementor", "a move (copy, then null the source) before a +=", "nothing leaks"), output);
        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("static void A() { } static void B() { }", "System.Action d = A; System.Action e = d + B; e();", "binary + on delegates")]
    [InlineData("static void A() { } static void B() { }", "System.Action d = A; d += B; System.Action e = d - B; e();", "binary - on delegates")]
    [InlineData("static void A() { } static void B() { }", "System.Action d = A; d += (System.Action)A + B;", "binary + on delegates")]
    public void Binary_plus_and_minus_on_delegates_are_ES0099(string members, string body, string message)
    {
        var r = Compile(Program(members, body));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.GetMessage().Contains(message));
    }

    [Fact]
    public void A_multicast_local_given_to_an_owned_parameter_or_a_setter_hands_its_list_over()
    {
        if (!HasGcc) return;
        const string members = @"
static int log;
static void A() { log = log * 10 + 1; }
static void B() { log = log * 10 + 2; }
static void Take([Owned] System.Action a) { a(); Heap.Free(ref a); }
static System.Action _held;
static System.Action Held { get { return _held; } set { _held = value; } }
static void M1() { System.Action d = A; d += B; Take(d); }
static void M2() { int k = 0; System.Action d = () => k++; d += B; Take(d); }
static void M3() { System.Action d = A; d += B; d += A; d -= A; Held = d; }
static void M4() { System.Action d = A; d += B; Take(d); d = null; }
static void M5() { System.Action d = A; d += B; Take(d); Heap.Free(ref d); }
";
        const string body = @"
int base0 = Heap.LiveClosures();
log = 0; M1();
Check(log == 12 && Heap.LiveClosures() == base0, ""a method-group multicast moves to an [Owned] parameter"");
log = 0; M2();
Check(log == 2 && Heap.LiveClosures() == base0, ""a multicast with a lambda moves too"");
M3();
log = 0; Held();
Check(log == 12, ""a multicast moves to a setter"");
Heap.Free(ref _held);
Check(Heap.LiveClosures() == base0, ""and the setter's owner frees it"");
log = 0; M4();
Check(log == 12 && Heap.LiveClosures() == base0, ""set to null after the hand-over"");
log = 0; M5();
Check(log == 12 && Heap.LiveClosures() == base0, ""freed after the hand-over: the callee had its own reference"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a method-group multicast moves to an [Owned] parameter", "a multicast with a lambda moves too", "a multicast moves to a setter",
            "and the setter's owner frees it", "set to null after the hand-over", "freed after the hand-over: the callee had its own reference"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Compound_assignment_after_owned_values_through_coalescing_and_Heap_Free_runs_clean()
    {
        if (!HasGcc) return;
        const string members = @"
static int log;
static void A() { log = log * 10 + 1; }
static void B() { log = log * 10 + 2; }
static void C() { log = log * 10 + 3; }
";
        const string body = @"
int base0 = Heap.LiveClosures();
int n = 0;
System.Action d = null;
d ??= () => n++;
d ??= A;
d += B;
log = 0; d();
Check(n == 1 && log == 2, ""??= with a lambda, then +="");
Heap.Free(ref d);
d += A;
d += C;
log = 0; d();
Check(log == 13, ""+= again after Heap.Free(ref d)"");
d -= A;
d += B;
log = 0; d();
Check(log == 32, ""-= then += on the same variable"");
Heap.Free(ref d);
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("??= with a lambda, then +=", "+= again after Heap.Free(ref d)", "-= then += on the same variable", "nothing leaks"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Delegate_equality_compares_the_function_and_the_environment_and_multicast_entries()
    {
        if (!HasGcc) return;
        const string members = @"
static void A() { }
static void B() { }
static void C() { }
";
        const string body = @"
int base0 = Heap.LiveClosures();
System.Action x = A; x += B;
System.Action y = A; y += C;
Check(x != y && !(x == y), ""multicast delegates with different entries differ"");
System.Action z = A; z += B;
Check(x == z && !(x != z), ""multicast delegates with the same entries are equal"");
System.Action p = A;
Check(p == A && p != B, ""a method group against a delegate"");
int n = 0;
System.Action l1 = () => n++, l2 = () => n++;
Check(l1 != l2 && l1 == l1, ""two lambdas with their own closures differ"");
Check(x != p && p != null, ""multicast against plain, and null"");
Heap.Free(ref x); Heap.Free(ref y); Heap.Free(ref z); Heap.Free(ref l1); Heap.Free(ref l2);
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("multicast delegates with different entries differ", "multicast delegates with the same entries are equal",
            "a method group against a delegate", "two lambdas with their own closures differ", "multicast against plain, and null", "nothing leaks"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Static_fields_of_reference_type_in_structs_are_allowed_and_initialised()
    {
        if (!HasGcc) return;
        const string members = @"
struct Table<T> { public static int[] Squares = { 1, 4, 9 }; public static string Name = ""tbl""; public int V; public static int Total; public T X; }
struct Plain { public static int[] Values = { 5, 6 }; public static System.Action Hook; public int V; }
class Box { public int N = 7; }
struct WithBox { public static Box Shared = new Box(); public int V; }
static void Hit() { Table<int>.Total += 1; }
";
        const string body = @"
Check(Table<int>.Squares[2] == 9 && Table<int>.Squares.Length == 3, ""a static array in a generic struct"");
Check(Table<int>.Name == ""tbl"", ""a static string in a generic struct"");
Check(Plain.Values[1] == 6, ""a static array in a struct"");
Plain.Hook += Hit; Plain.Hook += Hit;
Plain.Hook();
Check(Table<int>.Total == 2, ""a static delegate in a struct, with +="");
Plain.Hook -= Hit; Plain.Hook -= Hit;
Check(Plain.Hook == null, ""and -="");
WithBox.Shared.N++;
Check(WithBox.Shared.N == 8, ""a static class instance in a struct"");
Plain p = new Plain(); p.V = 3;
Check(p.V == 3, ""the struct itself is still a value"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a static array in a generic struct", "a static string in a generic struct", "a static array in a struct",
            "a static delegate in a struct, with +=", "and -=", "a static class instance in a struct", "the struct itself is still a value"), output);
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

    // ---- frames release the delegates they own (issue #85): each runs under AddressSanitizer/LeakSanitizer when available.

    [Fact]
    public void A_frame_releases_an_owned_parameter_captured_by_a_stored_lambda()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int> Twice([Owned] System.Func<int> f) => () => f() + f();
static System.Action Kept;
static void Keep([Owned] System.Action a) { int n = 0; Kept = () => { n++; a(); }; }
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
System.Func<int> twice = Twice(() => ++t);
Check(twice() == 3, ""a stored lambda calls the owned parameter it captured"");
Check(Heap.LiveClosures() == base0 + 2, ""the outer closure and the captured one are both counted"");
Heap.Free(ref twice);
Check(Heap.LiveClosures() == base0, ""freeing the outer closure releases the captured one"");
Keep(() => t += 10);
Kept(); Kept();
Check(t == 22, ""a block-bodied stored lambda over an owned parameter"");
Heap.Free(ref Kept);
Check(Heap.LiveClosures() == base0, ""nothing left over"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a stored lambda calls the owned parameter it captured", "the outer closure and the captured one are both counted",
            "freeing the outer closure releases the captured one", "a block-bodied stored lambda over an owned parameter", "nothing left over"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void A_frame_releases_a_setter_value_captured_by_a_stored_lambda()
    {
        if (!HasGcc) return;
        const string members = @"
public class Relay
{
    System.Action _fire;
    public System.Action Target { set { _fire = () => { value(); value(); }; } }
    public void Fire() { _fire(); }
    public void Dispose() { Heap.Free(ref _fire); }
}
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
Relay r = new Relay();
r.Target = () => t++;
r.Fire();
Check(t == 2, ""the stored lambda calls the setter's value"");
r.Dispose();
Heap.Free(ref r);
Check(Heap.LiveClosures() == base0, ""the setter's frame released its value"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("the stored lambda calls the setter's value", "the setter's frame released its value"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Aliases_captured_by_a_stored_lambda_are_not_released_by_the_frame()
    {
        if (!HasGcc) return;
        // Each alias has another owner that frees it: a double free here would stop the sanitized run.
        const string members = @"
static System.Action Field;
static System.Action Kept;
static System.Action Other;
static void FromField() { System.Action a = Field; Kept = () => a(); }
static void StoredToo([Owned] System.Action a) { Other = a; Kept = () => a(); }
static void CopiedOut() { int n = 0; System.Action a = () => n++; Other = a; Kept = () => a(); }
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
Field = () => t++;
FromField();
Kept();
Heap.Free(ref Kept);
Check(t == 1 && Heap.LiveClosures() == base0 + 1, ""a local copied from a field is left to the field"");
Heap.Free(ref Field);
StoredToo(() => t += 10);
Kept();
Heap.Free(ref Kept);
Other();
Check(t == 21 && Heap.LiveClosures() == base0 + 1, ""an owned parameter that was also stored is left to the field"");
Heap.Free(ref Other);
CopiedOut();
Kept();
Heap.Free(ref Kept);
Other();
Check(Heap.LiveClosures() == base0 + 1, ""a local copied into a field is left to the field"");
Heap.Free(ref Other);
Check(Heap.LiveClosures() == base0, ""nothing left over"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a local copied from a field is left to the field", "an owned parameter that was also stored is left to the field",
            "a local copied into a field is left to the field", "nothing left over"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void A_handed_over_local_and_a_cleared_field_are_released_once()
    {
        if (!HasGcc) return;
        // A local given to an [Owned] parameter and never used again hands its reference over (no new one, so no leak). A field
        // given to a setter takes a reference of its own (issue #110): clearing the field without Heap.Free now leaks its original
        // (counted below), freeing it is the right thing (see the next tests).
        const string members = @"
public class Relay
{
    System.Action _fire;
    public System.Action Target { set { _fire = () => value(); } }
    public void Fire() { _fire(); }
    public void Dispose() { Heap.Free(ref _fire); }
}
static System.Action Field;
static System.Func<int> Twice([Owned] System.Func<int> f) => () => f() + f();
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
System.Func<int> one = () => ++t;
System.Func<int> twice = Twice(one);
Check(twice() == 3, ""a local handed to an owned parameter"");
Heap.Free(ref twice);
Check(Heap.LiveClosures() == base0, ""the callee's frame released the local's closure"");
Field = () => t += 10;
Relay r = new Relay();
r.Target = Field;
r.Fire();
Check(t == 12, ""a field given to a setter"");
r.Dispose();
Heap.Free(ref r);
Check(Heap.LiveClosures() == base0 + 1, ""the setter's frame released its own reference, the field keeps one"");
Heap.Free(ref Field);
Check(Heap.LiveClosures() == base0, ""freeing the field releases the last one"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a local handed to an owned parameter", "the callee's frame released the local's closure",
            "a field given to a setter", "the setter's frame released its own reference, the field keeps one", "freeing the field releases the last one"), output);
        Assert.Equal(0, code);
    }

    // ---- shared values given to [Owned] parameters and setters keep their own reference (issue #110): each runs under
    // AddressSanitizer/LeakSanitizer/UBSan when available (a use after free or a leak at exit fails the run).

    [Fact]
    public void Issue110_a_field_given_to_a_capturing_setter_and_then_freed()
    {
        if (!HasGcc) return;
        const string members = @"
public class Relay
{
    System.Action _fire;
    public System.Action Target { set { _fire = () => value(); } }
    public void Fire() { _fire(); }
    public void Dispose() { Heap.Free(ref _fire); }
}
static System.Action Field;
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
Field = () => t++;
Relay r = new Relay();
r.Target = Field;
Heap.Free(ref Field);
r.Fire(); r.Fire();
Check(t == 2, ""the setter's closure still runs after the caller freed the field"");
r.Dispose();
Heap.Free(ref r);
Check(Heap.LiveClosures() == base0, ""each reference released once"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("the setter's closure still runs after the caller freed the field", "each reference released once"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_a_local_given_to_an_owned_parameter_and_also_freed()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int> Twice([Owned] System.Func<int> f) => () => f() + f();
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
System.Func<int> one = () => ++t;
System.Func<int> twice = Twice(one);
Check(one() == 1, ""the local is still the caller's"");
Heap.Free(ref one);
Check(twice() == 5, ""the returned closure still calls it"");
Heap.Free(ref twice);
Check(Heap.LiveClosures() == base0, ""each reference released once"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("the local is still the caller's", "the returned closure still calls it", "each reference released once"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_a_field_given_to_a_storing_setter_owned_constructor_and_initializer_then_freed()
    {
        if (!HasGcc) return;
        const string members = @"
public class Holder
{
    System.Action _run;
    public Holder() { }
    public Holder([Owned] System.Action run) { _run = run; }
    public System.Action Run { set { _run = value; } }
    public void Go() { _run(); }
    public void Dispose() { Heap.Free(ref _run); }
}
static System.Action Field;
static System.Action[] Arr = new System.Action[1];
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
Field = () => t++;
Holder a = new Holder();
a.Run = Field;
Holder b = new Holder(Field);
Holder c = new Holder { Run = Field };
Arr[0] = () => t += 100;
Holder d = new Holder(Arr[0]);
Heap.Free(ref Field);
Heap.Free(ref Arr[0]);
a.Go(); b.Go(); c.Go(); d.Go();
Check(t == 103, ""a storing setter, an owned constructor parameter and an initializer keep their own"");
a.Dispose(); b.Dispose(); c.Dispose(); d.Dispose();
Heap.Free(ref a); Heap.Free(ref b); Heap.Free(ref c); Heap.Free(ref d);
Check(Heap.LiveClosures() == base0, ""each reference released once"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a storing setter, an owned constructor parameter and an initializer keep their own", "each reference released once"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_fresh_values_and_plain_locals_are_handed_over_without_a_new_reference()
    {
        if (!HasGcc) return;
        // A new lambda, a call that always returns one, and locals handed over for good (also an [Owned] parameter and a setter's
        // value passed on) take no reference: with one, these would leak, which LeakSanitizer and LiveClosures would show.
        const string members = @"
public class Holder
{
    System.Action _run;
    public System.Action Run { set { _run = value; } }
    public System.Action Inner { set { Run = value; } }
    public void Go() { _run(); }
    public void Dispose() { Heap.Free(ref _run); }
}
static System.Action Kept;
static void Keep([Owned] System.Action a) { Kept = a; }
static void Pass([Owned] System.Action a) { Keep(a); }
static System.Action Make(int k) => () => Kept2 += k;
static int Kept2;
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
Keep(() => t++);
Kept();
Heap.Free(ref Kept);
Keep(Make(5));
Kept();
Heap.Free(ref Kept);
System.Action local = () => t += 10;
local();
Pass(local);
Kept();
Heap.Free(ref Kept);
Holder h = new Holder();
System.Action other = () => t += 100;
h.Inner = other;
h.Go();
h.Dispose();
h.Run = () => t += 1000;
h.Go();
h.Dispose();
Heap.Free(ref h);
Check(t == 1121 && Kept2 == 5, ""fresh lambdas, a fresh call and locals handed over all ran"");
Check(Heap.LiveClosures() == base0, ""no reference left over"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("fresh lambdas, a fresh call and locals handed over all ran", "no reference left over"), output);
        Assert.Equal(0, code);
        var c = CompileOk(Program(members + Check, body));
        Assert.DoesNotContain("es_closure_retain(_ho", c);
    }

    [Fact]
    public void Issue110_locals_used_again_given_twice_or_in_a_loop_keep_their_own_and_are_freed()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Action[] Kept = new System.Action[4];
static int N;
static void Keep([Owned] System.Action a) { Kept[N++] = a; }
static System.Func<int> Twice([Owned] System.Func<int> f) => () => f() + f();
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
System.Action a = () => t++;
Keep(a);
Keep(a);
Heap.Free(ref a);
System.Action b = () => t += 10;
for (int i = 0; i < 2; i++) Keep(b);
Heap.Free(ref b);
for (int i = 0; i < 4; i++) Kept[i]();
Check(t == 22, ""each kept copy runs after the local was freed"");
for (int i = 0; i < 4; i++) Heap.Free(ref Kept[i]);
int u = 0;
System.Func<int> f = () => ++u;
System.Func<int> g = Twice(f);
Check(f() == 1 && g() == 5, ""a local still called after it was given away"");
Heap.Free(ref f);
Heap.Free(ref g);
Check(Heap.LiveClosures() == base0, ""each reference released once"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("each kept copy runs after the local was freed", "a local still called after it was given away", "each reference released once"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_a_captured_local_given_to_an_owned_parameter_is_released_by_its_frame()
    {
        if (!HasGcc) return;
        // h is captured by a stored lambda and also given to Keep: Keep takes its own reference, and the method's frame releases h's.
        const string members = @"
static System.Action Kept;
static System.Action Other;
static void Keep([Owned] System.Action a) { Kept = a; }
static void Wire(int k) { System.Action h = () => N += k; Other = () => h(); Keep(h); }
static int N;
";
        const string body = @"
int base0 = Heap.LiveClosures();
Wire(3);
Kept(); Other();
Check(N == 6, ""both owners call it"");
Heap.Free(ref Kept);
Other();
Heap.Free(ref Other);
Check(N == 9 && Heap.LiveClosures() == base0, ""each reference released once"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("both owners call it", "each reference released once"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_a_local_given_away_twice_and_never_freed_is_ES0013()
    {
        const string members = @"
static System.Action[] Kept = new System.Action[2];
static void Keep([Owned] System.Action a, int i) { Kept[i] = a; }
static void Twice(int k) { System.Action a = () => k++; Keep(a, 0); Keep(a, 1); }
static void Once(int k) { System.Action a = () => k++; Keep(a, 0); }
static void Freed(int k) { System.Action a = () => k++; Keep(a, 0); Keep(a, 1); Heap.Free(ref a); }
";
        var r = Compile(Program(members, ""));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var es13 = r.Diagnostics.Where(d => d.Id == "ES0013").ToList();
        Assert.Single(es13);
        Assert.Contains("'a' is given to the [Owned] parameter 'a'", es13[0].GetMessage());
    }

    [Fact]
    public void Issue110_review_a_local_borrowed_by_a_stack_lambda_and_then_given_away_does_not_leak()
    {
        if (!HasGcc) return;
        // a is captured only by lambdas on the stack: no frame holds it. Given to Keep after the borrowing call it is handed over;
        // still called by a stack local after Drop it keeps its own and is freed by the caller.
        const string members = @"
static System.Action Kept;
static void Keep([Owned] System.Action a) { Kept = a; }
static void Drop([Owned] System.Action a) { Heap.Free(ref a); }
static void Run(System.Action f) { f(); }
";
        const string body = @"
int base0 = Heap.LiveClosures();
int t = 0;
System.Action a = () => t++;
Run(() => a());
Keep(a);
Kept();
Heap.Free(ref Kept);
Check(t == 2 && Heap.LiveClosures() == base0, ""a borrowed capture before the hand-over leaves nothing behind"");
System.Action b = () => t += 10;
System.Action c = () => b();
Drop(b);
c();
Heap.Free(ref b);
Check(t == 12 && Heap.LiveClosures() == base0, ""a stack local still calling it keeps the caller's reference"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("a borrowed capture before the hand-over leaves nothing behind", "a stack local still calling it keeps the caller's reference"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Issue110_review_a_free_or_store_on_another_branch_does_not_leak()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Action Kept;
static void Keep([Owned] System.Action a) { Kept = a; }
static void FreeOrKeep(bool x, int k) { System.Action c = () => N += k; if (x) Heap.Free(ref c); else Keep(c); }
static int Pick(int x, int k) { System.Action c = () => N += k; switch (x) { case 1: Keep(c); return 1; default: Heap.Free(ref c); return 0; } }
public class Holder
{
    System.Action _f;
    System.Action _g;
    public bool K;
    public System.Action Inner { set { _g = value; } }
    public System.Action Both { set { if (K) _f = value; else Inner = value; } }
    public void Go() { if (_f != null) _f(); if (_g != null) _g(); }
    public void Dispose() { Heap.Free(ref _f); Heap.Free(ref _g); }
}
static int N;
";
        const string body = @"
int base0 = Heap.LiveClosures();
FreeOrKeep(true, 1);
FreeOrKeep(false, 2);
Kept();
Heap.Free(ref Kept);
Pick(1, 4); Kept(); Heap.Free(ref Kept);
Pick(0, 8);
Check(N == 6 && Heap.LiveClosures() == base0, ""Heap.Free on one branch, a hand-over on the other"");
int t = 0;
Holder h = new Holder();
h.K = true;
h.Both = () => t++;
h.K = false;
h.Both = () => t += 10;
h.Go();
h.Dispose();
Heap.Free(ref h);
Check(t == 11 && Heap.LiveClosures() == base0, ""a setter storing value on one branch and passing it on on the other"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("Heap.Free on one branch, a hand-over on the other", "a setter storing value on one branch and passing it on on the other"), output);
        Assert.Equal(0, code);
        var c = CompileOk(Program(members + Check, body));
        Assert.DoesNotContain("es_closure_retain(_ho", c);
    }

    [Fact]
    public void Issue110_review_ES0013_for_borrowed_captures_and_frees_on_another_branch()
    {
        const string members = @"
static System.Action[] Kept = new System.Action[2];
static void Keep([Owned] System.Action a, int i) { Kept[i] = a; }
static void Run(System.Action f) { f(); }
static void Borrowed(int k) { System.Action a = () => k++; Run(() => a()); Keep(a, 0); Keep(a, 1); }
static void Branch(bool x, int k) { System.Action b = () => k++; if (x) Heap.Free(ref b); else { Keep(b, 0); b(); } }
static void BothFree(bool x, int k) { System.Action d = () => k++; if (x) { Keep(d, 0); d(); Heap.Free(ref d); } else Heap.Free(ref d); }
// Not covered (docs/STATUS.md): a free under a condition of its own on the hand-over's path silences the warning.
static void Cond(bool y, int k) { System.Action e = () => k++; Keep(e, 0); e(); if (y) Heap.Free(ref e); }
";
        var r = Compile(Program(members, ""));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var es13 = r.Diagnostics.Where(d => d.Id == "ES0013").Select(d => d.GetMessage()).ToList();
        Assert.Equal(2, es13.Count);
        Assert.Contains(es13, m => m.StartsWith("'a' is given"));
        Assert.Contains(es13, m => m.StartsWith("'b' is given"));
    }

    [Fact]
    public void Nested_frames_release_their_own_delegates()
    {
        if (!HasGcc) return;
        const string members = @"
static System.Func<int>[] Fs = new System.Func<int>[3];
static int Outer;
// The local h of each loop body holds a closure over the method's frame and is owned by the body's frame.
static void Fill([Owned] System.Func<int> seed)
{
    int k = 100;
    for (int i = 0; i < 3; i++)
    {
        int j = i;
        System.Func<int> h = () => k + seed();
        Fs[i] = () => h() + j;
    }
}
// A local given a lambda over its own scope: the frame holds it weakly, so the two do not keep each other alive.
static System.Func<int> SelfScope()
{
    int n = 5;
    System.Func<int> inc = () => ++n;
    return () => inc() * 2;
}
// A local given a fresh closure from a call.
static System.Func<int> Make(int v) => () => v;
static System.Func<int> FromCall()
{
    System.Func<int> g = Make(1);
    int w = 3;
    return () => g() + w;
}
";
        const string body = @"
int base0 = Heap.LiveClosures();
int one = 1;
Fill(() => Outer + one);
Check(Fs[0]() == 101 && Fs[2]() == 103, ""closures over nested frames"");
for (int i = 0; i < 3; i++) Heap.Free(ref Fs[i]);
Check(Heap.LiveClosures() == base0, ""each body frame released its local and the method frame its parameter"");
System.Func<int> s = SelfScope();
Check(s() == 12 && s() == 14, ""a local over its own frame"");
Check(Heap.LiveClosures() == base0 + 1, ""only the returned closure is counted"");
Heap.Free(ref s);
System.Func<int> q = FromCall();
Check(q() == 4, ""a local given a fresh call result"");
Heap.Free(ref q);
Check(Heap.LiveClosures() == base0, ""nothing left over"");
";
        var (code, output) = Run(members, body);
        Assert.Equal(OkLines("closures over nested frames", "each body frame released its local and the method frame its parameter",
            "a local over its own frame", "only the returned closure is counted", "a local given a fresh call result", "nothing left over"), output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Owned_frame_delegates_get_a_fini_and_aliases_do_not()
    {
        var c = CompileOk(Program(@"
static System.Action Kept;
static System.Action Field;
static void Own([Owned] System.Action a) { Kept = () => a(); }
static void Alias() { System.Action b = Field; Kept = () => b(); }", "Own(null); Heap.Free(ref Kept); Alias(); Heap.Free(ref Kept);"));
        Assert.Contains("->v_a.env); }", c);
        Assert.Contains("->hdr.fini = ", c);
        Assert.DoesNotContain("->v_b.env); }", c);
    }

    [Fact]
    public void Removing_a_lambda_literal_from_an_event_is_ES0015()
    {
        var r = Compile(Program(@"
public class B { public event System.Action Ev; public void Drop() { Heap.Free(ref Ev); } }
static int Count;
static void Wire(B b) { b.Ev += () => Count++; b.Ev -= () => Count++; b.Ev -= (System.Action)(() => { }); }", ""));
        Assert.Equal(2, r.Diagnostics.Count(x => x.Id == "ES0015"));
        Assert.Contains("b.Ev -= <lambda>", r.Diagnostics.First(x => x.Id == "ES0015").GetMessage());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));   // a warning, not an error

        // A capturing lambda there is also ES0099 (the emitter cannot hold it), and still gets the warning that says why it is pointless.
        r = Compile(Program(@"
public class B { public event System.Action Ev; public void Drop() { Heap.Free(ref Ev); } }
static void Wire(B b, int k) { b.Ev -= () => k++; }", ""));
        Assert.Contains(r.Diagnostics, x => x.Id == "ES0015");

        r = Compile(Program(@"
public class B { public event System.Action Ev; public void Drop() { Heap.Free(ref Ev); } }
static void Wire(B b, int k) { System.Action h = () => k++; b.Ev += h; b.Ev -= h; Heap.Free(ref h); }", ""));
        Assert.DoesNotContain(r.Diagnostics, x => x.Id == "ES0015");
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
        "a stored lambda over an [Owned] parameter",
        "a stored lambda over a setter's value",
        "a shared value given to a setter keeps its own reference",
        "a stored lambda that uses this",
        "a raise uses a snapshot: removal and addition wait for the next raise",
        "the removed handler is gone and the added one runs",
        "a static event",
        "Heap.Free on an event drops every handler",
        "+= on a local delegate calls in order",
        "-= removes the last match of a kept variable",
        "-= removes the other one",
        "Heap.Free on a multicast delegate",
        "method groups are added too",
        "removing the last entry leaves null",
        "a field and an auto-property",
        "a Func gives the last result",
        "a static array and a static delegate in a struct",
        "every stored closure was freed",
    };
}
