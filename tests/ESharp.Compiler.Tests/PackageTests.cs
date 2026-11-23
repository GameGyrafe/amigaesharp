using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using ESharp.Analyzers;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
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

    // An [Extensible] class of a package: virtual and abstract members, a constructor with arguments, a static count of the objects
    // made, and package code that calls the overridable members (Run, Describe). The program's Actor has the same simple name.
    internal const string ActorLib = @"using ESharp;

namespace Anim;

[Extensible]
public abstract class Actor
{
    public static int Made;
    public int X;
    public int Steps;
    protected int Speed;

    public Actor(int speed) { Speed = speed; Made++; }

    /// <summary>One step: Move (overridable), then OnStep (abstract).</summary>
    public void Run(int steps)
    {
        for (int i = 0; i < steps; i++) { X = Move(X); Steps++; OnStep(); }
        if (Steps >= 3) OnFinished();
    }

    protected virtual int Move(int x) => x + Speed;
    protected abstract void OnStep();
    protected virtual void OnFinished() { }
    public virtual int Kind => 1;
}

/// <summary>A class of the package that is not made for deriving.</summary>
public class Plain { public int Legs; }

/// <summary>Package code that takes any Actor, the program's ones too.</summary>
public static class Stage
{
    public static int Total(Actor a, Actor b) => a.X + b.X + a.Kind * 100 + b.Kind * 1000;
}
";

    internal const string ActorGame = Prelude + @"using Anim;

namespace Game;

public sealed class Actor : Anim.Actor
{
    public int Ticks;
    public bool Finished;
    public Actor() : base(2) { }
    protected override int Move(int x) => base.Move(x) * 2;
    protected override void OnStep() { Ticks++; }
    protected override void OnFinished() { Finished = true; }
    public override int Kind => 7;
}

public class Walker : Anim.Actor
{
    public int Seen;
    public Walker(int speed) : base(speed) { }
    protected override void OnStep() { Seen += X; }
}

public class Runner : Walker
{
    public Runner() : base(5) { }
    protected override int Move(int x) => x + 10 * Speed;
}

