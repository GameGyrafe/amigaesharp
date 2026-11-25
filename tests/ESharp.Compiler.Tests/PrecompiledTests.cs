using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Precompiled package binaries (issue #154): the C of a package's library build compiled with the host gcc into an ar archive, then the
/// program's C (program mode: the package's code declared, not defined) linked with it and run. The output must be the same as the
/// build that compiles the package from its sources, generic copies over the program's own types must work, and the copies the
/// binary already holds must not be defined again.
/// </summary>
public class PrecompiledTests
{
    private const string Stamp = "0.1.0-test+stamp";

    internal const string LibSource = @"using System;
using ESharp;
using ESharp.Platform;

namespace Lib;

public struct Pair<T> where T : unmanaged
{
    public T A;
    public T B;
    public Pair(T a, T b) { A = a; B = b; }
    public Pair<T> Swapped() { return new Pair<T>(B, A); }
}

[Extensible]
public class Shape
{
    public static int Made;
    public static int Seed = Start();
    private static int Start() => 40;
    public int Size;
    public int Tag = 7;
    public Shape(int size) { Size = size; Made++; }
    public virtual int Area() => Size * Size;
    public virtual string Name() => ""shape"";
    public int Twice() => Area() * 2;
}

public class Square : Shape
{
    public Square(int s) : base(s) { }
    public override string Name() => ""square"";
}

public static class Tools
{
    public static int Calls;
    static Tools() { Calls = 100; }
    public static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;
    public static int Twice(int x) => Pick(true, x * 2, 0);
    public static Pair<int> Ints() => new Pair<int>(1, 2).Swapped();
    public static string Describe(Shape s) { Calls++; return $""{s.Name()} {s.Area()}""; }
    public static int Apply(int x) { Func<int, int> f = v => v + 1; return f(x); }
    public static bool IsSquare(Shape s) => s is Square;
}
";

    internal const string GameSource = @"using ESharp;
using ESharp.Platform;
using Lib;

public struct Mine { public int V; }

public sealed class Circle : Shape
{
    public Circle() : base(3) { }
    public override int Area() => 28;
    public override string Name() => ""circle"";
}

public static class P
{
    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr(Tools.Describe(new Square(4)) + ""\n"");
        Dos.PutStr(Tools.Describe(new Circle()) + ""\n"");
        Shape c = new Circle();
        Dos.PutStr($""made {Shape.Made} seed {Shape.Seed} calls {Tools.Calls} tag {c.Tag} twice {c.Twice()}\n"");
        Dos.PutStr($""twice {Tools.Twice(21)} pick {Tools.Pick(true, 5, 6)} ints {Tools.Ints().A}\n"");
        var m = Tools.Pick(false, new Mine { V = 1 }, new Mine { V = 7 });
        var pm = new Pair<Mine>(new Mine { V = 2 }, new Mine { V = 3 }).Swapped();
        Dos.PutStr($""mine {m.V} pair {pm.A.V}{pm.B.V} apply {Tools.Apply(1)}\n"");
        Dos.PutStr($""square {(Tools.IsSquare(new Square(1)) ? 1 : 0)} circle {(Tools.IsSquare(c) ? 1 : 0)} {(c is Circle ? 1 : 0)}\n"");
    }
}
";

    internal const string Expected =
        "square 16\ncircle 28\nmade 3 seed 40 calls 102 tag 7 twice 56\ntwice 42 pick 5 ints 2\nmine 7 pair 32 apply 2\nsquare 1 circle 0 1\n";

    internal static EsPackage Lib() => PackageTests.Package("Sample.Lib", new Dictionary<string, string> { ["Lib.cs"] = LibSource });

    private static CompileOptions Options(EsPackage lib, string? stamp = Stamp, bool library = false) => Targets.M68k.ToOptions(packages: new[] { lib }, sdkVersion: "0.1.5") is var o
        ? new CompileOptions
        {
            Defines = o.Defines, TargetName = o.TargetName, Packages = o.Packages, SdkVersion = o.SdkVersion, Stamp = stamp,
            LibraryBuild = library ? lib : null, CFileName = library ? "library.c" : null,
        }
        : o;

    /// <summary>The library build of a package and the binary's manifest (as esharp precompile writes it), its C compiled into an archive.</summary>
    internal static (PackageBinary Binary, string Dir) BuildBinary(EsPackage lib, string stamp = Stamp)
    {
        var r = EsCompiler.Compile(System.Array.Empty<SourceFile>(), Options(lib, library: true));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        Assert.NotNull(r.Library);
        var dir = Path.Combine(Path.GetTempPath(), "esharp-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "proto"));
        WriteShims(dir);
        File.WriteAllText(Path.Combine(dir, "library.c"), r.C);
        var objects = new List<string> { "library.o" };
        Run(dir, Cc, "-std=gnu11", "-fwrapv", "-Wall", "-Werror", PackageNatives.CFlags, "-I.", "-c", "-o", "library.o", "library.c");
        foreach (var n in r.Native)
        {
            var path = Path.Combine(dir, n.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, n.Text);
        }
        foreach (var n in r.Native.Where(n => n.Compile))
        {
            var obj = "n_" + Path.GetFileNameWithoutExtension(n.Path) + ".o";
            Run(dir, Cc, "-std=gnu11", "-fwrapv", "-Wall", "-Werror", PackageNatives.CFlags, "-c", "-o", obj, n.Path);
            objects.Add(obj);
        }
        Run(dir, "ar", new[] { "rcsD", PackageBinaries.ArchiveName }.Concat(objects).ToArray());
        var files = new[] { new BinaryFile(PackageBinaries.ArchiveName, PackageBinaries.Sha256File(Path.Combine(dir, PackageBinaries.ArchiveName))) };
        var binary = PackageBinaries.FromBuild(lib, "m68k", stamp, "release", r.Library!, objects, files) with { Folder = dir };
        return (binary, dir);
    }

    private static void WriteShims(string dir)
    {
        // The same small dos.library shim the other host gcc tests use (TestHelpers): PutStr on stdio.
        File.WriteAllText(Path.Combine(dir, "proto", "dos.h"),
            "#pragma once\n#include <stdio.h>\nstatic inline long PutStr(const char *s) { return fputs(s, stdout) < 0; }\n" +
            "static inline void *ErrorOutput(void) { return stderr; }\nstatic inline void *Output(void) { return stdout; }\n" +
            "static inline long FPuts(void *f, const void *s) { return fputs((const char *)s, (FILE *)f) < 0; }\n" +
            "static inline long Flush(void *f) { return fflush((FILE *)f) == 0; }\n");
        File.WriteAllText(Path.Combine(dir, "proto", "exec.h"),
            "#pragma once\n#include <stdlib.h>\n#include <stdint.h>\n#include <stddef.h>\n#define MEMF_CHIP 2UL\n" +
            "static inline void *AllocVec(uint32_t size, uint32_t req) { (void)req; return calloc(1, size); }\n" +
            "static inline void FreeVec(void *p) { free(p); }\n");
    }

    internal static string Run(string dir, string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"{exe} {string.Join(" ", args)} failed:\n{output}{err.Result}");
        return (output + err.Result).Replace("\r\n", "\n");
    }

    /// <summary>Links the program's C with the binary's archive and runs it.</summary>
    internal static string LinkAndRun(string c, PackageBinary binary, string dir)
    {
        File.WriteAllText(Path.Combine(dir, "prog.c"), c);
        Run(dir, Cc, "-std=gnu11", "-fwrapv", "-Wall", "-Werror", PackageNatives.CFlags, "-I.", "-o", "prog", "prog.c", binary.Archive!);
        return Run(dir, Path.Combine(dir, "prog"));
    }

    private static bool CanBuild => HasGcc && !OperatingSystem.IsWindows();

    [Fact]
    public void Library_build_defines_the_package_and_declares_the_rest()
    {
        var r = EsCompiler.Compile(System.Array.Empty<SourceFile>(), Options(Lib(), library: true));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        var c = r.C!;
        Assert.DoesNotContain("int main(", c);
        Assert.Contains("\nvoid es_pkginit_sample_lib(void)\n", c);
        // The package's functions, statics and class tables have external linkage; the platform's are declared only.
        Assert.Contains("\nint32_t Es_Lib_Tools_Twice(int32_t x)\n", c);
        Assert.Contains("\nint32_t Es_Lib_Shape_Made;", c);
        Assert.Contains("\nconst struct Es_Lib_Shape_cvt Es_Lib_Shape_vt = {", c);
        Assert.Contains("\nextern const struct Es_Lib_Shape_cvt Es_Lib_Shape_vt;", c);
        Assert.DoesNotContain("Es_ESharp_Platform_Dos_PutStr(const char *s)\n{", c);
        // The runtime's state is the program's.
        Assert.Contains("\nextern char es_fmt_buf[8][128];", c);
        Assert.Contains("\nextern void (*es_exit_hook)(void);", c);
        // The generic copies the package makes for itself are listed.
        Assert.Contains("Es_Lib_Tools_Pick__int", r.Library!.Generics);
        Assert.Contains("Es_Lib_Pair__int", r.Library.Generics);
        Assert.Contains("fmt", r.Library.Runtime);
        Assert.Contains("alloc", r.Library.Runtime);
    }

    [Fact]
    public void Program_links_the_binary_and_runs_as_the_source_build_does()
    {
        var lib = Lib();
        var source = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) }, Options(lib, stamp: null));
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToString())));
        Assert.Empty(source.Linked);
        if (!CanBuild) return;
        var (binary, dir) = BuildBinary(lib);
        try
        {
            var (code, fromSource) = RunWithHostGcc(source.C!);
            Assert.Equal(0, code);
            Assert.Equal(Expected, fromSource);

            var linked = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) }, Options(lib.WithBinaries(new[] { binary })));
            Assert.True(linked.Success, string.Join("\n", linked.Diagnostics.Select(d => d.ToString())));
            Assert.Single(linked.Linked);
            var c = linked.C!;
            // The package's code is declared, not compiled again: no body of its functions, no definition of its statics or tables.
            Assert.Contains("\nint32_t Es_Lib_Tools_Twice(int32_t x);", c);
            Assert.DoesNotContain("\nint32_t Es_Lib_Tools_Twice(int32_t x)\n{", c);
            Assert.Contains("\nextern int32_t Es_Lib_Shape_Made;", c);
            Assert.DoesNotContain("\nconst struct Es_Lib_Shape_cvt Es_Lib_Shape_vt = {", c);
            Assert.Contains("\n    es_pkginit_sample_lib();\n", c);
            // Pick<int> is in the binary: referenced, not defined; Pick<Mine> and Pair<Mine> are over the program's type: defined here.
            Assert.DoesNotContain("\nint32_t Es_Lib_Tools_Pick__int(bool first, int32_t a, int32_t b)\n{", c);
            Assert.Contains("\nEs_Mine Es_Lib_Tools_Pick__Mine(bool first, Es_Mine a, Es_Mine b)\n{", c);
            Assert.Contains("Es_Lib_Pair__Mine Es_Lib_Pair__Mine_Swapped(Es_Lib_Pair__Mine *_this)\n{", c);
            Assert.Equal(Expected, LinkAndRun(c, binary, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string IdentityLibSource = @"using System;
using ESharp;
using ESharp.Platform;

namespace Lib;

public interface IThing { int Id(); }

public class LibThing : IThing { public int Id() => 1; }

public struct Box<T> where T : unmanaged { public T V; public Box(T v) { V = v; } }

public static class Bus
{
    public static int Count;
    public static event Action Ticked;
    public static event Action Done;
    public static void OnTick() { Count++; }
    public static void OnOther() { Count += 10; }
    public static void Subscribe() { Ticked += OnTick; }
    public static void Unsubscribe() { Ticked -= OnTick; }
    public static void Fire() { Ticked?.Invoke(); }
    public static void HookFlush() { Done += Dos.FlushOutput; }
    public static bool IsLib(IThing t) => t is LibThing;
    public static int Kind(IThing t) => t switch { LibThing => 1, _ => 0 };
    public static int Boxed() => new Box<int>(5).V;
}
";

    private const string IdentityGameSource = @"using ESharp;
using ESharp.Platform;
using Lib;

namespace Game;

public struct Box<T> where T : unmanaged { public T V; public Box(T v) { V = v; } }

public class GameThing : IThing { public int Id() => 2; }

public static class P
{
    static int B(bool b) => b ? 1 : 0;

    [EntryPoint]
    public static void Main()
    {
        IThing a = new LibThing();
        IThing g = new GameThing();
        Dos.PutStr($""is {B(a is LibThing)} {B(g is GameThing)} {B(a is GameThing)} lib {B(Bus.IsLib(a))} {B(Bus.IsLib(g))}\n"");
        var k = g switch { GameThing => 2, LibThing => 1, _ => 0 };
        Dos.PutStr($""kind {Bus.Kind(a)} {Bus.Kind(g)} {k}\n"");
        Bus.Subscribe();
        Bus.Ticked -= Bus.OnTick;
        Bus.Fire();
        Bus.Ticked += Bus.OnTick;
        Bus.Ticked += Bus.OnOther;
        Bus.Unsubscribe();
        Bus.Fire();
        Bus.Ticked -= Bus.OnOther;
        Bus.Fire();
        Bus.HookFlush();
        Bus.Done -= Dos.FlushOutput;
        Dos.PutStr($""count {Bus.Count} box {Bus.Boxed()} {new Box<int>(6).V}\n"");
    }
}
";

    [Fact]
    public void Interface_tests_and_delegate_identity_hold_across_the_binary()
    {
        // An interface value's 'is' and switch compare its table's address, and an event's -= compares the wrapper of a method group:
        // the program and the binary must use one table and one wrapper each (defined by their owner), as a whole-program build does.
        var lib = PackageTests.Package("Sample.Ident", new Dictionary<string, string> { ["Lib.cs"] = IdentityLibSource });
        const string expected = "is 1 1 0 lib 1 0\nkind 1 0 2\ncount 10 box 5 6\n";
        var source = EsCompiler.Compile(new[] { new SourceFile("game.cs", IdentityGameSource) }, Options(lib, stamp: null));
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToString())));
        var built = EsCompiler.Compile(System.Array.Empty<SourceFile>(), Options(lib, library: true));
        Assert.True(built.Success, string.Join("\n", built.Diagnostics.Select(d => d.ToString())));
        Assert.Contains("\nconst struct Es_Lib_IThing_vtbl Es_Lib_IThing_vt_Lib_LibThing = {", built.C!);
        Assert.Contains("Es_Lib_Bus_OnTick__dg", built.Library!.Delegates);
        Assert.Contains("Es_ESharp_Platform_Dos_FlushOutput__dg", built.Library.DelegateUses);
        Assert.Contains("Es_Lib_Box__int", built.Library.Generics);
        if (!CanBuild) return;
        var (binary, dir) = BuildBinary(lib);
        try
        {
            var (code, fromSource) = RunWithHostGcc(source.C!);
            Assert.Equal(0, code);
            Assert.Equal(expected, fromSource);
            var linked = EsCompiler.Compile(new[] { new SourceFile("game.cs", IdentityGameSource) }, Options(lib.WithBinaries(new[] { binary })));
            Assert.True(linked.Success, string.Join("\n", linked.Diagnostics.Select(d => d.ToString())));
            Assert.Single(linked.Linked);
            var c = linked.C!;
            Assert.Contains("\nextern const struct Es_Lib_IThing_vtbl Es_Lib_IThing_vt_Lib_LibThing;", c);
            Assert.Contains("\nconst struct Es_Lib_IThing_vtbl Es_Lib_IThing_vt_Game_GameThing = {", c);
            Assert.Contains("\nvoid Es_Lib_Bus_OnTick__dg(void *_env);", c);                   // the binary's
            Assert.Contains("\nvoid Es_Lib_Bus_OnOther__dg(void *_env) {", c);                 // the binary's code never uses it as a delegate
            Assert.Contains("\nvoid Es_ESharp_Platform_Dos_FlushOutput__dg(void *_env) {", c); // the platform's: the program defines it
            // Same-named generics in two namespaces: Lib.Box<int> is the binary's copy, Game.Box<int> the program's own.
            Assert.Contains("Es_Lib_Box__int", binary.Generics);
            Assert.DoesNotContain("Es_Game_Box__int", binary.Generics);
            Assert.Equal(expected, LinkAndRun(c, binary, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string DerivedLibSource = @"using ESharp;
using ESharp.Platform;

namespace Lib;

public interface IThing { int Id(); }

[Extensible]
public class Base : IThing { public virtual int Id() => 1; }

public class Sub : Base { public override int Id() => 3; }

[Extensible]
public class Plain : IThing { public int V = 7; public int Id() => V; }

public static class Probe
{
    public static IThing Keep;
    public static int IsBase(IThing t) => t is Base ? 1 : 0;
    public static int IsSub(IThing t) => t is Sub ? 1 : 0;
    public static int Kind(IThing t) => t switch { Sub => 3, Base => 1, _ => 0 };
    public static int Bound(IThing t) => t switch { Base b => b.Id() * 10, _ => -1 };
    public static int AsId(IThing t) { var b = t as Base; return b == null ? -1 : b.Id(); }
    public static void Store(IThing t) { Keep = t; }
    public static int KeptIsBase() => Keep is Base ? 1 : 0;
    public static int ClassIsSub(Base b) => b is Sub ? 1 : 0;
    public static int IsPlain(IThing t) => t is Plain ? 1 : 0;
    public static int Read(Plain p) => p.V;
}
";

    private const string DerivedGameSource = @"using ESharp;
using ESharp.Platform;
using Lib;

namespace Game;

public class Derived : Base { public override int Id() => 2; }
public class Deeper : Derived { public override int Id() => 4; }
public class Other : IThing { public int Id() => 5; }
public class OtherPoly : IThing { public virtual int Id() => 6; }
public class Kid : Plain { public int W = 3; }

public static class P
{
    static string Row(IThing t) =>
        $""{Probe.IsBase(t)} {Probe.IsSub(t)} {Probe.Kind(t)} {Probe.Bound(t)} {Probe.AsId(t)} {(t is Base ? 1 : 0)}\n"";

    [EntryPoint]
    public static void Main()
    {
        IThing d = new Derived();
        Base asBase = new Deeper();
        IThing viaBase = asBase;
        Dos.PutStr(Row(d));
        Dos.PutStr(Row(new Deeper()));
        Dos.PutStr(Row(viaBase));
        Dos.PutStr(Row(new Sub()));
        Dos.PutStr(Row(new Base()));
        Dos.PutStr(Row(new Other()));
        Dos.PutStr(Row(new OtherPoly()));
        IThing none = null;
        Dos.PutStr(Row(none));
        Probe.Store(d);
        Dos.PutStr($""kept {Probe.KeptIsBase()} class {Probe.ClassIsSub(new Derived())} {Probe.ClassIsSub(new Sub())} {d.Id()}\n"");
        var kid = new Kid();
        IThing tk = kid;
        Dos.PutStr($""plain {Probe.Read(kid)} {Probe.IsPlain(tk)} {Probe.IsPlain(d)} {kid.W} {tk.Id()}\n"");
    }
}
";

    [Fact]
    public void Interface_tests_in_the_binary_see_program_classes_derived_from_an_extensible_class()
    {
        // The binary knows only its own interface tables; a program class derived from an [Extensible] package class has a table of its
        // own, so is, as and switch on an interface value fall back to the object's own type id (re-review of #160). An [Extensible] class
        // without virtual members (Plain) has a class table in a split build, so a program subclass has the layout the binary expects.
        var lib = PackageTests.Package("Sample.Derive", new Dictionary<string, string> { ["Lib.cs"] = DerivedLibSource });
        const string expected =
            "1 0 1 20 2 1\n1 0 1 40 4 1\n1 0 1 40 4 1\n1 1 3 30 3 1\n1 0 1 10 1 1\n0 0 0 -1 -1 0\n0 0 0 -1 -1 0\n0 0 0 -1 -1 0\nkept 1 class 0 1 2\nplain 7 1 0 3 7\n";
        var source = EsCompiler.Compile(new[] { new SourceFile("game.cs", DerivedGameSource) }, Options(lib, stamp: null));
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToString())));
        if (!CanBuild) return;
        var (binary, dir) = BuildBinary(lib);
        try
        {
            var (code, fromSource) = RunWithHostGcc(source.C!);
            Assert.Equal(0, code);
            Assert.Equal(expected, fromSource);
            var linked = EsCompiler.Compile(new[] { new SourceFile("game.cs", DerivedGameSource) }, Options(lib.WithBinaries(new[] { binary })));
            Assert.True(linked.Success, string.Join("\n", linked.Diagnostics.Select(d => d.ToString())));
            Assert.Single(linked.Linked);
            Assert.Contains(".es_poly = 1", linked.C!);
            Assert.Equal(expected, LinkAndRun(linked.C!, binary, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Issue161LibSource = @"using ESharp;
using ESharp.Platform;

namespace Lib;

public struct Counter<T> where T : unmanaged
{
    public static int N;
    public static int Start = 10;
    public T V;
    public static int Bump() { N++; return N; }
}

public interface IA { int A(); }
public interface IB : IA { int B(); }

public class PkgImpl : IB { public int A() => 1; public int B() => 2; }

public static class Up
{
    public static IA Down(IB b) => b;
    public static int CallA(IB b) => b.A();
    public static IB Make() => new PkgImpl();
    public static int BumpInt() => Counter<int>.Bump();
    public static int SeenInt() => Counter<int>.N;
    public static int StartInt() => Counter<int>.Start;
}
";

    private const string Issue161GameSource = @"using ESharp;
using ESharp.Platform;
using Lib;

public struct M { public int V; }
public interface IC : IB { int C(); }
public class Mine : IB { public int A() => 100; public int B() => 200; }
public class Mine3 : IC { public int A() => 7; public int B() => 8; public int C() => 9; }

public static class P
{
    [EntryPoint]
    public static void Main()
    {
        Up.BumpInt();
        Counter<int>.Bump();
        Counter<M>.Bump();
        Counter<M>.Bump();
        Counter<M>.Bump();
        Dos.PutStr($""n {Counter<int>.N} {Up.SeenInt()} {Counter<M>.N} start {Up.StartInt()} {Counter<M>.Start}\n"");
        IA a = Up.Down(new Mine());
        IA b = Up.Down(Up.Make());
        IC c = new Mine3();
        IA d = Up.Down(c);
        IB cb = c;
        IA e = cb;
        Dos.PutStr($""down {a.A()} {b.A()} {d.A()} {e.A()} call {Up.CallA(new Mine())} {Up.CallA(Up.Make())} {Up.CallA(c)}\n"");
        Dos.PutStr($""is {(a is Mine ? 1 : 0)} {(b is PkgImpl ? 1 : 0)} {(d is Mine3 ? 1 : 0)} {(d is Mine ? 1 : 0)} {(e is Mine3 ? 1 : 0)}\n"");
    }
}
";

    [Fact]
    public void Static_fields_of_generic_structs_and_base_interface_conversions_in_a_split_build()
    {
        // Issue #161: Counter<int> has one owner (the binary, which makes the copy itself), Counter<M> is the program's; a base-interface
        // conversion reads the table the class's owner filled in, so the binary converts the program's classes and the program the binary's.
        var lib = PackageTests.Package("Sample.Up", new Dictionary<string, string> { ["Lib.cs"] = Issue161LibSource });
        const string expected = "n 2 2 3 start 10 10\ndown 100 1 7 7 call 100 1 7\nis 1 1 1 0 1\n";
        var source = EsCompiler.Compile(new[] { new SourceFile("game.cs", Issue161GameSource) }, Options(lib, stamp: null));
        Assert.True(source.Success, string.Join("\n", source.Diagnostics.Select(d => d.ToString())));
        if (!CanBuild) return;
        var (binary, dir) = BuildBinary(lib);
        try
        {
            var (code, fromSource) = RunWithHostGcc(source.C!);
            Assert.Equal(0, code);
            Assert.Equal(expected, fromSource);
            var linked = EsCompiler.Compile(new[] { new SourceFile("game.cs", Issue161GameSource) }, Options(lib.WithBinaries(new[] { binary })));
            Assert.True(linked.Success, string.Join("\n", linked.Diagnostics.Select(d => d.ToString())));
            Assert.Single(linked.Linked);
            Assert.Equal(expected, LinkAndRun(linked.C!, binary, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_package_with_esharp_dependencies_is_not_precompiled()
    {
        var lib = Lib();
        var other = PackageTests.Package("Sample.Other", new Dictionary<string, string> { ["Other.cs"] = "namespace Other; public static class O { public static int One() => 1; }" });
        var r = EsCompiler.Compile(System.Array.Empty<SourceFile>(), Options(lib, library: true) is var o
            ? new CompileOptions { Defines = o.Defines, TargetName = o.TargetName, Packages = new[] { lib, other }, Stamp = Stamp, LibraryBuild = lib, CFileName = "library.c" }
            : null);
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0125" && d.GetMessage().Contains("Sample.Other"));
    }

    [Fact]
    public void A_binary_from_another_esharp_or_config_falls_back_to_the_sources_with_ES0124()
    {
        var lib = Lib();
        var other = new PackageBinary("Sample.Lib", "1.0.0", "m68k", "m68k", "0.0.9+other", "release", "es_pkginit_sample_lib",
            new List<string>(), new List<string>(), new List<PackageBase>(), new List<string> { "library.o" },
            new List<BinaryFile> { new(PackageBinaries.ArchiveName, new string('0', 64)) }, PackageBinaries.SourcesHash(lib));
        var r = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) }, Options(lib.WithBinaries(new[] { other })));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
        Assert.Empty(r.Linked);
        var warning = Assert.Single(r.Diagnostics, d => d.Id == "ES0124");
        Assert.Contains("its m68k binary was built by E# 0.0.9+other, and this is E# " + Stamp, warning.GetMessage());
        Assert.Contains("\nint32_t Es_Lib_Tools_Twice(int32_t x)\n{", r.C!);   // compiled from source

        var debug = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) },
            Options(lib.WithBinaries(new[] { other with { Stamp = Stamp } })) is var o ? new CompileOptions { Defines = o.Defines.Append("DEBUG").ToArray(), TargetName = o.TargetName, Packages = o.Packages, Stamp = Stamp } : null);
        Assert.Contains(debug.Diagnostics, d => d.Id == "ES0124" && d.GetMessage().Contains("is a release build, and this is a debug build"));
        var sdl = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) }, new CompileOptions
            { Defines = Targets.Sdl.Defines, TargetName = "sdl", Packages = new[] { lib.WithBinaries(new[] { other with { Stamp = Stamp } }) }, Stamp = Stamp });
        Assert.Contains(sdl.Diagnostics, d => d.Id == "ES0124" && d.GetMessage().Contains("none for sdl-linux-x64 (it has m68k)"));
    }

    /// <summary>A GNU ar archive with these members (the bytes do not matter for the checks).</summary>
    private static byte[] Ar(params string[] members)
    {
        var data = new List<byte>(Encoding.ASCII.GetBytes("!<arch>\n"));
        foreach (var m in members)
        {
            data.AddRange(Encoding.ASCII.GetBytes((m + "/").PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "644".PadRight(8) + "2".PadRight(10) + "`\n"));
            data.AddRange(new byte[] { 1, 2 });
        }
        return data.ToArray();
    }

    [Fact]
    public void Binaries_beside_the_dll_are_checked_and_hand_made_objects_refused()
    {
        var lib = Lib();
        var dir = Path.Combine(Path.GetTempPath(), "esharp-binfolder-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(dir, "lib", "net10.0", PackageBinaries.FolderName, "m68k");
        Directory.CreateDirectory(bin);
        try
        {
            var archive = Ar("library.o");
            File.WriteAllBytes(Path.Combine(bin, PackageBinaries.ArchiveName), archive);
            var good = new PackageBinary("Sample.Lib", "1.0.0", "m68k", "m68k", Stamp, "release", "es_pkginit_sample_lib", new List<string>(),
                new List<string>(), new List<PackageBase>(), new List<string> { "library.o" },
                new List<BinaryFile> { new(PackageBinaries.ArchiveName, PackageBinaries.Sha256(archive)) }, PackageBinaries.SourcesHash(lib));
            void Manifest(PackageBinary b) => File.WriteAllText(Path.Combine(bin, PackageBinaries.ManifestName), PackageBinaries.Write(b));
            List<string> Problems(out List<PackageBinary> found)
            {
                var problems = new List<string>();
                found = PackageBinaries.ReadFolder(Path.Combine(dir, "lib", "net10.0"), lib, problems);
                problems.AddRange(PackageBinaries.StrayPrebuilt(dir, Path.Combine(dir, "lib", "net10.0"), found));
                return problems;
            }
            Manifest(good);
            Assert.Empty(Problems(out var binaries));
            Assert.Equal(Stamp, Assert.Single(binaries).Stamp);
            Assert.Equal(PackageBinaries.Write(good), PackageBinaries.Write(binaries[0]));   // read back as written
            Assert.Equal(bin, binaries[0].Folder);

            // Each of these makes the binary unusable (ES0118 in the build).
            foreach (var (bad, says) in new (PackageBinary, string)[]
                     {
                         (good with { Objects = new List<string> { "library.o", "extra.o" } }, "holds library.o, not the objects binary.json lists"),
                         (good with { SourcesHash = new string('1', 64) }, "it was not built from the sources this package carries"),
                         (good with { Files = new List<BinaryFile> { new(PackageBinaries.ArchiveName, new string('2', 64)) } }, "'library.a' is not the file binary.json lists (SHA-256 differs)"),
                         (good with { Version = "2.0.0" }, "it is for Sample.Lib 2.0.0"),
                         (good with { BinaryTarget = "os4" }, "it is for 'os4', not 'm68k'"),
                         (good with { Init = "not a C name" }, "lacks the target, the E# stamp or the init function"),
                     })
            {
                Manifest(bad);
                var problems = Problems(out binaries);
                Assert.Empty(binaries);
                Assert.True(problems.Any(p => p.Contains(says)), $"expected '{says}', got: {string.Join(" | ", problems)}");
            }
            Manifest(good);
            // A file the manifest does not list, beside the archive; an object anywhere else in the package.
            File.WriteAllBytes(Path.Combine(bin, "fast.o"), new byte[] { 0 });
            Assert.Contains(Problems(out _), p => p.Contains("it carries 'fast.o', which binary.json does not list (hand-made objects are refused)"));
            File.Delete(Path.Combine(bin, "fast.o"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net10.0", "libfast.a"), new byte[] { 0 });
            Assert.Equal(new[] { "lib/net10.0/libfast.a" }, Problems(out _));
            // A package with problems is never linked.
            var withProblem = lib.WithBinaries(new[] { good with { Folder = bin } }, new[] { "it carries the prebuilt file 'lib/net10.0/libfast.a'" });
            var r = EsCompiler.Compile(new[] { new SourceFile("game.cs", GameSource) }, Options(withProblem));
            Assert.Empty(r.Linked);
            Assert.Contains(r.Diagnostics, d => d.Id == "ES0118" && d.GetMessage().Contains("libfast.a"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Archive_members_are_read_from_gnu_and_bsd_archives()
    {
        static byte[] Header(string name, int size) => Encoding.ASCII.GetBytes(name.PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "644".PadRight(8) + size.ToString().PadRight(10) + "`\n");
        var data = new List<byte>(Encoding.ASCII.GetBytes("!<arch>\n"));
        data.AddRange(Header("/", 4)); data.AddRange(new byte[4]);
        data.AddRange(Header("//", 22)); data.AddRange(Encoding.ASCII.GetBytes("a_very_long_name.o/\n\n\n"));
        data.AddRange(Header("library.o/", 3)); data.AddRange(new byte[] { 1, 2, 3, 10 });
        data.AddRange(Header("/0", 2)); data.AddRange(new byte[2]);
        Assert.Equal(new[] { "library.o", "a_very_long_name.o" }, PackageBinaries.ArchiveMembers(data.ToArray()));
        Assert.Null(PackageBinaries.ArchiveMembers(Encoding.ASCII.GetBytes("not an archive")));
    }
}
