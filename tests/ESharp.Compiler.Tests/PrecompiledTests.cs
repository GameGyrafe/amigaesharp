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