public static class P
{
    static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }

    [EntryPoint]
    public static void Main()
    {
        var a = new Actor();
        a.Run(3);
        Check(a.X == 28 && a.Ticks == 3 && a.Finished && a.Steps == 3, ""overrides called from package code, base call into the package"");
        var w = new Walker(3);
        w.Run(2);
        Check(w.X == 6 && w.Seen == 9, ""inherited virtual of the package, abstract member implemented"");
        Anim.Actor r = new Runner();
        r.Run(1);
        Check(r.X == 50 && r.Kind == 1 && r is Walker && !(r is Actor) && (r as Runner) != null, ""two levels down, is and as"");
        Check(Stage.Total(a, w) == 28 + 6 + 700 + 1000, ""program objects passed to package code"");
        Check(Anim.Actor.Made == 3, ""the package constructor ran for each"");
        Heap.Free(ref a);
        Heap.Free(ref w);
        Heap.Free(ref r);
    }
}
";

    [Theory]
    [InlineData("m68k")]
    [InlineData("sdl")]
    public void A_program_class_derives_from_an_Extensible_package_class_and_runs(string target)
    {
        var lib = Package("Sample.Anim", new Dictionary<string, string> { ["Actor.cs"] = ActorLib });
        var r = Build(ActorGame, target == "sdl" ? Targets.Sdl.ToOptions(packages: new[] { lib }, sdkVersion: "0.1.5") : M68k(lib));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal("ok overrides called from package code, base call into the package\n" +
                     "ok inherited virtual of the package, abstract member implemented\n" +
                     "ok two levels down, is and as\n" +
                     "ok program objects passed to package code\n" +
                     "ok the package constructor ran for each\n", output);
    }

    [Fact]
    public void Deriving_from_a_package_class_not_marked_Extensible_is_an_error()
    {
        var lib = Package("Sample.Anim", new Dictionary<string, string> { ["Actor.cs"] = ActorLib });
        var program = Prelude + "public class Dog : Anim.Plain { }\npublic static class P { [EntryPoint] public static void Main() { var a = new Anim.Plain(); a.Legs = 4; } }";
        var r = Build(program, M68k(lib));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0117");
        Assert.Equal("'Dog' derives from 'Anim.Plain' of package 'Sample.Anim', which is not marked [Extensible]; only package classes made for it (marked [Extensible] in the package) can be base classes of another package's or the program's classes", d.GetMessage());
        Assert.DoesNotContain(r.Diagnostics, x => x.Id == "ES0007");
        Assert.True(Build(Prelude + "public static class P { [EntryPoint] public static void Main() { var a = new Anim.Plain(); a.Legs = 4; } }", M68k(lib)).Success);

        // Within the package itself any class can be a base class; another package needs [Extensible] too.
        var inside = Package("Sample.Zoo", new Dictionary<string, string> { ["Zoo.cs"] = "namespace Zoo;\npublic class Animal { public int Legs; }\npublic class Cat : Animal { }\n" });
        Assert.True(Build(Prelude + "public static class P { [EntryPoint] public static void Main() { var c = new Zoo.Cat(); c.Legs = 4; } }", M68k(inside)).Success);
        var other = Package("Sample.Pets", new Dictionary<string, string> { ["Pets.cs"] = "namespace Pets;\npublic class Dog : Zoo.Animal { }\npublic class Hero : Anim.Actor { public Hero() : base(1) { } protected override void OnStep() { } }\n" });
        var two = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(inside, other, lib));
        var e = Assert.Single(two.Diagnostics, x => x.Id == "ES0117");
        Assert.StartsWith("'Dog' derives from 'Zoo.Animal' of package 'Sample.Zoo'", e.GetMessage());
    }

    [Fact]
    public void Internal_package_members_cannot_be_reached_through_inheritance()
    {
        // An [Extensible] class with internal constructors and internal (or private protected) virtual members: the dotnet host build
        // rejects a base call to them and an override of them from another assembly (CS0122, CS0115), so esharp build must too.
        const string lib = @"using ESharp;
namespace Shape;
[Extensible]
public class Base
{
    public int Size;
    internal Base(int size) { Size = size; }
    internal Base() { Size = 1; }
    public Base(string name) { Size = 2; }
    internal virtual int Area() => Size;
    private protected virtual int Edge() => 1;
    internal virtual int Corners => 4;
    public virtual int Sides() => 4;
    public int Measure() => Area() + Edge() + Corners + Sides();
}
[Extensible]
public class Open
{
    public Open() { }
    internal Open(int size) { }
}
";
        var package = Package("Sample.Shape", new Dictionary<string, string> { ["Shape.cs"] = lib });
        string Program(string classes) => Prelude + classes + "\npublic static class P { [EntryPoint] public static void Main() { } }";
        IEnumerable<string> Errors(string classes) =>
            Build(Program(classes), M68k(package)).Diagnostics.Where(d => d.Id == "ES0116").Select(d => d.GetMessage().Split('\'')[1]);

        Assert.Equal(new[] { "Shape.Base.Base(int)" }, Errors("public class A : Shape.Base { public A() : base(5) { } }"));
        Assert.Equal(new[] { "Shape.Base.Base()" }, Errors("public class A : Shape.Base { public A(int x) { } }"));
        Assert.Equal(new[] { "Shape.Base.Base()" }, Errors("public class A : Shape.Base { }"));
        Assert.Equal(new[] { "Shape.Base.Area()" }, Errors("public class A : Shape.Base { public A() : base(\"a\") { } internal override int Area() => 5; }"));
        Assert.Equal(new[] { "Shape.Base.Edge()" }, Errors("public class A : Shape.Base { public A() : base(\"a\") { } private protected override int Edge() => 5; }"));
        Assert.Equal(new[] { "Shape.Base.Corners" }, Errors("public class A : Shape.Base { public A() : base(\"a\") { } internal override int Corners => 3; }"));

        // Public constructors and public virtual members are fine, implicit base() included; so is a constructor chained with this().
        Assert.Empty(Errors("public class A : Shape.Base { public A() : base(\"a\") { } public A(int x) : this() { } public override int Sides() => 3; }\n" +
                            "public class B : Shape.Open { }\npublic class C : Shape.Open { public C(int x) { } }"));
        Assert.True(Build(Program("public class A : Shape.Base { public A() : base(\"a\") { } public override int Sides() => 3; }\npublic class B : Shape.Open { }"), M68k(package)).Success);

        // Within the package the internal members are its own.
        var inside = Package("Sample.Shape", new Dictionary<string, string> { ["Shape.cs"] = lib + "public class Square : Base { public Square() : base(3) { } internal override int Area() => Size * Size; }\n" });
        Assert.True(Build(Program(""), M68k(inside)).Success);
    }

    /// <summary>
    /// The IDE and the dotnet host build see a package as a referenced assembly: its HOST build, with [Extensible] and the
    /// [assembly: EsLibrary(id)] that AmigaESharp.Sdk adds in metadata. The analyzer must agree with esharp build there.
    /// </summary>
    private static ImmutableArray<Diagnostic> AnalyzeAgainstPackageAssembly(string program, string library, string assemblyInfo)
    {
        var refs = PlatformSubclassTests.HostReferences().ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);
        var parse = new CSharpParseOptions(preprocessorSymbols: new[] { "HOST" });
        var lib = CSharpCompilation.Create("Anim", new[] { CSharpSyntaxTree.ParseText(library, parse), CSharpSyntaxTree.ParseText(assemblyInfo, parse) }, refs, options);
        Assert.Empty(lib.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var image = new System.IO.MemoryStream();
        Assert.True(lib.Emit(image).Success);
        var game = CSharpCompilation.Create("Game", new[] { CSharpSyntaxTree.ParseText(program, parse) },
            refs.Append(MetadataReference.CreateFromImage(image.ToArray())), options);
        Assert.Empty(game.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return game.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SubsetAnalyzer())).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void The_analyzer_reads_Extensible_from_the_package_assembly_as_esharp_build_reads_it_from_source()
    {
        const string withId = "[assembly: ESharp.EsLibrary(\"Sample.Anim\")]";
        // [Extensible]: no diagnostic, as in esharp build (A_program_class_derives_from_an_Extensible_package_class_and_runs).
        var ok = AnalyzeAgainstPackageAssembly(ActorGame, ActorLib, withId);
        Assert.DoesNotContain(ok, d => d.Id is "ES0007" or "ES0117");

        // Not [Extensible]: ES0117 with the same message esharp build gives (Deriving_from_a_package_class_not_marked_Extensible_is_an_error).
        var dog = Prelude + "public class Dog : Anim.Plain { }\n";
        var bad = Assert.Single(AnalyzeAgainstPackageAssembly(dog, ActorLib, withId), d => d.Id is "ES0007" or "ES0117");
        var source = Assert.Single(Build(dog + "public static class P { [EntryPoint] public static void Main() { } }",
            M68k(Package("Sample.Anim", new Dictionary<string, string> { ["Actor.cs"] = ActorLib }))).Diagnostics, d => d.Id is "ES0007" or "ES0117");
        Assert.Equal("ES0117", bad.Id);
        Assert.Equal(source.GetMessage(), bad.GetMessage());

        // A package DLL without the assembly attribute (GenerateAssemblyInfo off): named by its assembly, same rule.
        var bare = Assert.Single(AnalyzeAgainstPackageAssembly(dog, ActorLib, "// no assembly info"), d => d.Id is "ES0007" or "ES0117");
        Assert.Equal("ES0117", bare.Id);
        Assert.Contains("of package 'Anim'", bare.GetMessage());

        // A generic method of the package is monomorphised as the program's own are (not ES0008), in the IDE as in esharp build.
        var generic = Prelude + "public static class G { public static int F() => Anim.Pick.One(false, 1, 2); }\n";
        const string pickLib = "namespace Anim;\npublic static class Pick { public static T One<T>(bool first, T a, T b) where T : unmanaged => first ? a : b; }\n";
        Assert.DoesNotContain(AnalyzeAgainstPackageAssembly(generic, ActorLib, withId + "\n" + pickLib), d => d.Id == "ES0008");

        // A .NET class is still ES0007.
        Assert.Contains(AnalyzeAgainstPackageAssembly(Prelude + "public class R : System.Random { }\n", ActorLib, withId), d => d.Id == "ES0007");
    }

    [Fact]
    public void Misplaced_C_files_unknown_resources_and_unknown_manifests_are_refused()
    {
        var unknown = Package("Sample.Art", new Dictionary<string, string> { ["A.cs"] = "namespace A; public static class X { }" },
            extraResources: new Dictionary<string, byte[]> { ["esharp/assets/cards.png"] = new byte[] { 1 }, ["esharp/other/x"] = new byte[] { 1 } });
        var cfile = Package("Sample.C", new Dictionary<string, string> { ["A.cs"] = "namespace C; public static class X { }", ["fast.c"] = "int f;" });
        var oldForm = Package("Sample.Native", new Dictionary<string, string>(), extraManifest: ", \"native\": [\"fast.c\"]");
        var newer = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 2, \"id\": \"Sample.N\" }"), "Sample.N", "1.0.0");
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        var r = Build(program, M68k(unknown, cfile, oldForm, newer));
        var messages = r.Diagnostics.Where(d => d.Id == "ES0118").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Art' 1.0.0: it carries the asset input 'esharp/assets/cards.png'; a package's assets are the Assets.g.cs"));
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Art' 1.0.0: it carries 'esharp/other/x', which this E# does not know"));
        Assert.Contains(messages, m => m.Contains("'Sample.C'") && m.Contains("the C file 'fast.c' with its C# sources; a package's C goes in native/common/ or native/<target>/"));
        Assert.Contains(messages, m => m.Contains("'Sample.Native'") && m.Contains("declares native C in a form this E# does not know"));
        Assert.Contains(messages, m => m.Contains("'Sample.N'") && m.Contains("format 2"));
        var mismatch = PackageReader.FromResources(new Dictionary<string, byte[]>(), Encoding.UTF8.GetBytes("{ \"format\": 1, \"id\": \"A\", \"targets\": [\"m68k\"] }"), "B", "1.0.0");
        Assert.Contains(mismatch.Problems, p => p.Contains("names the library 'A', not 'B'"));
    }

    // ------------------------------------------------------------------ C files and headers in packages (native/, feature NativeC)

    /// <summary>A package with C files: <paramref name="native"/> maps "folder/name" to the text, embedded as esharp/native/folder/name.</summary>
    private static EsPackage NativePackage(string id, string prefix, IReadOnlyDictionary<string, string> native, string csharp,
        string features = "\"Native\", \"NativeC\"", string targets = "\"m68k\", \"os4\", \"sdl\"", IEnumerable<string>? listed = null)
    {
        var files = string.Join(", ", (listed ?? native.Keys).Select(f => "\"" + f + "\""));
        return Package(id, new Dictionary<string, string> { ["Lib.cs"] = csharp }, targets, features,
            extraManifest: $", \"native\": {{ \"prefix\": \"{prefix}\", \"files\": [{files}] }}",
            extraResources: native.ToDictionary(kv => PackageReader.NativePrefix + kv.Key, kv => Encoding.UTF8.GetBytes(kv.Value)));
    }

    private const string FixLib = @"using ESharp;

