using System.Collections.Generic;
using System.Text;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>E# library packages (issue #31): sources from a package compiled with the program, and the package checks.</summary>
public class PackageTests
{
    private const string EaseLib = @"using ESharp;

namespace Ease;

public struct Pair<T> where T : unmanaged
{
    public T A;
    public T B;
    public Pair(T a, T b) { A = a; B = b; }
    public Pair<T> Swapped() { return new Pair<T>(B, A); }
}

public static class Curves
{
    public const int One = 4096;
    public static int InQuad(int t) { t = Clamp(t); return (t * t) >> 12; }
    public static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;
    internal static int Clamp(int t) => t < 0 ? 0 : t > One ? One : t;
}

/// <summary>The same simple name as a type of the program, in the package's own namespace.</summary>
public static class Names
{
    public static int Value() => 1;
}
";

    /// <summary>A package as the loader reads it from a DLL: the manifest and the sources as resources.</summary>
    internal static EsPackage Package(string id, IReadOnlyDictionary<string, string> files, string targets = "\"m68k\", \"os4\", \"sdl\"",
        string features = "", string license = "MIT", string minSdk = "0.1.0", string version = "1.0.0", string extraManifest = "",
        IReadOnlyDictionary<string, byte[]>? extraResources = null)
    {
        var manifest = $"{{ \"format\": 1, \"id\": \"{id}\", \"version\": \"{version}\", \"targets\": [{targets}], \"minSdk\": \"{minSdk}\", " +
                       $"\"features\": [{features}], \"license\": \"{license}\"{extraManifest} }}";
        var resources = new Dictionary<string, byte[]> { [PackageReader.ManifestResource] = Encoding.UTF8.GetBytes(manifest) };
        foreach (var (path, text) in files) resources[PackageReader.SourcePrefix + path] = Encoding.UTF8.GetBytes(text);
        foreach (var (name, bytes) in extraResources ?? new Dictionary<string, byte[]>()) resources[name] = bytes;
        return PackageReader.FromResources(resources, resources[PackageReader.ManifestResource], id, version);
    }

    private static EsPackage Ease(string features = "", string license = "MIT", string targets = "\"m68k\", \"os4\", \"sdl\"") =>
        Package("Sample.Ease", new Dictionary<string, string> { ["Ease.cs"] = EaseLib }, targets, features, license);

    private static CompileResult Build(string program, CompileOptions options) =>
        EsCompiler.Compile(new[] { new SourceFile("game.cs", program) }, options);

    private static CompileOptions M68k(params EsPackage[] packages) =>
        Targets.M68k.ToOptions(packages: packages, sdkVersion: "0.1.5");

    private const string Game = Prelude + @"using Ease;

namespace Game;

public static class Names
{
    public static int Value() => 2;
}

public static class P
{
    static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }

