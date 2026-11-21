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
    public void Assets_c_files_and_unknown_manifests_are_refused()
    {
        var assets = Package("Sample.Art", new Dictionary<string, string> { ["A.cs"] = "namespace A; public static class X { }" },
            extraResources: new Dictionary<string, byte[]> { ["esharp/assets/cards.png"] = new byte[] { 1 } });
        var cfile = Package("Sample.C", new Dictionary<string, string> { ["A.cs"] = "namespace C; public static class X { }", ["fast.c"] = "int f;" });
        var declared = Package("Sample.D", new Dictionary<string, string>(), extraManifest: ", \"assets\": [\"x.png\"]");
        var newer = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 2, \"id\": \"Sample.N\" }"), "Sample.N", "1.0.0");
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        var r = Build(program, M68k(assets, cfile, declared, newer));
        var messages = r.Diagnostics.Where(d => d.Id == "ES0118").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Art' 1.0.0: it carries assets (esharp/assets/cards.png); assets in packages are not supported yet"));
        Assert.Contains(messages, m => m.Contains("'Sample.C'") && m.Contains("C files and headers in packages are not supported yet"));
        Assert.Contains(messages, m => m.Contains("'Sample.D'") && m.Contains("declares assets"));
        Assert.Contains(messages, m => m.Contains("'Sample.N'") && m.Contains("format 2"));
        var mismatch = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 1, \"id\": \"A\", \"targets\": [\"m68k\"] }"), "B", "1.0.0");
        Assert.Contains(mismatch.Problems, p => p.Contains("names the library 'A', not 'B'"));
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
}