namespace Fx;

public static unsafe class M
{
    /// <summary>16.16 fixed-point multiply, in the package's C.</summary>
    [Native(""fx_mul"")] public static extern int Mul(int a, int b);
    [Native(""fx_sum"")] public static extern uint Sum(byte* data, int length);
    [Native(""fx_tuned"")] public static extern int Tuned();
    [Native] public static extern void fx_hello(string text);
}
";

    private static readonly Dictionary<string, string> FixC = new()
    {
        ["common/fx.c"] = "#include \"fx_tune.h\"\n#include <stdio.h>\n" +
                          "static int32_t fx_helper(int64_t v) { return (int32_t)(v >> 16); }\n" +
                          "static int unprefixed_but_static;\n" +
                          "int32_t fx_calls;\n" +
                          "int32_t fx_mul(int32_t a, int32_t b) { fx_calls++; (void)unprefixed_but_static; return fx_helper((int64_t)a * b); }\n" +
                          "uint32_t fx_sum(uint8_t *data, int32_t length) { uint32_t s = 0; while (length-- > 0) s += *data++; return s; }\n" +
                          "int32_t fx_tuned(void) { return FX_TUNE; }\n" +
                          "void fx_hello(const char *text) { fputs(text, stdout); }\n",
        ["common/fx_tune.h"] = "#define FX_TUNE 1\n",
        ["m68k/fx_tune.h"] = "#define FX_TUNE 68\n",
    };

    private const string FixGame = Prelude + @"using Fx;

