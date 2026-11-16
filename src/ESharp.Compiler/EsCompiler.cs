using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ESharp.Compiler;

/// <summary>
/// The E# front end: parse with Roslyn, enforce the subset with the analyzer,
/// then lower and emit C.
/// </summary>
public static class EsCompiler
{
    public static CompileResult Compile(IEnumerable<SourceFile> sources, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: options.Defines);

        var userTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, parse, path: s.Path))
            .ToList();
        var libTrees = LibrarySources.Load()
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, parse, path: s.Path))
            .ToList();
        var userSet = new HashSet<SyntaxTree>(userTrees);

        var compilation = CSharpCompilation.Create(
            "ESharpProgram",
            userTrees.Concat(libTrees),
            References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable));

        var diagnostics = new List<Diagnostic>();

        // 1. Ordinary C# errors in user code.
        var csErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && IsUser(d, userSet)).ToList();
        diagnostics.AddRange(csErrors);
        if (!options.Defines.Contains("AMIGA")) diagnostics.AddRange(AmigaOnlyHints(csErrors, options.Defines.Contains("SDL") ? "sdl" : "this target"));

        // 2. Subset rules.
        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new GlobalOptionsProvider(options.AllowFloat));
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SubsetAnalyzer()), analyzerOptions);
        diagnostics.AddRange(withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
            // ES0100 (software 64-bit maths on a 68020) is about the 68020: sdl runs on a desktop CPU. (The IDE analyzer cannot know the target.)
            .Where(d => IsUser(d, userSet) && !(d.Id == "ES0100" && options.Defines.Contains("SDL"))));

        // 3. Entry point rules.
        CheckEntryPoint(compilation, userSet, diagnostics);

        // Chip RAM budget (constant sizes only).
        var chip = ChipBudget.Analyze(compilation, userSet, options.Defines.Contains("M68K"));
        if (options.ChipBudget is { } budget && ChipBudget.Total(chip) > budget)
            diagnostics.Add(Diagnostic.Create(Descriptors.ChipBudgetExceeded, Location.None, ChipBudget.Total(chip), budget));

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new CompileResult(null, Sorted(diagnostics)) { Chip = chip };

        // 4. Monomorphise generics, then check the expanded program still compiles.
        var expanded = Monomorphizer.Expand(compilation, userSet, parse);
        compilation = expanded.Compilation;
        userSet = expanded.User;
        diagnostics.AddRange(expanded.Errors);
        diagnostics.AddRange(compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)); // library copies too: a private type argument breaks them
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new CompileResult(null, Sorted(diagnostics)) { Chip = chip };

        // C keywords and predefined macros used as C# names (a parameter called 'auto') get a C name of their own.
        (compilation, userSet) = CKeywords.Apply(compilation, userSet);

        // 5. Emit C.
        var emitter = new CEmitter(compilation, userSet, diagnostics, options.BoundsChecks);
        var c = emitter.Emit();
        return new CompileResult(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? null : c,
            Sorted(diagnostics)) { Chip = chip };
    }

    /// <summary>
    /// Platform classes that exist only on Amiga targets (and in the dotnet host fake): on sdl they are not compiled, so using
    /// one is an unknown name. Such an error gets an ES0102 next to it that says why.
    /// </summary>
    public static readonly IReadOnlyList<string> AmigaOnlyTypes = new[]
    {
        "Gui", "GtWindow", "GtButton", "GtString", "GtText", "GtGadget", "GuiEvent", "GuiEventKind", "IGuiHandler", "GadTools",
        "Asl", "AslLib", "Interrupts", "Iff", "IffFile", "IffParse", "Fonts", "FontHandle", "DiskFontLib", "Joystick", "LowLevel",
        "Exec", "Intuition", "Graphics", "CyberGfx", "Utility", "TimerDevice", "Keymap", "Layout", "DosLib",
        "BoopsiObject", "BoopsiWindow", "BoopsiEvent", "BoopsiEventKind", "Hook", "HookFunc", "IntAttr", "BoolAttr", "StrAttr", "PtrAttr",
        "ObjAttr", "HookAttr", "Ga", "Pga", "Stringa", "Ica", "Ia", "Om",
    };

    private static IEnumerable<Diagnostic> AmigaOnlyHints(IEnumerable<Diagnostic> errors, string target)
    {
        HashSet<string>? amigaMembers = null;   // "Type.Member" of the platform sources as an Amiga target sees them (read only if needed)
        foreach (var d in errors)
        {
            var text = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            if (d.Id == "CS0117")
            {
                // 'Video' does not contain a definition for 'Window': a member that exists only in the Amiga build of a class that sdl has too.
                var m = System.Text.RegularExpressions.Regex.Match(text, @"'(?:[\w.]*\.)?(\w+)' does not contain a definition for '(\w+)'");
                if (!m.Success) continue;
                amigaMembers ??= AmigaPlatformMembers();
                var key = m.Groups[1].Value + "." + m.Groups[2].Value;
                if (amigaMembers.Contains(key)) yield return Diagnostic.Create(Descriptors.AmigaOnly, d.Location, key, target);
                continue;
            }
            if (d.Id is not ("CS0103" or "CS0246" or "CS0234")) continue;
            var name = AmigaOnlyTypes.FirstOrDefault(n => text.Contains("'" + n + "'", StringComparison.Ordinal));
            if (name != null) yield return Diagnostic.Create(Descriptors.AmigaOnly, d.Location, name, target);
        }
    }

    private static HashSet<string> AmigaPlatformMembers()
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "AMIGA", "M68K", "AMIGAOS3" });
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var src in LibrarySources.Load())
        {
            var root = CSharpSyntaxTree.ParseText(src.Text, parse, path: src.Path).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
                foreach (var member in type.Members)
                {
                    switch (member)
                    {
                        case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax md: result.Add(type.Identifier.Text + "." + md.Identifier.Text); break;
                        case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax pd: result.Add(type.Identifier.Text + "." + pd.Identifier.Text); break;
                        case Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax fd:
                            foreach (var v in fd.Declaration.Variables) result.Add(type.Identifier.Text + "." + v.Identifier.Text);
                            break;
                    }
                }
        }
        return result;
    }

    private static ImmutableArray<Diagnostic> Sorted(List<Diagnostic> diagnostics) =>
        diagnostics
            .OrderBy(d => d.Location.SourceTree?.FilePath ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();

    private static bool IsUser(Diagnostic d, HashSet<SyntaxTree> user) =>
        d.Location.SourceTree == null || user.Contains(d.Location.SourceTree);

    private static void CheckEntryPoint(CSharpCompilation compilation, HashSet<SyntaxTree> user, List<Diagnostic> diagnostics)
    {
        var entries = CEmitter.FindEntryPoints(compilation, user).ToList();
        if (entries.Count == 0)
            diagnostics.Add(Diagnostic.Create(Descriptors.NoEntryPoint, Location.None));
        else if (entries.Count > 1)
            foreach (var e in entries.Skip(1))
                diagnostics.Add(Diagnostic.Create(Descriptors.MultipleEntryPoints, e.Locations[0]));
        foreach (var e in entries)
        {
            var ok = e.IsStatic && e.ReturnType.SpecialType is SpecialType.System_Void or SpecialType.System_Int32 && !e.IsGenericMethod
                && (e.Parameters.Length == 0 || CEmitter.IsArgsEntry(e));
            if (!ok) diagnostics.Add(Diagnostic.Create(Descriptors.BadEntryPoint, e.Locations[0], e.ToDisplayString()));
        }
    }

    private static IEnumerable<MetadataReference> References()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib", "System.Runtime", "System.Console", "System.Linq",
            "System.Collections", "System.Runtime.InteropServices", "System.Threading.Tasks",
            "System.Reflection", "System.Memory", "System.IO",
        };
        return tpa.Split(Path.PathSeparator)
            .Where(p => wanted.Contains(Path.GetFileNameWithoutExtension(p)))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
    }

    private sealed class GlobalOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Options _global;

        public GlobalOptionsProvider(bool allowFloat) => _global = new Options(allowFloat);

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly bool _allowFloat;
            public Options(bool allowFloat) => _allowFloat = allowFloat;

            public override bool TryGetValue(string key, out string value)
            {
                if (key == SubsetAnalyzer.AllowFloatProperty)
                {
                    value = _allowFloat ? "true" : "false";
                    return true;
                }
                if (key == SubsetAnalyzer.LibrarySourcesProperty)
                {
                    value = LibrarySources.PathPrefixes;
                    return true;
                }
                value = "";
                return false;
            }
        }
    }
}