    [EntryPoint]
    public static void Main()
    {
        var p = new Pair<int>(3, 4).Swapped();
        Check(p.A == 4 && p.B == 3, ""generic struct of the package"");
        var q = new Pair<Fix>(Fix.FromInt(1), Fix.FromInt(2));
        Check(Fix.ToInt(q.Swapped().A) == 2, ""same struct with another type argument"");
        Check(Curves.InQuad(2048) == 1024 && Curves.InQuad(99999) == Curves.One, ""package method"");
        Check(Curves.Pick(false, 7, 8) == 8, ""generic method of the package"");
        Check(Game.Names.Value() == 2 && Ease.Names.Value() == 1, ""same type name in the program and the package"");
    }
}
";

    [Fact]
    public void Package_code_compiles_with_the_program_and_runs()
    {
        var r = Build(Game, M68k(Ease()));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains(" \"<package Sample.Ease 1.0.0>/Ease.cs\"\n", r.C!);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal("ok generic struct of the package\nok same struct with another type argument\nok package method\n" +
                     "ok generic method of the package\nok same type name in the program and the package\n", output);
    }

    [Fact]
    public void Two_packages_with_the_same_type_name_in_their_own_namespaces_run()
    {
        var other = Package("Sample.Other", new Dictionary<string, string>
        {
            ["Names.cs"] = "namespace Other;\npublic static class Names { public static int Value() => 3; }\n",
        });
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { " +
                      "Dos.PutStr(Ease.Names.Value() + Other.Names.Value() == 4 ? \"ok\\n\" : \"FAIL\\n\"); } }";
        var r = Build(program, M68k(Ease(), other));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (!HasGcc) return;
        Assert.Equal((0, "ok\n"), RunWithHostGcc(r.C!));
    }

    [Fact]
    public void Subset_rules_apply_to_package_code_and_name_the_package_file()
    {
        var bad = Package("Sample.Bad", new Dictionary<string, string>
        {
            ["Sub/Bad.cs"] = "namespace Bad;\npublic static class B { public static int F() { try { return 1; } catch { return 2; } } }\n",
        });
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { Bad.B.F(); } }", M68k(bad));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0001");
        Assert.Equal("<package Sample.Bad 1.0.0>/Sub/Bad.cs", d.Location.GetLineSpan().Path);
        Assert.StartsWith("<package Sample.Bad 1.0.0>/Sub/Bad.cs(2,", d.ToString());
    }

    [Fact]
    public void CSharp_errors_in_package_code_name_the_package_file()
    {
        var bad = Package("Sample.Broken", new Dictionary<string, string> { ["Broken.cs"] = "namespace Broken;\npublic static class B { public static int F() => missing; }\n" });
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(bad));
        Assert.Contains(r.Diagnostics, d => d.Id == "CS0103" && d.Location.GetLineSpan().Path == "<package Sample.Broken 1.0.0>/Broken.cs");
    }

    [Fact]
    public void Library_versions_are_checked_in_package_code()
    {
        var lib = Package("Sample.New", new Dictionary<string, string>
        {
            ["New.cs"] = "using ESharp;\nnamespace NewLib;\npublic static class N {\n" +
                         "  [AmigaLib(\"dos\", -900), Since(50)] static extern int NewCall(int x);\n" +
                         "  public static int F() { return NewCall(1); }\n}\n",
        }, features: "\"AmigaLib\"");
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { NewLib.N.F(); } }", M68k(lib));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0101" && d.Location.GetLineSpan().Path == "<package Sample.New 1.0.0>/New.cs");
        Assert.DoesNotContain(r.Diagnostics, d => d.Id == "ES0113");
    }

    [Fact]
    public void A_target_the_package_does_not_list_is_an_error()
    {
        var r = Build(Game, Targets.Aros.ToOptions(packages: new[] { Ease() }));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0110");
        Assert.Contains("'Sample.Ease' 1.0.0 does not support target 'aros' (it lists: m68k, os4, sdl)", d.GetMessage());
        Assert.True(Build(Game, Targets.Os4.ToOptions(packages: new[] { Ease() })).Success);
    }

    [Theory]
    [InlineData("GPL-3.0-only", true)]
    [InlineData("LGPL-2.1-or-later", true)]
    [InlineData("AGPL-3.0-only", true)]
    [InlineData("gpl-2.0+", true)]
    [InlineData("MIT AND LGPL-2.1-only", true)]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0", true)]
    [InlineData("(MIT OR Apache-2.0) AND GPL-3.0-only", true)]
    [InlineData("MIT OR GPL-3.0-only", false)]
    [InlineData("(GPL-2.0-only OR LGPL-2.1-only) OR BSD-3-Clause", false)]
    [InlineData("MIT", false)]
    [InlineData("Apache-2.0 WITH LLVM-exception", false)]
    [InlineData("", false)]
    [InlineData("MIT AND (", false)]
    [InlineData("GPL-3.0 AND (", true)]
    public void Copyleft_licence_expressions(string expression, bool copyleft) =>
        Assert.Equal(copyleft, Licenses.IsCopyleft(expression));

    [Fact]
    public void A_gpl_package_is_an_error_unless_the_program_allows_it()
    {
        var gpl = Ease(license: "LGPL-2.1-or-later");
        var r = Build(Game, M68k(gpl));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0111");
        Assert.Contains("EsAllowCopyleft", d.GetMessage());
        Assert.True(Build(Game, Targets.M68k.ToOptions(packages: new[] { gpl }, allowCopyleft: true)).Success);
        Assert.True(Build(Game, M68k(Ease(license: "MIT OR GPL-3.0-only"))).Success);
    }

    [Fact]
    public void A_package_that_needs_a_newer_esharp_is_an_error_except_on_a_dev_build()
    {
        var lib = Package("Sample.Ease", new Dictionary<string, string> { ["Ease.cs"] = EaseLib }, minSdk: "0.2.0");
        var r = Build(Game, M68k(lib));
        Assert.Contains("needs E# 0.2.0 or newer; this esharp is 0.1.5", Assert.Single(r.Diagnostics, x => x.Id == "ES0112").GetMessage());
        Assert.True(Build(Game, Targets.M68k.ToOptions(packages: new[] { lib }, sdkVersion: "0.2.0")).Success);
        Assert.True(Build(Game, Targets.M68k.ToOptions(packages: new[] { lib }, sdkVersion: "0.1.0-dev")).Success);
    }

    private const string Bindings = "using ESharp;\nnamespace Raw;\npublic static class R {\n" +
        "  [AmigaLib(\"exec\", -132)] public static extern void Forbid();\n" +
        "  [Native(\"es_raw\")] public static extern int Raw(int x);\n" +
        "  [InlineAsm(\"moveq #1,%[result]\")] public static int One() { return 1; }\n}\n";

    [Fact]
    public void Platform_level_features_must_be_declared()
    {
        var undeclared = Package("Sample.Raw", new Dictionary<string, string> { ["Raw.cs"] = Bindings });
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { Raw.R.One(); } }";
        var r = Build(program, M68k(undeclared));
        var features = r.Diagnostics.Where(d => d.Id == "ES0113").Select(d => d.GetMessage()).ToList();
        Assert.Equal(3, features.Count);
        Assert.Contains(features, m => m.Contains("uses AmigaLib ('Forbid')"));
        Assert.Contains(features, m => m.Contains("uses Native ('Raw')"));
        Assert.Contains(features, m => m.Contains("uses InlineAsm ('One')") && m.Contains("EsFeatures"));

        var partly = Package("Sample.Raw", new Dictionary<string, string> { ["Raw.cs"] = Bindings }, features: "\"AmigaLib\", \"inlineasm\"");
        Assert.Contains("uses Native ('Raw')", Assert.Single(Build(program, M68k(partly)).Diagnostics, d => d.Id == "ES0113").GetMessage());
    }

    private const string RawUse = "using ESharp.Platform;\nnamespace RawUse;\npublic static unsafe class U {\n" +
        "  public static int Block() { byte* b = Exec.AllocVec(16, 0); if (b == null) return 0; Exec.FreeVec(b); return 1; }\n" +
        "  public static void Say(string s) { Dos.PutStr(s); Dos.PutStr(\"\\n\"); }\n}\n";

    [Fact]
    public void Calling_the_platform_raw_bindings_needs_the_feature_too()
    {
        // Not declared in the package, but called from it: Exec.AllocVec/FreeVec and (on the Amiga targets) Dos.PutStr are [AmigaLib] externs.
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { RawUse.U.Say(RawUse.U.Block() == 1 ? \"ok\" : \"FAIL\"); } }";
        var undeclared = Package("Sample.RawUse", new Dictionary<string, string> { ["U.cs"] = RawUse });
        var features = Build(program, M68k(undeclared)).Diagnostics.Where(d => d.Id == "ES0113").Select(d => d.GetMessage()).ToList();
        Assert.Equal(3, features.Count);   // each method once, though Dos.PutStr is called twice
        Assert.Contains(features, m => m.Contains("'Sample.RawUse' uses AmigaLib ('Exec.AllocVec')"));
        Assert.Contains(features, m => m.Contains("uses AmigaLib ('Exec.FreeVec')"));
        Assert.Contains(features, m => m.Contains("uses AmigaLib ('Dos.PutStr')"));

        var declared = Package("Sample.RawUse", new Dictionary<string, string> { ["U.cs"] = RawUse }, features: "\"AmigaLib\"");
        var r = Build(program, M68k(declared));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (HasGcc) Assert.Equal((0, "ok\n"), RunWithHostGcc(r.C!));

        // On sdl, Dos.PutStr is an ordinary method of the platform (over the C runtime): nothing to declare there.
        var say = Package("Sample.Say", new Dictionary<string, string>
        {
            ["S.cs"] = "using ESharp.Platform;\nnamespace Say;\npublic static class S { public static void Line(string s) { Dos.PutStr(s); } }\n",
        });
        var sdl = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { Say.S.Line(\"ok\"); } }",
            Targets.Sdl.ToOptions(packages: new[] { say }, sdkVersion: "0.1.5"));
        Assert.True(sdl.Success, string.Join("\n", sdl.Diagnostics));
    }

    // Pick<T> makes the generic expansion rewrite and re-parse this file, which must keep the library's symbols.
    private const string Symbols = "namespace Sym;\npublic static class S {\n" +
        "  public static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;\n" +
        "#if FANCY\n  public static int Mode() => 1;\n  public static int Fancy() => 5;\n" +
        "#else\n  public static int Mode() => 2;\n#endif\n" +
        "#if AMIGA\n  public static bool Amiga() => true;\n#else\n  public static bool Amiga() => false;\n#endif\n}\n";

    [Fact]
    public void A_library_own_symbols_hold_for_its_files_only()
    {
        // FANCY is the library's DefineConstants (manifest "defines"): its files see it next to the target's symbols; the program does not.
        var lib = Package("Sample.Sym", new Dictionary<string, string> { ["S.cs"] = Symbols }, extraManifest: ", \"defines\": [\"FANCY\"]");
        Assert.Empty(lib.Problems);
        Assert.Equal(new[] { "FANCY" }, lib.Manifest!.Symbols);
        var program = Prelude + "public static class P {\n" +
                      "  static void Check(bool ok, string name) { Dos.PutStr(ok ? \"ok \" : \"FAIL \"); Dos.PutStr(name); Dos.PutStr(\"\\n\"); }\n" +
                      "  [EntryPoint] public static void Main() {\n" +
                      "    Check(Sym.S.Mode() == 1 && Sym.S.Pick(false, 3, 4) == 4 && Sym.S.Fancy() == 5, \"library symbol\");\n" +
                      "    Check(Sym.S.Amiga(), \"target symbol in the library\");\n" +
                      "#if FANCY\n    Check(false, \"not in the program\");\n#else\n    Check(true, \"not in the program\");\n#endif\n  }\n}\n";
        var r = Build(program, M68k(lib));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (HasGcc) Assert.Equal((0, "ok library symbol\nok target symbol in the library\nok not in the program\n"), RunWithHostGcc(r.C!));

        // Without the symbol the other branch is compiled (and Fancy does not exist).
        var plain = Package("Sample.Sym", new Dictionary<string, string> { ["S.cs"] = Symbols });
        Assert.Contains(Build(program, M68k(plain)).Diagnostics, d => d.Id == "CS0117" && d.GetMessage().Contains("Fancy"));

        // A target symbol, DEBUG/TRACE or a non-symbol in the manifest: the package cannot be used.
        var bad = Package("Sample.Sym", new Dictionary<string, string> { ["S.cs"] = Symbols }, extraManifest: ", \"defines\": [\"AMIGA\", \"DEBUG\", \"A-B\"]");
        Assert.Equal(3, bad.Problems.Count);
        Assert.Contains(bad.Problems, p => p.Contains("'AMIGA', a symbol esharp build sets itself"));
        Assert.Contains(bad.Problems, p => p.Contains("'A-B', which is not a preprocessor symbol"));
        Assert.Contains(Build(program, M68k(bad)).Diagnostics, d => d.Id == "ES0118");
    }

    [Fact]
    public void An_entry_point_in_a_package_is_an_error()
    {
        var lib = Package("Sample.Main", new Dictionary<string, string>
        {
            ["Main.cs"] = "using ESharp;\nnamespace M;\npublic static class Lib { [EntryPoint] public static void Main() { } }\n",
        });
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(lib));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0114" && d.GetMessage().Contains("'Sample.Main'"));
        Assert.DoesNotContain(r.Diagnostics, d => d.Id == "ES0021");
    }

    [Fact]
    public void The_same_type_in_a_package_and_the_program_is_an_error()
    {
        // A partial declaration would otherwise merge silently into the package's type.
        var program = Prelude + "namespace Ease { public static partial class Curves { public static int Mine() => 1; } }\n" +
                      "public static class P { [EntryPoint] public static void Main() { } }";
        var lib = Package("Sample.Ease", new Dictionary<string, string> { ["Ease.cs"] = EaseLib.Replace("public static class Curves", "public static partial class Curves") });
        var r = Build(program, M68k(lib));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0115");
        Assert.Equal("'Ease.Curves' is declared in the program and in package 'Sample.Ease'; a package and the program, or two packages, cannot declare the same type (use a namespace of your own)", d.GetMessage());
    }

    [Fact]
    public void The_same_type_in_two_packages_is_an_error()
    {
        var twin = Package("Sample.Twin", new Dictionary<string, string> { ["Twin.cs"] = "namespace Ease;\npublic static class Names { public static int Value() => 9; }\n" });
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(Ease(), twin));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0115" && d.GetMessage().Contains("'Ease.Names' is declared in package 'Sample.Ease' and in package 'Sample.Twin'"));
    }

    [Fact]
    public void Internal_package_members_cannot_be_used_from_the_program()
    {
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { Ease.Curves.Clamp(5); } }";
        var r = Build(program, M68k(Ease()));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0116");
        Assert.Contains("'Ease.Curves.Clamp(int)' is internal to package 'Sample.Ease' and cannot be used from the program", d.GetMessage());
    }

    [Fact]
    public void Deriving_from_a_package_class_is_an_error()
    {
        var lib = Package("Sample.Base", new Dictionary<string, string> { ["Base.cs"] = "namespace B;\npublic class Animal { public int Legs; }\n" });
        var program = Prelude + "public class Dog : B.Animal { }\npublic static class P { [EntryPoint] public static void Main() { var a = new B.Animal(); a.Legs = 4; } }";
        var r = Build(program, M68k(lib));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0117" && d.GetMessage().Contains("'Dog' derives from 'B.Animal' of package 'Sample.Base'"));
        Assert.True(Build(Prelude + "public static class P { [EntryPoint] public static void Main() { var a = new B.Animal(); a.Legs = 4; } }", M68k(lib)).Success);
    }

    [Fact]
    public void C_files_unknown_resources_and_unknown_manifests_are_refused()
    {
        var unknown = Package("Sample.Art", new Dictionary<string, string> { ["A.cs"] = "namespace A; public static class X { }" },
            extraResources: new Dictionary<string, byte[]> { ["esharp/assets/cards.png"] = new byte[] { 1 } });
        var cfile = Package("Sample.C", new Dictionary<string, string> { ["A.cs"] = "namespace C; public static class X { }", ["fast.c"] = "int f;" });
        var native = Package("Sample.Native", new Dictionary<string, string>(), extraManifest: ", \"native\": [\"fast.c\"]");
        var newer = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 2, \"id\": \"Sample.N\" }"), "Sample.N", "1.0.0");
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        var r = Build(program, M68k(unknown, cfile, native, newer));
        var messages = r.Diagnostics.Where(d => d.Id == "ES0118").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Art' 1.0.0: it carries 'esharp/assets/cards.png', which this E# does not know"));
        Assert.Contains(messages, m => m.Contains("'Sample.C'") && m.Contains("C files and headers in packages are not supported yet"));
        Assert.Contains(messages, m => m.Contains("'Sample.Native'") && m.Contains("declares native C code"));
        Assert.Contains(messages, m => m.Contains("'Sample.N'") && m.Contains("format 2"));
        var mismatch = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 1, \"id\": \"A\", \"targets\": [\"m68k\"] }"), "B", "1.0.0");
        Assert.Contains(mismatch.Problems, p => p.Contains("names the library 'A', not 'B'"));
    }

    /// <summary>The Assets.g.cs that esharp assets writes for an assets.json with a 16x2 four-colour image (masked) and a short sound.</summary>
    private static string GeneratedAssets(string ns, string cls)
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-pkgassets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Row 0: transparent, red, green, blue repeated; row 1: all red.
            var px = new byte[16 * 2 * 4];
            byte[][] colours = { new byte[] { 0, 0, 0, 0 }, new byte[] { 255, 0, 0, 255 }, new byte[] { 0, 255, 0, 255 }, new byte[] { 0, 0, 255, 255 } };
            for (var x = 0; x < 16; x++)
            {
                Array.Copy(colours[x % 4], 0, px, x * 4, 4);
                Array.Copy(colours[1], 0, px, (16 + x) * 4, 4);
            }
            File.WriteAllBytes(Path.Combine(dir, "dot.png"), ESharp.Assets.Png.Write(new ESharp.Assets.RgbaImage(16, 2, px)));
            var wav = new List<byte>();
            void Tag(string t) { foreach (var c in t) wav.Add((byte)c); }
            void Le32(int v) { for (var i = 0; i < 4; i++) wav.Add((byte)(v >> (8 * i))); }
            void Le16(int v) { wav.Add((byte)v); wav.Add((byte)(v >> 8)); }
            Tag("RIFF"); Le32(36 + 6); Tag("WAVE"); Tag("fmt "); Le32(16); Le16(1); Le16(1); Le32(8000); Le32(8000); Le16(1); Le16(8); Tag("data"); Le32(6);
            wav.AddRange(new byte[] { 128, 228, 28, 128, 200, 56 });
            File.WriteAllBytes(Path.Combine(dir, "tick.wav"), wav.ToArray());
            File.WriteAllText(Path.Combine(dir, "assets.json"),
                $"{{ \"namespace\": \"{ns}\", \"class\": \"{cls}\", \"images\": [ {{ \"name\": \"Dot\", \"file\": \"dot.png\", \"colors\": 4, \"mask\": true }} ], " +
                "\"samples\": [ { \"name\": \"Tick\", \"file\": \"tick.wav\" } ] }");
            return ESharp.Assets.AssetBuilder.Build(Path.Combine(dir, "assets.json")).Source;
        }
        finally { Directory.Delete(dir, true); }
    }

    private static EsPackage ArtPackage(string id = "Sample.Art", string ns = "Art", string cls = "Sprites") =>
        Package(id, new Dictionary<string, string>
        {
            ["Assets.g.cs"] = GeneratedAssets(ns, cls),
            ["Draw.cs"] = $"namespace {ns};\npublic static class Dots {{ public static int Opaque() {{ int n = 0; foreach (var b in {cls}.DotMask) for (int i = 0; i < 8; i++) if ((b & (0x80 >> i)) != 0) n++; return n; }} }}\n",
        }, extraManifest: $", \"assets\": {{ \"source\": \"Assets.g.cs\", \"namespace\": \"{ns}\", \"class\": \"{cls}\" }}");

    private const string ArtGame = Prelude + @"