public static unsafe class P
{
    static byte[] Data = { 1, 2, 3, 250 };

    [EntryPoint]
    public static void Main()
    {
        M.fx_hello(""from the package's C\n"");
        Dos.PutStr(M.Mul(3 << 16, 1 << 15) == 3 << 15 ? ""ok mul\n"" : ""FAIL mul\n"");
        fixed (byte* p = Data) Dos.PutStr(M.Sum(p, 4) == 256u ? ""ok sum\n"" : ""FAIL sum\n"");
        Dos.PutStr(M.Tuned() == 68 ? ""ok m68k header\n"" : ""FAIL m68k header\n"");
    }
}
";

    [Fact]
    public void Package_C_compiles_with_the_program_and_runs()
    {
        var lib = NativePackage("Sample.Fx", "fx_", FixC, FixLib);
        Assert.Empty(lib.Problems);
        var r = Build(FixGame, M68k(lib));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        // The program's C has the prototypes made from the C# declarations, and never includes a package header.
        var c = r.C!.Replace("\r\n", "\n");   // the prelude's lines end in the host's newline
        Assert.Contains("\nextern int32_t fx_mul(int32_t, int32_t);\n", c);
        Assert.Contains("\nextern uint32_t fx_sum(uint8_t *, int32_t);\n", c);
        Assert.Contains("\nextern void fx_hello(const char *);\n", c);
        Assert.Contains("\nextern int32_t fx_tuned(void);\n", c);
        Assert.DoesNotContain("fx_tune.h", c);
        Assert.DoesNotContain(PackageNatives.PrototypeHeader, c);
        // m68k: the common C file, the m68k header in place of the common one, and the prototype header.
        Assert.Equal(new[] { "esharp-native/Sample.Fx/esharp_natives.h", "esharp-native/Sample.Fx/fx.c", "esharp-native/Sample.Fx/fx_tune.h" },
            r.Native.Select(n => n.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "esharp-native/Sample.Fx/fx.c" }, r.Native.Where(n => n.Compile).Select(n => n.Path).ToArray());
        Assert.Equal("#define FX_TUNE 68\n", r.Native.Single(n => n.Path.EndsWith("fx_tune.h")).Text);
        var fx = r.Native.Single(n => n.Path.EndsWith("fx.c")).Text;
        Assert.StartsWith("#include \"esharp_natives.h\"\n#line 1 \"<package Sample.Fx 1.0.0>/native/common/fx.c\"\n#include \"fx_tune.h\"", fx);
        // Another target takes the common header.
        Assert.Equal("common", PackageNatives.Select(lib, "os4").Single(f => f.Name == "fx_tune.h").Folder);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGccAndNative(r.C!, r.Native);
        Assert.Equal(0, code);
        Assert.Equal("from the package's C\nok mul\nok sum\nok m68k header\n", output);
    }

