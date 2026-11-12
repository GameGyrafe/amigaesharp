using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Issue #12 follow-ups: constant string compares, ?? messages, Equals calls, member name collisions, SDL diagnostics.</summary>
public class EmitterFollowupTests
{
    private static string Messages(CompileResult r, string id) =>
        string.Join("\n", r.Diagnostics.Where(d => d.Id == id).Select(d => d.GetMessage()));

    [Fact]
    public void Constant_string_compare_in_a_static_initializer_is_folded()
    {
        const string members = @"
const string K = ""x"";
static bool A = K == ""x"";
static bool B = ""a"" != ""a"";
static bool C = K != ""y"";";
        const string body = @"Dos.PutStr(A && !B && C ? ""folded\n"" : ""wrong\n"");";
        var c = CompileOk(Program(members, body));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("folded\n", output);
    }

    [Fact]
    public void Coalesce_message_names_the_real_case()
    {
        // Most other left types are already refused by the analyzer or C#; a nullable value type is the case that reaches the emitter.
        var nullable = Compile(Program("static int F(int? a) { return a ?? 3; }"));
        Assert.Contains("nullable value type", Messages(nullable, "ES0099"));
    }

    [Fact]
    public void Equals_on_classes_without_an_override_is_reference_equality()
    {
        const string members = @"
class Thing { public int N; }
class Other : Thing { }";
        const string body = @"
var a = new Thing(); var b = new Thing(); Thing c = a; Thing none = null; Thing o = new Other();
int ok = 0;
if (a.Equals(c)) ok++;
if (!a.Equals(b)) ok++;
if (object.Equals(a, c)) ok++;
if (!object.Equals(a, b)) ok++;
if (object.ReferenceEquals(a, c) && !object.ReferenceEquals(a, o)) ok++;
if (!a.Equals(none) && object.Equals(none, null)) ok++;
Dos.PutStr(ok == 6 ? ""equals ok\n"" : ""wrong\n"");";
        var c = CompileOk(Program(members, body));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("equals ok\n", output);
    }

    [Fact]
    public void Unqualified_Equals_in_an_instance_method_and_object_Equals_on_strings()
    {
        const string members = @"
class T { public bool Same(T o) { return Equals(o); } public bool Self() { return Equals(this); } }";
        const string body = @"
var t = new T(); var u = new T();
string s1 = ""ab""; string h = ""a""; string s2 = h + ""b""; string n = null;
int ok = 0;
if (t.Same(t) && !t.Same(u) && t.Self()) ok++;
if (object.Equals(s1, s2)) ok++;
if (!object.Equals(s1, ""x"") && object.Equals(n, n)) ok++;
Dos.PutStr(ok == 3 ? ""eq ok\n"" : ""wrong\n"");";
        var c = CompileOk(Program(members, body));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("eq ok\n", output);
    }

    [Fact]
    public void Equals_that_cannot_be_mapped_is_ES0099_not_undeclared_C()
    {
        var structEquals = Compile(Program("struct S { public int N; }", "var s = new S(); var t = new S(); bool b = s.Equals(t);"));
        Assert.Contains("ES0099", ErrorIds(structEquals));
        Assert.Contains("ValueType.Equals", Messages(structEquals, "ES0099"));

        var overridden = Compile(Program(
            "class Thing { public int N; public override bool Equals(object o) { return o is Thing t && t.N == N; } public override int GetHashCode() { return N; } }",
            "var a = new Thing(); var b = new Thing(); bool x = a.Equals(b);"));
        Assert.Contains("ES0099", ErrorIds(overridden));

        var boxed = Compile(Program("", "int i = 1; bool x = object.Equals(i, i);"));
        Assert.Contains("ES0099", ErrorIds(boxed));
    }

    [Fact]
    public void Member_name_collisions_are_reported()
    {
        // class A_B member C and class A member B_C both become Es_P_A_B_C (here as static fields)
        var src = Prelude + @"
public static class A_B { public static int C = 1; }
public static class A { public static int B_C = 2; }
public static class P { [EntryPoint] public static void Main() { Dos.PutStr(A_B.C == A.B_C ? ""same\n"" : ""apart\n""); } }";
        var r = Compile(src);
        Assert.False(r.Success);
        Assert.Contains("ES0099", ErrorIds(r));
        Assert.Contains("already used by", Messages(r, "ES0099"));

        var methods = Prelude + @"
public static class A_B { public static int C() { return 1; } }
public static class A { public static int B_C() { return 2; } }
public static class P { [EntryPoint] public static void Main() { A_B.C(); A.B_C(); } }";
        Assert.Contains("ES0099", ErrorIds(Compile(methods)));

        // no collision: ordinary distinct members compile
        Assert.True(Compile(Prelude + "public static class A { public static int B = 1; public static int C() { return B; } }\n" +
            "public static class P { [EntryPoint] public static void Main() { A.C(); } }").Success);
    }

    [Fact]
    public void Amiga_only_member_of_a_class_that_sdl_has_explains_CS0117()
    {
        var r = Compile(Program("", "var w = Video.Window; var d = Dos.DateStamp(null); int x = Video.NoSuchThing;"), Targets.Sdl.ToOptions());
        Assert.False(r.Success);
        var messages = Messages(r, "ES0102");
        Assert.Contains("'Video.Window'", messages);
        Assert.Contains("'Dos.DateStamp'", messages);
        Assert.DoesNotContain("NoSuchThing", messages);
    }

    [Fact]
    public void Slow_long_warning_is_for_the_68020_not_sdl()
    {
        var src = Program("static long F(long a) { return a * 2; }");
        Assert.Contains(Compile(src, Targets.M68k.ToOptions()).Diagnostics, d => d.Id == "ES0100");
        var sdl = Compile(src, Targets.Sdl.ToOptions());
        Assert.True(sdl.Success);
        Assert.DoesNotContain(sdl.Diagnostics, d => d.Id == "ES0100");
    }

    [Fact]
    public void Sdl_blit_checks_the_data_length_and_the_key_map_has_the_extra_keys()
    {
        var c = CompileOk(Program("", "byte[] img = new byte[4]; if (Video.Open(32, 32, 2)) { Video.Blit(img, 32, 32, 2, 0, 0); Video.Close(); }"), Targets.Sdl.ToOptions());
        Assert.Contains("planarLen", c);
        Assert.Contains("blit data too short", c);
        Assert.Contains("SDL_SCANCODE_KP_0", c);
        Assert.Contains("SDL_SCANCODE_F12", c);
        Assert.Contains("SDL_SCANCODE_HELP", c);
    }
}
