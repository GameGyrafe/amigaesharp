using ESharp.Target.Host;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// [Owned] hand-overs in a switch section's when guard (issue #173) and a delegate that removes or frees its own running closure
/// (issue #174): each program runs on dotnet and as C under AddressSanitizer and LeakSanitizer, with the same output.
/// </summary>
public class DelegateOwnershipTests
{
    private const string Check = @"static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }";

    private static string OkLines(params string[] names) => string.Concat(names.Select(n => "ok " + n + "\n"));

    /// <summary>The program on dotnet (the host build of Runtime and Platform) and its C under the sanitizers: the output of both.</summary>
    private static void RunBoth(string members, string body, string expected)
    {
        var src = Program(members + Check, body);
        var compilation = CSharpCompilation.Create("ownership", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences(),
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
        if (!HasGcc) return;
        var c = CompileOk(src);
        var (code, output) = HasSanitizers ? RunSanitized(c) : RunWithHostGcc(c);
        Assert.Equal(expected, output);
        Assert.Equal(0, code);
    }

    private const string GuardMembers = @"
static int log;
static void A() { log = log * 10 + 1; }
static void B() { log = log * 10 + 2; }
static bool Take([Owned] System.Action a) { a(); Heap.Free(ref a); return false; }
static bool TakeTrue([Owned] System.Action a) { a(); Heap.Free(ref a); return true; }
static void S1(int k) { System.Action d = A; d += B; switch (k) { case 0 when Take(d): break; default: d(); Heap.Free(ref d); break; } }
static void S2(int k) { System.Action d = A; d += B; switch (k) { case 0 when Take(d): break; default: Heap.Free(ref d); break; } }
static void S3(int k) { System.Action d = A; d += B; switch (k) { case 0 when Take(d): break; case 0: d(); Heap.Free(ref d); break; case 1: Heap.Free(ref d); break; } }
static void S4(int k) { System.Action d = A; d += B; switch (k) { default: Heap.Free(ref d); break; case 0 when Take(d): break; } }
static void S5(int k) { System.Action d = A; d += B; switch (k) { case 0 when Take(d): case >= 0: d(); Heap.Free(ref d); break; default: Heap.Free(ref d); break; } }
static void S6(int k) { System.Action d = A; d += B; switch (k) { case 1: d(); Heap.Free(ref d); break; case 0 when TakeTrue(d): break; } }
static void S7(int k) { System.Action d = A; d += B; int r = k switch { 0 when Take(d) => 1, _ => 2 }; d(); Heap.Free(ref d); log = log * 10 + r; }
";

    /// <summary>
    /// Issue #173: a when guard runs before the later sections (and the default section, wherever it is), and its section's own body,
    /// so a hand-over there is not the variable's last use: the parameter takes a reference of its own and the variable keeps its one.
    /// A use in an earlier section's body (S6) still never runs with the guard, so that hand-over is for good.
    /// </summary>
    [Fact]
    public void A_hand_over_in_a_when_guard_is_not_the_last_use_of_a_later_section()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
log = 0; S1(0); Check(log == 1212, ""guard then default: the default section calls the list"");
log = 0; S1(1); Check(log == 12, ""default alone"");
log = 0; S2(0); Check(log == 12, ""guard then default: the default section frees the variable's own reference"");
log = 0; S3(0); Check(log == 1212, ""guard then a later case of the same value"");
log = 0; S3(1); Check(log == 0, ""a later case alone"");
log = 0; S4(0); Check(log == 12, ""a default section written before the guard runs after it"");
log = 0; S5(0); Check(log == 1212, ""guard, then the next label of the same section"");
log = 0; S5(1); Check(log == 12, ""the next label alone"");
log = 0; S6(1); Check(log == 12, ""an earlier section's body"");
log = 0; S6(0); Check(log == 12, ""a guard after it: handed over for good"");
log = 0; S7(0); Check(log == 12122, ""a switch expression's when clause"");
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(GuardMembers, body, OkLines("guard then default: the default section calls the list", "default alone",
            "guard then default: the default section frees the variable's own reference", "guard then a later case of the same value",
            "a later case alone", "a default section written before the guard runs after it", "guard, then the next label of the same section",
            "the next label alone", "an earlier section's body", "a guard after it: handed over for good", "a switch expression's when clause",
            "nothing leaks"));
    }

    /// <summary>
    /// The other positions that run only sometimes (a condition, &amp;&amp;, ||, ?:, ??): only the branches of an if, ?: or switch are
    /// exclusive, so a hand-over in a condition or an operand is not the last use of anything after it.
    /// </summary>
    [Fact]
    public void A_hand_over_in_a_condition_or_a_short_circuit_operand_is_not_the_last_use_of_what_follows()
    {
        const string members = GuardMembers + @"
public class Box { public int V; }
static void C1(int k) { System.Action d = A; d += B; if (k == 0 && Take(d)) log = 9; else { d(); Heap.Free(ref d); } }
static void C2(int k) { System.Action d = A; d += B; bool r = k == 0 ? Take(d) : k == 1 || Take(d); d(); Heap.Free(ref d); if (r) log = log * 10 + 9; }
static void C3(int k) { System.Action d = A; d += B; Box b = k == 0 ? null : new Box(); Box c = b ?? (Take(d) ? b : b); d(); Heap.Free(ref d); if (c != null) Heap.Free(ref c); }
static void C4(int k) { System.Action d = A; d += B; while (k < 2 && !Take(d)) k++; d(); Heap.Free(ref d); }
";
        const string body = @"
int base0 = Heap.LiveClosures();
log = 0; C1(0); Check(log == 1212, ""a hand-over in an if condition, then the else branch"");
log = 0; C1(1); Check(log == 12, ""the else branch alone"");
log = 0; C2(0); Check(log == 1212, ""a hand-over in a ?: branch, then after it"");
log = 0; C2(1); Check(log == 129, ""the || short-circuits"");
log = 0; C2(2); Check(log == 1212, ""the || runs its right operand"");
log = 0; C3(0); Check(log == 1212, ""a hand-over in a ?? operand"");
log = 0; C3(1); Check(log == 12, ""the ?? short-circuits"");
log = 0; C4(0); Check(log == 121212, ""a hand-over in a loop condition"");
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(members, body, OkLines("a hand-over in an if condition, then the else branch", "the else branch alone",
            "a hand-over in a ?: branch, then after it", "the || short-circuits", "the || runs its right operand", "a hand-over in a ?? operand",
            "the ?? short-circuits", "a hand-over in a loop condition", "nothing leaks"));
    }

    [Fact]
    public void A_hand_over_in_a_when_guard_takes_a_reference_of_its_own_and_an_earlier_section_does_not()
    {
        // S1 takes a new reference at the guard; S6 hands its list over (the earlier section never runs with the guard).
        var c = CompileOk(Program(GuardMembers + Check, ""));
        string Fn(string name)
        {
            var start = c.IndexOf("P_" + name + "(int32_t k)\n{", StringComparison.Ordinal);
            Assert.True(start >= 0, name);
            return c.Substring(start, c.IndexOf("\n}\n", start, StringComparison.Ordinal) - start);
        }
        Assert.Contains("es_closure_retain(_ho", Fn("S1"));
        Assert.Contains("es_closure_retain(_ho", Fn("S4"));
        Assert.DoesNotContain("es_closure_retain(_ho", Fn("S6"));
    }

    private const string SelfMembers = @"
static int log;
static System.Action Hook;
static System.Action Fd;
static System.Func<int> Ff;
static System.Action MakeC() { int n = 0; return () => { n++; log = log * 10 + n; Hook(); log = log * 10 + n; }; }
static System.Func<int> MakeF() { int n = 4; return () => { Hook(); return n; }; }
static System.Action MakeD(int k) { return () => { log = log * 10 + k; Hook(); log = log * 10 + k; }; }
static void L1() { System.Action d = MakeC(); Hook = () => d -= d; d(); Check(d == null && log == 11, ""a local removes itself inside its own call""); Heap.Free(ref Hook); }
static void L2() { Fd = MakeC(); Hook = () => Fd -= Fd; Fd(); Check(Fd == null && log == 11, ""a field removes itself inside its own call""); Heap.Free(ref Hook); }
static void L3() { Ff = MakeF(); Hook = () => Ff -= Ff; int r = Ff(); Check(Ff == null && r == 4, ""a Func removes itself and returns a captured value""); Heap.Free(ref Hook); }
static void L4() { System.Action d = MakeC(); Hook = () => d -= d; d?.Invoke(); Check(d == null && log == 11, ""?.Invoke""); Heap.Free(ref Hook); }
static void L5() { System.Action d = MakeC(); Hook = () => d -= d; d.Invoke(); Check(d == null && log == 11, "".Invoke""); Heap.Free(ref Hook); }
static void L6() { System.Action d = MakeD(1); System.Action e = MakeD(2); d += e; Heap.Free(ref e); Hook = () => d -= d; d(); Check(d == null && log == 1122, ""a multicast emptied by its first entry: the snapshot still runs the second""); Heap.Free(ref Hook); }
static void L7() { System.Action d = MakeD(1); int calls = 0; Hook = () => { calls++; if (calls == 1) { System.Action x = MakeD(2); d += x; Heap.Free(ref x); } }; d(); d(); Check(log == 111122 && calls == 3, ""an entry added inside the call waits for the next call""); Heap.Free(ref d); Heap.Free(ref Hook); }
static void L8() { System.Action d = MakeC(); Hook = () => Heap.Free(ref d); d(); Check(d == null && log == 11, ""a local freed inside its own call""); Heap.Free(ref Hook); }
static void L9() { Fd = MakeC(); Hook = () => { Fd -= Fd; System.Action x = MakeD(3); Fd += x; Heap.Free(ref x); }; Fd(); Check(log == 11, ""removed and replaced inside its own call""); Heap.Free(ref Hook); Hook = () => { }; Fd(); Check(log == 1133, ""the replacement runs on the next call""); Heap.Free(ref Fd); Heap.Free(ref Hook); }
";

    /// <summary>
    /// Issue #174: a delegate variable that -= or Heap.Free can empty holds its closure for the length of a call, so a -= (or a
    /// Heap.Free) of the running closure from inside the call, through another closure, frees it only when the call returns. As in
    /// dotnet the list being run is a snapshot: the running entry completes and the later entries still run.
    /// </summary>
    [Fact]
    public void A_delegate_that_removes_or_frees_its_running_closure_keeps_it_until_the_call_returns()
    {
        const string body = @"
int base0 = Heap.LiveClosures();
log = 0; L1();
log = 0; L2();
log = 0; L3();
log = 0; L4();
log = 0; L5();
log = 0; L6();
log = 0; L7();
log = 0; L8();
log = 0; L9();
Check(Heap.LiveClosures() == base0, ""nothing leaks"");
";
        RunBoth(SelfMembers, body,
            OkLines("a local removes itself inside its own call", "a field removes itself inside its own call",
                "a Func removes itself and returns a captured value", "?.Invoke", ".Invoke",
                "a multicast emptied by its first entry: the snapshot still runs the second", "an entry added inside the call waits for the next call",
                "a local freed inside its own call", "removed and replaced inside its own call", "the replacement runs on the next call", "nothing leaks"));
    }

    [Fact]
    public void Only_a_delegate_that_minus_equals_or_Heap_Free_can_empty_holds_its_closure_around_a_call()
    {
        var c = CompileOk(Program(@"
static int log;
static System.Action Make(int k) { return () => log += k; }
static void Held() { System.Action d = Make(1); d(); d -= d; }
static void Plain() { System.Action d = Make(2); d(); System.Action x = Make(3); d += x; Heap.Free(ref x); d(); }
" + Check, "Held(); Plain();"));
        string Fn(string name)
        {
            var start = c.IndexOf("P_" + name + "(void)\n{", StringComparison.Ordinal);
            Assert.True(start >= 0, name);
            return c.Substring(start, c.IndexOf("\n}\n", start, StringComparison.Ordinal) - start);
        }
        Assert.Contains("es_frame_hold(", Fn("Held"));
        Assert.DoesNotContain("es_frame_hold(", Fn("Plain"));
    }
}
