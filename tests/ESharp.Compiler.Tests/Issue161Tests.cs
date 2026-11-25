using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Issue #161: a delegate compared with a method group, static fields in generic structs, an interface converted to its base interface.</summary>
public class Issue161Tests
{
    private static void RunsAs(string members, string body, string expected)
    {
        var c = CompileOk("using System;\n" + Program(members, body));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void Delegate_compared_with_a_method_group()
    {
        const string members = @"
static void F() { }
static void G() { }
static int Two(int x) { return x * 2; }";
        const string body = @"
Action a = F;
Func<int, int> t = Two;
Action none = null;
int ok = 0;
if (a == F) ok++;
if (F == a) ok++;
if (a != G) ok++;
if (!(a == G)) ok++;
if (!(a != F)) ok++;
if (t == Two) ok++;
if (none != F) ok++;
Dos.PutStr(ok == 7 ? ""same\n"" : ""wrong "" + ok + ""\n"");";
        RunsAs(members, body, "same\n");
    }

    [Fact]
    public void Static_field_in_a_generic_struct_is_one_per_instance()
    {
        const string members = @"
struct Box<T> where T : unmanaged
{
    public static int Count;
    public static int Start = 5;
    public static int Late = Twice(21);
    public static int Ready;
    static int Twice(int x) { return x * 2; }
    static Box() { Ready = Late + 1; }
    public T Value;
    public static int Make() { Count++; return Count; }
    public int Bump() { Count += 10; return Count; }
}
struct Plain { public static int N = 9; public static int Hit() { N++; return N; } }";
        const string body = @"
Box<int>.Count = 3;
Box<int>.Make();
Box<long>.Make();
Box<long>.Make();
Box<int> bx = new Box<int>();
bx.Bump();
Plain.Hit();
int ok = 0;
if (Box<int>.Count == 14 && Box<long>.Count == 2) ok++;
if (Box<int>.Late == 42 && Box<long>.Ready == 43 && Box<int>.Ready == 43) ok++;
if (Plain.N == 10) ok++;
if (Box<int>.Start == 5 && Box<long>.Start == 5) ok++;
Dos.PutStr(ok == 4 ? ""statics ok\n"" : ""wrong "" + ok + ""\n"");";
        RunsAs(members, body, "statics ok\n");
    }

    [Fact]
    public void Interface_converted_to_its_base_interface()
    {
        const string members = @"
interface IA { int A(); }
interface IB : IA { int B(); }
interface IC : IB { int C(); }
class Impl : IC { public int A() { return 1; } public int B() { return 2; } public int C() { return 3; } }
static int UseA(IA a) { return a.A(); }
static IB PickB(IC c) { return c; }
static IA AsA(IB b) { return b; }
class Other : IB { public int A() { return 10; } public int B() { return 20; } }";
        const string body = @"
IC c = new Impl();
IB b = c;
IA a = b;
IA a2 = c;
IA none = null;
IB nb = null;
IA fromNull = nb;
IA viaCast = (IA)b;
IA o = AsA(new Other());
int ok = 0;
if (PickB(c).A() == 1 && viaCast.A() == 1 && ((IA)c).A() == 1 && o.A() == 10 && !(o is Impl)) ok++;
if (a.A() == 1 && b.B() == 2 && b.A() == 1) ok++;
if (a2.A() == 1 && UseA(b) == 1 && UseA(c) == 1) ok++;
if (a == a2 && a == b && none == fromNull) ok++;
if (a is Impl && !(none is Impl) && b is Impl) ok++;
Dos.PutStr(ok == 5 ? ""base ok\n"" : ""wrong "" + ok + ""\n"");";
        RunsAs(members, body, "base ok\n");
    }
}