    [Fact]
    public void A_C_definition_that_does_not_match_its_CSharp_declaration_does_not_compile()
    {
        var wrong = new Dictionary<string, string>(FixC) { ["common/fx.c"] = FixC["common/fx.c"].Replace("int32_t fx_mul(int32_t a, int32_t b)", "int32_t fx_mul(int32_t a, int16_t b)") };
        var r = Build(FixGame, M68k(NativePackage("Sample.Fx", "fx_", wrong, FixLib)));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (!HasGcc) return;
        var dir = Path.Combine(Path.GetTempPath(), "esharp-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var n in r.Native)
            {
                var path = Path.Combine(dir, n.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, n.Text);
            }
            var psi = new System.Diagnostics.ProcessStartInfo(Cc) { RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = dir };
            foreach (var a in new[] { "-std=gnu11", "-c", "-o", "fx.o", Path.Combine("esharp-native", "Sample.Fx", "fx.c") }) psi.ArgumentList.Add(a);
            using var cc = System.Diagnostics.Process.Start(psi)!;
            var err = cc.StandardError.ReadToEnd();
            cc.WaitForExit();
            Assert.NotEqual(0, cc.ExitCode);
            // gcc says "conflicting types", clang "conflicting types" too; both name the package file through the #line.
            Assert.Contains("conflicting types", err);
            Assert.Contains("<package Sample.Fx 1.0.0>/native/common/fx.c", err);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void A_variable_two_packages_define_through_a_macro_is_a_link_error()
    {
        // The macro hides the name from ES0120; the uninitialised definitions would be merged into one variable under -fcommon
        // (the default of gcc before 10, the pinned m68k gcc 6.5 among them). The build's PackageNatives.CFlags make it an error.
        const string c = "#define SHARED(n) int n\nSHARED(shared_count);\nint {0}touch(void) {{ return ++shared_count; }}\n";
        var a = NativePackage("Sample.A", "pa_", new Dictionary<string, string> { ["common/a.c"] = string.Format(c, "pa_") }, "namespace PA; public static class N { }");
        var b = NativePackage("Sample.B", "pb_", new Dictionary<string, string> { ["common/b.c"] = string.Format(c, "pb_") }, "namespace PB; public static class N { }");
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(a, b));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal("-fno-common", PackageNatives.CFlags);
        if (!HasGcc) return;
        var dir = Path.Combine(Path.GetTempPath(), "esharp-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var n in r.Native)
            {
                var path = Path.Combine(dir, n.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, n.Text);
            }
            File.WriteAllText(Path.Combine(dir, "main.c"), "int main(void) { return 0; }\n");
            (int Code, string Err) Link(string flag)
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Cc) { RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = dir };
                foreach (var x in new[] { "-std=gnu11", flag, "-o", "prog", "main.c" }.Concat(r.Native.Where(n => n.Compile).Select(n => n.Path.Replace('/', Path.DirectorySeparatorChar))))
                    psi.ArgumentList.Add(x);
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var err = proc.StandardError.ReadToEnd();
                proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return (proc.ExitCode, err);
            }
            var strict = Link(PackageNatives.CFlags);
            Assert.NotEqual(0, strict.Code);
            Assert.Contains("shared_count", strict.Err);
            // The hazard the flag removes: with common symbols the two definitions link as one variable, without a word.
            Assert.Equal(0, Link("-fcommon").Code);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void A_target_without_C_files_is_an_error_only_when_the_package_declares_externs_for_its_C()
    {
        const string main = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        // With an extern for its own C (a name with the prefix): no .c file for os4 is ES0119.
        var withExtern = NativePackage("Sample.Fx", "fx_", new Dictionary<string, string> { ["m68k/fx.c"] = "int fx_one(void) { return 1; }\n" },
            "using ESharp;\nnamespace Fx; public static class N { [Native(\"fx_one\")] public static extern int One(); }");
        Assert.True(Build(main, M68k(withExtern)).Success);
        var r = Build(main, Targets.Os4.ToOptions(packages: new[] { withExtern }, sdkVersion: "0.1.5"));
        var d = Assert.Single(r.Diagnostics, x => x.Id == "ES0119");
        Assert.Equal("Package 'Sample.Fx' 1.0.0 has native C, but none for target 'os4' (no .c file in its native/common or native/os4 folder); it has: m68k/fx.c", d.GetMessage());
        // C for m68k only, C# for the other targets (the extern is under #if M68K, as a library written so would have it): no error there.
        var m68kOnly = NativePackage("Sample.Fx", "fx_", new Dictionary<string, string> { ["m68k/fx.c"] = "int fx_one(void) { return 1; }\n" },
            "using ESharp;\nnamespace Fx; public static class N {\n#if M68K\n [Native(\"fx_one\")] public static extern int One();\n#else\n public static int One() => 1;\n#endif\n}");
        Assert.True(Build(main, M68k(m68kOnly)).Success);
        var os4 = Build(main, Targets.Os4.ToOptions(packages: new[] { m68kOnly }, sdkVersion: "0.1.5"));
        Assert.True(os4.Success, string.Join("\n", os4.Diagnostics));
        Assert.Empty(os4.Native.Where(n => n.Compile));
        // No extern at all: nothing needs the C either.
        var noExtern = NativePackage("Sample.Fx", "fx_", new Dictionary<string, string> { ["m68k/fx.c"] = "int fx_one(void) { return 1; }\n" },
            "namespace Fx; public static class N { }");
        Assert.DoesNotContain(Build(main, Targets.Os4.ToOptions(packages: new[] { noExtern }, sdkVersion: "0.1.5")).Diagnostics, x => x.Id == "ES0119");
    }

    [Fact]
    public void A_program_extern_for_a_package_C_function_is_refused()
    {
        var package = NativePackage("Sample.Fx", "fx_", FixC, FixLib);
        // The program's own declaration would get no prototype: an implicit declaration in the C. Refused, also for a name given by the method.
        const string own = Prelude + "public static unsafe class P {\n [Native(\"fx_mul\")] static extern int Mul(int a, int b);\n" +
                           " [Native] static extern void fx_hello(string s);\n [Native(\"es_fixdiv\")] static extern int Other(int a, int b);\n" +
                           " [EntryPoint] public static void Main() { } }";
        var r = Build(own, M68k(package));
        var refused = r.Diagnostics.Where(x => x.Id == "ES0123").Select(x => x.GetMessage()).ToList();
        Assert.Equal(2, refused.Count);
        Assert.Contains("The [Native] extern 'P.Mul' names 'fx_mul', a C function of package 'Sample.Fx' (native prefix 'fx_'); only the package declares its own C functions: call its C# method instead", refused);
        Assert.Contains(refused, m => m.Contains("'P.fx_hello' names 'fx_hello'"));
        Assert.False(r.Success);
        // Another package's extern for it is refused too; the package's own externs, and the program calling them, are fine.
        var other = NativePackage("Sample.Other", "ot_", new Dictionary<string, string> { ["common/o.c"] = "int ot_x(void) { return 0; }\n" },
            "using ESharp;\nnamespace Ot; public static class O { [Native(\"fx_mul\")] public static extern int Mul(int a, int b); }");
        Assert.Contains(Build(FixGame, M68k(package, other)).Diagnostics, x => x.Id == "ES0123" && x.GetMessage().Contains("'O.Mul'") && x.GetMessage().Contains("'Sample.Fx'"));
        Assert.DoesNotContain(Build(FixGame, M68k(package)).Diagnostics, x => x.Id == "ES0123");
    }

    [Fact]
    public void Native_file_names_that_differ_only_in_case_are_refused()
    {
        Assert.Null(PackageNatives.PathProblem("common/Tune.h"));
        Assert.Null(PackageNatives.PathProblem("common/tune.h", new[] { "common/tune.h", "m68k/other.h" }));
        Assert.Null(PackageNatives.PathProblem("m68k/tune.h", new[] { "common/tune.h", "m68k/tune.h" }));   // the same name: a target's file replaces the common one
        var problem = PackageNatives.PathProblem("m68k/tune.h", new[] { "common/Tune.h", "m68k/tune.h" });
        Assert.Equal("its name differs from 'common/Tune.h' only in case (one file on Windows and macOS)", problem);
        var csharp = "namespace Fx; public static class N { }";
        var c = new Dictionary<string, string> { ["common/fx.c"] = "int fx_a;\n", ["common/Tune.h"] = "#define A 1\n", ["m68k/tune.h"] = "#define A 2\n" };
        var carried = NativePackage("S", "fx_", c, csharp);
        Assert.Contains(carried.Problems, x => x.Contains("it carries 'esharp/native/") && x.Contains("only in case"));
        var listed = NativePackage("S", "fx_", new Dictionary<string, string> { ["common/fx.c"] = "int fx_a;\n" }, csharp, listed: new[] { "common/fx.c", "common/Tune.h", "m68k/tune.h" });
        Assert.Contains(listed.Problems, x => x.Contains("its manifest lists the C file") && x.Contains("only in case"));
    }

    [Fact]
    public void Exported_C_names_need_the_package_prefix_and_prefixes_may_not_overlap()
    {
        var c = "int fx_ok(void) { return 0; }\nstatic int helper(void) { return 1; }\nint counter = 0, fx_also;\nvoid set_up(void) { }\n" +
                "extern int elsewhere;\nint prototype_only(int);\ntypedef struct thing { int x; } thing_t;\nstruct thing global_thing;\n";
        var bad = NativePackage("Sample.Fx", "fx_", new Dictionary<string, string> { ["common/fx.c"] = c }, "namespace Fx; public static class N { }");
        var program = Prelude + "public static class P { [EntryPoint] public static void Main() { } }";
        var names = Build(program, M68k(bad)).Diagnostics.Where(d => d.Id == "ES0120").Select(d => d.GetMessage()).ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("Package 'Sample.Fx' 1.0.0: native/common/fx.c:3 defines 'counter', which does not start with the package's native prefix 'fx_'", names[0]);
        Assert.Contains(names, m => m.Contains("native/common/fx.c:4 defines 'set_up'"));
        Assert.Contains(names, m => m.Contains("native/common/fx.c:8 defines 'global_thing'"));

        var good = "int fx_x(void) { return 0; }\n";
        var a = NativePackage("Sample.A", "pk_", new Dictionary<string, string> { ["common/a.c"] = good.Replace("fx_", "pk_") }, "namespace PA; public static class N { }");
        var b = NativePackage("Sample.B", "pk_b_", new Dictionary<string, string> { ["common/b.c"] = good.Replace("fx_", "pk_b_") }, "namespace PB; public static class N { }");
        var other = NativePackage("Sample.C", "pkc_", new Dictionary<string, string> { ["common/c.c"] = good.Replace("fx_", "pkc_") }, "namespace PC; public static class N { }");
        var clash = Assert.Single(Build(program, M68k(a, b, other)).Diagnostics, d => d.Id == "ES0122");
        Assert.Equal("Packages 'Sample.A' (native prefix 'pk_') and 'Sample.B' (native prefix 'pk_b_') could export the same C names; give one of them another EsNativePrefix", clash.GetMessage());
        Assert.True(Build(program, M68k(a, other)).Success);
    }

    [Fact]
    public void Package_native_externs_take_numbers_strings_and_pointers_only()
    {
        const string lib = "using ESharp;\nnamespace Fx;\npublic struct V { public int X; }\npublic static unsafe class M {\n" +
                           "  [Native(\"fx_ref\")] public static extern int ByRef(ref int a);\n" +
                           "  [Native(\"fx_struct\")] public static extern int ByValue(V v);\n" +
                           "  [Native(\"fx_ok\")] public static extern void* Ok(V* v, nint n, bool b);\n" +
                           "  [Native(\"fx_fine\")] public static extern void* Fine(void* v, nint n, bool b, string s);\n" +
                           "  [Native(\"es_fixdiv\")] public static extern int EmitterOwn(int a, int b);\n}\n";
        var package = NativePackage("Sample.Fx", "fx_", new Dictionary<string, string> { ["common/fx.c"] = "int fx_dummy;\n" }, lib);
        var r = Build(Prelude + "public static class P { [EntryPoint] public static void Main() { } }", M68k(package));
        var messages = r.Diagnostics.Where(d => d.Id == "ES0121").Select(d => d.GetMessage()).ToList();
        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Fx': the [Native] extern 'M.ByRef' (fx_ref) has the parameter 'ref int a'"));
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Fx': the [Native] extern 'M.ByValue' (fx_struct) has the parameter 'Fx.V v'"));
        // A pointer to an E# struct has no C type the package's C could share either (pass void* and the fields it needs).
        Assert.Contains(messages, m => m.StartsWith("Package 'Sample.Fx': the [Native] extern 'M.Ok' (fx_ok) has the parameter 'Fx.V* v'"));
        // es_fixdiv does not start with the package's prefix: a C function of the emitter, as in program code, not checked here.
        Assert.DoesNotContain(messages, m => m.Contains("EmitterOwn"));
    }

    [Fact]
    public void Native_manifests_and_files_must_agree()
    {
        var csharp = "namespace Fx; public static class N { }";
        var c = new Dictionary<string, string> { ["common/fx.c"] = "int fx_a;\n" };
        Problems(NativePackage("S", "fx_", c, csharp, features: "\"Native\""), "declares native C but not the NativeC feature");
        Problems(Package("S", new Dictionary<string, string> { ["Lib.cs"] = csharp }, features: "\"NativeC\""), "declares the NativeC feature but no C files");
        Problems(NativePackage("S", "es_", c, csharp), "native prefix 'es_': es_ is E#'s own");
        Problems(NativePackage("S", "fx", c, csharp), "native prefix 'fx': a native prefix is a C name of at least 3 characters");
        Problems(NativePackage("S", "fx_", c, csharp, listed: new[] { "common/fx.c", "common/gone.c" }), "lists the C file 'common/gone.c', which the package does not carry");
        Problems(NativePackage("S", "fx_", c, csharp, listed: Array.Empty<string>()), "declares native C without any file");
        Problems(NativePackage("S", "fx_", new Dictionary<string, string>(c) { ["m68k/extra.c"] = "" }, csharp, listed: new[] { "common/fx.c" }),
            "carries the C file 'm68k/extra.c', which its manifest does not list");
        foreach (var (path, says) in new[]
                 {
                     ("m68k/libfx.a", "prebuilt code (object files, archives, libraries) is refused"),
                     ("common/fx.o", "prebuilt code"),
                     ("common/sub/fx.c", "one level deep"),
                     ("amigaos/fx.c", "C files go in native/common/ or native/<target>/"),
                     ("common/esharp_natives.h", "file names starting with esharp_ are the build's own"),
                 })
            Problems(NativePackage("S", "fx_", new Dictionary<string, string>(c) { [path] = "x" }, csharp, listed: new[] { "common/fx.c" }), says);
        Assert.Empty(NativePackage("S", "fx_", c, csharp).Problems);

        static void Problems(EsPackage p, string expected) =>
            Assert.True(p.Problems.Any(x => x.Contains(expected)), $"expected a problem with '{expected}', got: {string.Join(" | ", p.Problems)}");
    }

    [Fact]
    public void The_C_reading_finds_exported_definitions_only()
    {
        const string c = @"#include <stdint.h>
#define MACRO(x) int x;
/* int in_comment; */ // int in_line_comment;
static int s1 = 1;
static void s2(void) { int local; (void)local; }
int f1(int a, int b) { return a + b; }
unsigned long v1, v2 = 3, *v3;
int arr[4] = { 1, 2, 3, 4 };
const char *text = ""int not_a_name;"";
extern int declared;
int proto(int);
void (*callback_setter(void))(int) { return 0; }
typedef int myint;
struct tag { int field; };
enum { E1, E2 };
struct tag instance = { 1 };
__attribute__((unused)) int with_attr(void) { return 0; }
inline int inl(void) { return 0; }
int
multi_line
(void)
{
    return 0;
}
void (*hook)(int);
int (*table[4])(void);
int (*fptr_a)(int), (*fptr_b)(int) = 0, plain;
int knr(a, b)
    int a;
    char *b;
{ return a + *b; }
int after_knr = 1;
extern int ext_init = 5;
extern void (*ext_hook)(int);
UBYTE (*ptr_typedef_name)(int);
int (*nested_ret(int (*arg)(int)))(int) { return arg; }
static void (*static_hook)(int);
typedef void (*hook_t)(int);
int (*proto_ret(void))(int);
";
        var names = PackageNatives.ExportedSymbols(c).Select(s => $"{s.Name}:{s.Line}").ToList();
        Assert.Equal(new[] { "f1:6", "v1:7", "v2:7", "v3:7", "arr:8", "text:9", "callback_setter:12", "instance:16", "with_attr:17", "inl:18", "multi_line:20",
            "hook:25", "table:26", "fptr_a:27", "fptr_b:27", "plain:27", "knr:28", "after_knr:32", "ext_init:33", "ptr_typedef_name:35", "nested_ret:36" }, names);
    }

    [Fact]
    public void The_default_native_prefix_comes_from_the_package_id()
    {
        Assert.Equal("esharpsample_checksum_", PackageNatives.DefaultPrefix("ESharpSample.Checksum"));
        Assert.Equal("my_lib_x_", PackageNatives.DefaultPrefix("My..Lib-X"));
        Assert.Equal("lib_3d_", PackageNatives.DefaultPrefix("3D"));
        Assert.Null(PackageNatives.PrefixProblem(PackageNatives.DefaultPrefix("ESharpSample.Checksum")));
    }

    [Fact]
    public void The_build_prints_the_package_C_files_for_the_target()
    {
        var lib = NativePackage("Sample.Fx", "fx_", FixC, FixLib);
        var lines = ESharp.Cli.PackageStep.Describe(new[] { lib }, "m68k").ToList();
        Assert.Contains("  platform-level features: Native, NativeC", lines);
        Assert.Contains("  native C (prefix fx_; compiled into the program, runs with its privileges): native/common/fx.c, native/m68k/fx_tune.h", lines);
        Assert.Contains("  native C (prefix fx_; compiled into the program, runs with its privileges): native/common/fx.c, native/common/fx_tune.h",
            ESharp.Cli.PackageStep.Describe(new[] { lib }, "sdl"));
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