namespace Game;

public static class Sprites { public static int Mine = 7; }

public static class P
{
    static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }

    [EntryPoint]
    public static void Main()
    {
        Check(Art.Sprites.DotWidth == 16 && Art.Sprites.DotHeight == 2 && Art.Sprites.DotDepth == 2 && Art.Sprites.DotPalette.Length == 4, ""constants"");
        Check(Art.Sprites.DotPlanes.Length == 8 && Art.Sprites.DotMask.Length == 4, ""chip data sizes"");
        Check(Art.Sprites.DotMask[0] == 0x77 && Art.Sprites.DotMask[1] == 0x77 && Art.Sprites.DotMask[2] == 0xFF && Art.Dots.Opaque() == 28, ""mask from the package"");
        int red = Art.Sprites.DotPalette[1] == 0xF00 ? 1 : Art.Sprites.DotPalette[2] == 0xF00 ? 2 : 3;
        int row1 = (Art.Sprites.DotPlanes[2] == 0xFF ? 1 : 0) | (Art.Sprites.DotPlanes[6] == 0xFF ? 2 : 0);
        Check(Art.Sprites.DotPalette[red] == 0xF00 && row1 == red && Art.Sprites.DotPalette[0] == 0, ""planes and palette agree"");
        Check(Art.Sprites.TickLength == 6 && Art.Sprites.TickRate == 8000 && (sbyte)Art.Sprites.TickData[1] == 100 && (sbyte)Art.Sprites.TickData[2] == -100, ""sample"");
        Check(Game.Sprites.Mine == 7, ""the game's own Sprites class next to the package's"");
    }
}
";

    [Fact]
    public void Package_assets_are_chip_data_under_the_package_namespace_and_run()
    {
        var art = ArtPackage();
        Assert.Empty(art.Problems);
        Assert.Equal(new PackageAssets("Assets.g.cs", "Art", "Sprites"), art.Manifest!.Assets);
        var r = Build(ArtGame, M68k(art));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        // The package's images and sample are static [ChipRam] data, as a program's own assets: __chip on m68k, in the chip budget.
        var c = r.C!;
        foreach (var field in new[] { "DotPlanes", "DotMask", "TickData" })
            Assert.Contains(c.Split('\n'), l => l.StartsWith("static __chip ") && l.Contains("Art_Sprites_" + field));
        Assert.DoesNotContain(c.Split('\n'), l => l.Contains("__chip") && l.Contains("DotPalette"));
        Assert.Equal(new[] { ("Sprites.DotPlanes", 8L), ("Sprites.DotMask", 4L), ("Sprites.TickData", 6L) },
            r.Chip.Select(e => (e.What, e.Bytes)).ToArray());
        Assert.All(r.Chip, e => Assert.StartsWith("<package Sample.Art 1.0.0>/Assets.g.cs(", e.Location));
        // PowerPC: no __chip (chip memory is allocated at run time there), the same data.
        var os4 = Build(ArtGame, Targets.Os4.ToOptions(packages: new[] { art }, sdkVersion: "0.1.5"));
        Assert.True(os4.Success, string.Join("\n", os4.Diagnostics));
        Assert.DoesNotContain("__chip", os4.C!);
        Assert.Contains("Art_Sprites_DotPlanes", os4.C!);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c.Replace("static __chip ", "static "));   // the host gcc has no chip RAM
        Assert.Equal(0, code);
        Assert.Equal("ok constants\nok chip data sizes\nok mask from the package\nok planes and palette agree\nok sample\n" +
                     "ok the game's own Sprites class next to the package's\n", output);
    }

    [Fact]
    public void Package_assets_in_the_same_class_as_the_game_or_another_package_are_an_error()
    {
        // The game's assets.json chose the package's namespace and class: C# says CS0101, and ES0115 names the assets.json of both.
        var program = Prelude + "namespace Art { public static class Sprites { public const int MineWidth = 1; } }\n" +
                      "public static class P { [EntryPoint] public static void Main() { } }";
        var r = EsCompiler.Compile(new[] { new SourceFile("Assets.g.cs", program) }, M68k(ArtPackage()));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0115");
        Assert.Equal("'Art.Sprites' is declared in the program (its assets: the \"namespace\" and \"class\" of its assets.json) and in package 'Sample.Art' " +
                     "(its assets: the \"namespace\" and \"class\" of its assets.json); a package and the program, or two packages, cannot declare the same type (use a namespace of your own)",
            d.GetMessage());
        var twin = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(ArtPackage(), ArtPackage("Sample.Twin")));
        Assert.Contains(twin.Diagnostics, x => x.Id == "ES0115" && x.GetMessage().StartsWith("'Art.Sprites' is declared in package 'Sample.Art' (its assets"));
        // In their own namespaces, two packages with assets are fine.
        var two = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { Dos.PutStr(Art.Sprites.DotWidth + Other.Sprites.DotWidth == 32 ? \"ok\\n\" : \"FAIL\\n\"); } }",
            M68k(ArtPackage(), ArtPackage("Sample.Other", ns: "Other")));
        Assert.True(two.Success, string.Join("\n", two.Diagnostics));
    }

    [Fact]
    public void Package_asset_declarations_are_checked()
    {
        var missing = Package("Sample.M", new Dictionary<string, string> { ["A.cs"] = "namespace M; public static class X { }" },
            extraManifest: ", \"assets\": { \"source\": \"Assets.g.cs\", \"namespace\": \"M\", \"class\": \"Art\" }");
        Assert.Contains(missing.Problems, p => p.Contains("names the asset source 'Assets.g.cs', which the package does not carry"));
        var list = Package("Sample.L", new Dictionary<string, string>(), extraManifest: ", \"assets\": [\"x.png\"]");
        Assert.Contains(list.Problems, p => p.Contains("declares assets in a form this E# does not know"));
        var badClass = Package("Sample.B", new Dictionary<string, string> { ["Assets.g.cs"] = "" },
            extraManifest: ", \"assets\": { \"source\": \"Assets.g.cs\", \"namespace\": \"B\", \"class\": \"1x\" }");
        Assert.Contains(badClass.Problems, p => p.Contains("asset class 'B.1x'"));
        var noSource = Package("Sample.S", new Dictionary<string, string>(), extraManifest: ", \"assets\": { \"namespace\": \"S\", \"class\": \"Art\" }");
        Assert.Contains(noSource.Problems, p => p.Contains("without the C# source"));
        var none = Package("Sample.N", new Dictionary<string, string>(), extraManifest: ", \"assets\": null");
        Assert.Empty(none.Problems);
        Assert.Null(none.Manifest!.Assets);
        Assert.Contains("  assets: Art.Sprites (Assets.g.cs; images and samples converted when the library was built)",
            ESharp.Cli.PackageStep.Describe(new[] { ArtPackage() }));
    }

    [Fact]
    public void Source_paths_are_normalised_and_bad_ones_refused()
    {
        var p = Package("Sample.Paths", new Dictionary<string, string>
        {
            ["Sub\\Win.cs"] = "namespace S; public static class W { }",
            ["../Up.cs"] = "namespace S; public static class U { }",
        });
        Assert.Equal(new[] { "Sub/Win.cs" }, p.Sources.Select(s => s.Path).ToArray());
        Assert.Contains(p.Problems, x => x.Contains("bad path '../Up.cs'"));
    }

    [Fact]
    public void Sdk_versions_compare_by_number()
    {
        Assert.True(SdkVersions.Numeric("0.1.40") > SdkVersions.Numeric("0.1.9"));
        Assert.Equal(SdkVersions.Numeric("0.1.0"), SdkVersions.Numeric("0.1.0-dev"));
        Assert.Null(SdkVersions.Numeric("abc"));
        Assert.True(SdkVersions.IsDev("0.1.0-dev"));
        Assert.False(SdkVersions.IsDev("0.1.41"));
    }

    private const string Server = "using ESharp;\nnamespace Srv;\npublic static unsafe class S {\n" +
        "  [Interrupt] public static int Tick(void* data) { return 0; }\n" +
        "  [HookEntry] public static nint Entry(void* hook, void* obj, void* msg) { return 0; }\n}\n";

    [Fact]
    public void Interrupt_and_hook_entry_are_platform_level_features()
    {
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        var undeclared = Package("Sample.Srv", new Dictionary<string, string> { ["S.cs"] = Server });
        var r = Build(program, M68k(undeclared));
        var messages = r.Diagnostics.Where(d => d.Id == "ES0113").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("Interrupt ('Tick')"));
        Assert.Contains(messages, m => m.Contains("HookEntry ('Entry')"));
        var partly = Package("Sample.Srv", new Dictionary<string, string> { ["S.cs"] = Server }, features: "\"Interrupt\"");
        Assert.Single(Build(program, M68k(partly)).Diagnostics, d => d.Id == "ES0113");
        var declared = Package("Sample.Srv", new Dictionary<string, string> { ["S.cs"] = Server }, features: "\"interrupt\", \"HookEntry\"");
        Assert.DoesNotContain(Build(program, M68k(declared)).Diagnostics, d => d.Id == "ES0113" || d.Id == "ES0118");
    }

    [Fact]
    public void A_package_that_needs_a_newer_esharp_is_found_from_the_manifest_alone()
    {
        // `esharp test` runs this before the host run loads the package DLL.
        var lib = Package("Sample.Ease", new Dictionary<string, string> { ["Ease.cs"] = EaseLib }, minSdk: "0.2.0");
        var error = Assert.Single(ESharp.Cli.PackageStep.SdkErrors(new[] { lib, Ease() }, "0.1.5"));
        Assert.Contains("ES0112", error);
        Assert.Contains("Package 'Sample.Ease' 1.0.0 needs E# 0.2.0 or newer; this esharp is 0.1.5", error);
        Assert.Empty(ESharp.Cli.PackageStep.SdkErrors(new[] { lib }, "0.2.0"));
        Assert.Empty(ESharp.Cli.PackageStep.SdkErrors(new[] { lib }, "0.1.0-dev"));
    }

    private static string WriteDll(string path, string manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path),
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class C { }") },
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var resources = new[]
        {
            new Microsoft.CodeAnalysis.ResourceDescription(PackageReader.ManifestResource, () => new MemoryStream(Encoding.UTF8.GetBytes(manifest)), true),
            new Microsoft.CodeAnalysis.ResourceDescription(PackageReader.SourcePrefix + "A.cs", () => new MemoryStream(Encoding.UTF8.GetBytes("namespace A; public static class X { }")), true),
        };
        using var stream = File.Create(path);
        Assert.True(compilation.Emit(stream, manifestResources: resources).Success);
        return path;
    }

    [Fact]
    public void Two_compile_dlls_with_a_manifest_are_one_package_read_from_lib()
    {
        var root = Path.Combine(Path.GetTempPath(), "esharp-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var packageDir = Path.Combine(root, "cache", "sample.two", "1.0.0");
            const string manifest = "{ \"format\": 1, \"id\": \"Sample.Two\", \"version\": \"1.0.0\", \"targets\": [\"m68k\"], \"minSdk\": \"0.1.0\", \"features\": [], \"license\": \"MIT\" }";
            var lib = WriteDll(Path.Combine(packageDir, "lib", "net10.0", "Two.dll"), manifest);
            WriteDll(Path.Combine(packageDir, "ref", "net10.0", "Two.dll"), manifest);
            var assets = Path.Combine(root, "project.assets.json");
            File.WriteAllText(assets, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["packageFolders"] = new Dictionary<string, object> { [Path.Combine(root, "cache")] = new { } },
                ["libraries"] = new Dictionary<string, object> { ["Sample.Two/1.0.0"] = new { type = "package", path = "sample.two/1.0.0" } },
                ["targets"] = new Dictionary<string, object>
                {
                    ["net10.0"] = new Dictionary<string, object>
                    {
                        // as a real restore lists it: ref/ under compile, lib/ under runtime
                        ["Sample.Two/1.0.0"] = new { type = "package", compile = new Dictionary<string, object> { ["ref/net10.0/Two.dll"] = new { } }, runtime = new Dictionary<string, object> { ["lib/net10.0/Two.dll"] = new { } } },
                    },
                },
            }));
            var errors = new List<string>();
            var found = PackageLoader.FromAssetsFile(assets, errors);
            Assert.Empty(errors);
            var package = Assert.Single(found);
            Assert.Equal(lib, package.DllPath);
            Assert.Equal(new[] { "A.cs" }, package.Sources.Select(x => x.Path).ToArray());
            var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(found.ToArray()));
            Assert.DoesNotContain(r.Diagnostics, d => d.Id == "ES0115");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
