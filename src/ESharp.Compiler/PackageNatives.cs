using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>A file the build writes next to the program's C and compiles with it (package C), path relative to the output folder.</summary>
public sealed record NativeOutput(string Path, string Text, bool Compile, string PackageId);

/// <summary>
/// C files and headers in E# library packages (issue #31, feature NativeC). The library project keeps them in
/// <c>native/common/</c> (every target) and <c>native/&lt;target&gt;/</c> (m68k, os4, morphos, aros, aros-i386, sdl), flat, as
/// <c>.c</c> and <c>.h</c> source; a target's file replaces the common file of the same name. The game's build writes the chosen
/// target's set into <c>esharp-native/&lt;package id&gt;/</c> under its output folder, one folder per package, and compiles each
/// <c>.c</c> as a translation unit of its own next to the program's C. So:
/// <list type="bullet">
/// <item>a package's headers are found only by its own C (<c>#include "x.h"</c> looks next to the including file; no -I is added),
/// and the program's C never includes them: it gets a prototype of each [Native] extern instead, made from the C# declaration;</item>
/// <item>the same prototypes are in <c>esharp_natives.h</c>, which each of the package's .c files includes first, so gcc reports
/// a C definition that does not match its C# declaration (conflicting types);</item>
/// <item>static functions and variables are private to their file; everything a package's C exports starts with its native
/// prefix (ES0120), prefixes of two packages may not overlap (ES0122), and none starts with <c>es_</c> (E#'s own C names are
/// <c>es_*</c>, <c>Es_*</c>, <c>ES_*</c>), so no two packages and no package and the program define the same C name. The check reads
/// the C source (<see cref="ExportedSymbols"/>); a name made by a macro escapes it, and then the linker's duplicate-symbol
/// error is what stops the build (package C is built with <see cref="CFlags"/>, so uninitialised variables are not merged).</item>
/// </list>
/// </summary>
public static class PackageNatives
{
    /// <summary>The header of prototypes each package .c file includes first (a package file may not use the name).</summary>
    public const string PrototypeHeader = "esharp_natives.h";

    /// <summary>The folder under the build's output folder that holds the package C, one sub-folder per package.</summary>
    public const string OutputFolder = "esharp-native";

    /// <summary>
    /// Compiler flags for a build with package C: <c>-fno-common</c>, so an uninitialised variable two translation units both
    /// define (<c>int x;</c>) is a duplicate-symbol error at link time, not one merged variable (gcc before 10, the pinned m68k
    /// gcc 6.5 among them, defaults to <c>-fcommon</c>). This is what stops a build when a name a macro makes escapes ES0120.
    /// </summary>
    public const string CFlags = "-fno-common";

    public static IReadOnlyList<string> Folders { get; } = new[] { "common" }.Concat(Targets.All.Select(t => t.Name)).ToArray();

    private static readonly Regex FileName = new(@"^[A-Za-z0-9_][A-Za-z0-9_.-]*\.[ch]$", RegexOptions.CultureInvariant);
    private static readonly Regex PrefixForm = new(@"^[A-Za-z][A-Za-z0-9_]*_$", RegexOptions.CultureInvariant);

    /// <summary>
    /// What is wrong with a native path "folder/name" (as in the manifest and under esharp/native/), or null. With
    /// <paramref name="all"/>, the package's paths: a file name that another path has in different case is refused too (the two are one
    /// file on Windows and macOS, and a target's header would silently replace the common one there).
    /// </summary>
    public static string? PathProblem(string path, IEnumerable<string>? all = null)
    {
        var parts = path.Split('/');
        if (parts.Length != 2 || !Folders.Contains(parts[0], StringComparer.Ordinal))
            return $"C files go in native/common/ or native/<target>/ ({string.Join(", ", Folders.Skip(1))}), one level deep";
        if (!FileName.IsMatch(parts[1]))
            return "only C source (.c) and headers (.h) with plain file names are accepted; prebuilt code (object files, archives, libraries) is refused (a package's only binaries are those the E# pack step builds from its sources, in esharp-bin/<target>/ beside the DLL)";
        if (parts[1].StartsWith("esharp_", StringComparison.OrdinalIgnoreCase))
            return "file names starting with esharp_ are the build's own (it writes " + PrototypeHeader + ")";
        if (all != null && all.FirstOrDefault(o => o.Split('/') is [_, var other] && other != parts[1] && string.Equals(other, parts[1], StringComparison.OrdinalIgnoreCase)) is { } clash)
            return $"its name differs from '{clash}' only in case (one file on Windows and macOS)";
        return null;
    }

    /// <summary>What is wrong with a native prefix, or null: a C identifier start that ends in '_', at least 3 characters, not es_.</summary>
    public static string? PrefixProblem(string prefix)
    {
        if (prefix.Length < 3 || !PrefixForm.IsMatch(prefix))
            return "a native prefix is a C name of at least 3 characters that starts with a letter and ends with '_' (such as mylib_)";
        if (prefix.StartsWith("es_", StringComparison.OrdinalIgnoreCase))
            return "es_ is E#'s own (es_*, Es_* and ES_* are the generated C's names)";
        return null;
    }

    /// <summary>The prefix a library gets when it sets no EsNativePrefix: its id in lower case, other characters as '_', then '_'.</summary>
    public static string DefaultPrefix(string id)
    {
        var sb = new StringBuilder();
        foreach (var c in id.ToLowerInvariant())
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_');
        var s = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (s.Length == 0 || !char.IsLetter(s[0])) s = "lib_" + s;
        return s.TrimEnd('_') + "_";
    }

    /// <summary>The files of the package for <paramref name="target"/>: the common ones, with a target file in place of a common one of the same name.</summary>
    public static IReadOnlyList<NativeFile> Select(EsPackage package, string? target)
    {
        var chosen = new SortedDictionary<string, NativeFile>(StringComparer.Ordinal);
        foreach (var f in package.Native.Where(f => f.Folder == "common")) chosen[f.Name] = f;
        if (target != null)
            foreach (var f in package.Native.Where(f => f.Folder == target)) chosen[f.Name] = f;
        return chosen.Values.ToList();
    }

    /// <summary>Checks on the native C of the packages for the target being built (ES0120, ES0122).</summary>
    public static void Check(CompileOptions options, List<Diagnostic> diagnostics)
    {
        var withNative = options.Packages.Where(p => p.Manifest?.Native != null && p.Problems.Count == 0).ToList();
        foreach (var p in withNative)
        {
            var prefix = p.Manifest!.Native!.Prefix;
            foreach (var f in Select(p, options.TargetName))
                foreach (var (name, line) in ExportedSymbols(f.Text).Where(s => !s.Name.StartsWith(prefix, StringComparison.Ordinal)))
                    diagnostics.Add(Diagnostic.Create(Descriptors.PackageNativeName, Location.None, p.Id, p.Version, $"native/{f.Path}:{line}", name, prefix));
        }
        for (var i = 0; i < withNative.Count; i++)
            for (var j = i + 1; j < withNative.Count; j++)
            {
                string a = withNative[i].Manifest!.Native!.Prefix, b = withNative[j].Manifest!.Native!.Prefix;
                if (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
                    diagnostics.Add(Diagnostic.Create(Descriptors.PackageNativePrefix, Location.None, withNative[i].Id, a, withNative[j].Id, b));
            }
    }

    /// <summary>
    /// ES0119: a package that declares [Native] externs for its own C (names with its prefix, in the C# compiled for this target)
    /// but has no .c file for the target. A package with C for some targets only and C# for the others has no such externs on the
    /// others (they are in <c>#if M68K</c>), and the build goes on.
    /// </summary>
    public static void CheckTargetHasC(CompileOptions options, IReadOnlyDictionary<EsPackage, List<string>> prototypes, List<Diagnostic> diagnostics)
    {
        foreach (var p in options.Packages.Where(p => p.Manifest?.Native != null && p.Problems.Count == 0 && prototypes.ContainsKey(p)))
            if (!Select(p, options.TargetName).Any(f => f.IsSource))
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageNativeTarget, Location.None, p.Id, p.Version, options.TargetName ?? "?",
                    string.Join(", ", p.Manifest!.Native!.Files)));
    }

    /// <summary>
    /// ES0123: a [Native] extern, in the program or in another package, whose C name starts with a package's native prefix. The
    /// prototype of a package's C function comes from the declaration in the package itself, so another declaration of it would be
    /// an implicit declaration in the program's C (and a second, possibly different, signature): it is refused, and the program calls
    /// the package's C# method instead.
    /// </summary>
    public static void CheckForeignExterns(CompileOptions options, Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, IEnumerable<SyntaxTree> programTrees,
        IReadOnlyDictionary<SyntaxTree, EsPackage> packageOf, List<Diagnostic> diagnostics)
    {
        // Every package with native C owns its prefix, also one with no C# tree (only C).
        var owners = options.Packages.Where(p => p.Manifest?.Native != null && p.Problems.Count == 0).ToList();
        if (owners.Count == 0) return;
        foreach (var tree in programTrees.Concat(packageOf.Keys.OrderBy(t => t.FilePath, StringComparer.Ordinal)))
        {
            packageOf.TryGetValue(tree, out var here);
            var model = compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not IMethodSymbol m || !m.IsExtern) continue;
                var attr = m.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "NativeAttribute");
                if (attr == null) continue;
                var cName = attr.ConstructorArguments is [{ Value: string n }] && n.Length > 0 ? n : m.Name;
                var owner = owners.FirstOrDefault(o => o != here && cName.StartsWith(o.Manifest!.Native!.Prefix, StringComparison.Ordinal));
                if (owner != null)
                    diagnostics.Add(Diagnostic.Create(Descriptors.PackageNativeForeign, decl.Identifier.GetLocation(),
                        m.ContainingType.Name + "." + m.Name, cName, owner.Id, owner.Manifest!.Native!.Prefix));
            }
        }
    }

    /// <summary>
    /// The C prototype of each [Native] extern a package with native C declares whose C name starts with the package's prefix
    /// (the functions of its C files; other [Native] externs name C functions of the emitter, as in program code), per package.
    /// A type C cannot be given is ES0121.
    /// </summary>
    public static Dictionary<EsPackage, List<string>> Prototypes(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
        IReadOnlyDictionary<SyntaxTree, EsPackage> packageOf, List<Diagnostic> diagnostics)
    {
        var result = new Dictionary<EsPackage, List<string>>();
        foreach (var (tree, package) in packageOf.OrderBy(k => k.Key.FilePath, StringComparer.Ordinal))
        {
            if (package.Manifest?.Native is not { } native || package.Problems.Count > 0) continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not IMethodSymbol m || !m.IsExtern) continue;
                var attr = m.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "NativeAttribute");
                if (attr == null) continue;
                var cName = attr.ConstructorArguments is [{ Value: string n }] && n.Length > 0 ? n : m.Name;
                if (!cName.StartsWith(native.Prefix, StringComparison.Ordinal)) continue;
                string? problem = null;
                if (m.IsGenericMethod) problem = "type parameters";
                var ret = m.ReturnsVoid ? "void" : CType(m.ReturnType);
                if (ret == null) problem ??= $"the return type '{m.ReturnType.ToDisplayString()}'";
                var ps = new List<string>();
                foreach (var p in m.Parameters)
                {
                    var t = p.RefKind == RefKind.None ? CType(p.Type) : null;
                    if (t == null) problem ??= $"the parameter '{(p.RefKind != RefKind.None ? p.RefKind.ToString().ToLowerInvariant() + " " : "")}{p.Type.ToDisplayString()} {p.Name}'";
                    ps.Add(t ?? "int");
                }
                if (problem != null)
                {
                    diagnostics.Add(Diagnostic.Create(Descriptors.PackageNativeSignature, decl.Identifier.GetLocation(), package.Id,
                        m.ContainingType.Name + "." + m.Name, cName, problem));
                    continue;
                }
                if (!result.TryGetValue(package, out var list)) result[package] = list = new List<string>();
                var proto = $"extern {ret} {cName}({(ps.Count == 0 ? "void" : string.Join(", ", ps))});";
                if (!list.Contains(proto)) list.Add(proto);
            }
        }
        return result;
    }

    /// <summary>The C type of a parameter or result of a package's own C function, as the emitter writes it, or null.</summary>
    private static string? CType(ITypeSymbol t)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return "bool";
            case SpecialType.System_Byte: return "uint8_t";
            case SpecialType.System_SByte: return "int8_t";
            case SpecialType.System_Int16: return "int16_t";
            case SpecialType.System_UInt16: return "uint16_t";
            case SpecialType.System_Int32: return "int32_t";
            case SpecialType.System_UInt32: return "uint32_t";
            case SpecialType.System_Int64: return "int64_t";
            case SpecialType.System_UInt64: return "uint64_t";
            case SpecialType.System_IntPtr: return "intptr_t";
            case SpecialType.System_UIntPtr: return "uintptr_t";
            case SpecialType.System_Char: return "uint16_t";
            case SpecialType.System_Single: return "float";
            case SpecialType.System_Double: return "double";
            case SpecialType.System_String: return "const char *";
        }
        if (t is IPointerTypeSymbol p)
        {
            var inner = p.PointedAtType.SpecialType == SpecialType.System_Void ? "void"
                : p.PointedAtType.SpecialType == SpecialType.System_String ? null : CType(p.PointedAtType);
            if (inner == null) return null;
            return inner.EndsWith("*", StringComparison.Ordinal) ? inner + "*" : inner + " *";
        }
        return null;
    }

    /// <summary>
    /// The files the build writes for the packages' C (<see cref="OutputFolder"/>/&lt;id&gt;/...): the chosen target's set, each .c
    /// starting with an include of <see cref="PrototypeHeader"/> and a #line back to its own name, and that header.
    /// </summary>
    public static List<NativeOutput> Outputs(IEnumerable<EsPackage> packages, string? target, IReadOnlyDictionary<EsPackage, List<string>> prototypes)
    {
        var result = new List<NativeOutput>();
        foreach (var p in packages.Where(p => p.Manifest?.Native != null))
        {
            var dir = OutputFolder + "/" + p.Id;
            var header = new StringBuilder();
            var guard = "ESHARP_NATIVES_" + Regex.Replace(p.Manifest!.Native!.Prefix.ToUpperInvariant(), "[^A-Z0-9_]", "_");
            header.Append("/* Generated by esharp build: the [Native] externs of package ").Append(p.Id).Append(' ').Append(p.Version)
                .Append(" as its C# declares them. Do not edit. */\n");
            header.Append("#ifndef ").Append(guard).Append("\n#define ").Append(guard).Append("\n#include <stdbool.h>\n#include <stddef.h>\n#include <stdint.h>\n");
            foreach (var proto in prototypes.TryGetValue(p, out var list) ? list : new List<string>()) header.Append(proto).Append('\n');
            header.Append("#endif\n");
            result.Add(new NativeOutput(dir + "/" + PrototypeHeader, header.ToString(), false, p.Id));
            foreach (var f in Select(p, target))
            {
                var display = $"{p.PathPrefix}native/{f.Path}".Replace("\\", "/").Replace("\"", "\\\"");
                var text = f.IsSource ? $"#include \"{PrototypeHeader}\"\n#line 1 \"{display}\"\n{f.Text}" : f.Text;
                if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += "\n";
                result.Add(new NativeOutput(dir + "/" + f.Name, text, f.IsSource, p.Id));
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ a light reading of C source

    private static readonly HashSet<string> NotNames = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for",
        "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed", "sizeof", "static", "struct",
        "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "_Bool", "_Complex", "_Noreturn", "_Thread_local",
        "_Alignas", "_Atomic", "__inline", "__inline__", "__restrict", "__restrict__", "__volatile__", "__const", "__extension__",
        "__signed__",
    };

    /// <summary>
    /// The functions and variables a C file defines with external linkage, with their line: definitions at file level that are
    /// not static (a function body, also with K&amp;R parameter declarations; a variable without extern, or an extern variable with an
    /// initializer), skipping prototypes, typedefs, struct, union and enum declarations and preprocessor lines. Declarators in
    /// parentheses are read for the name inside (<c>void (*hook)(int);</c>, <c>int (*table[4])(void);</c>, <c>int (*f(void))(int) { }</c>).
    /// A light reading, not a C parser: names a macro makes are not seen (the build links package C with <see cref="CFlags"/>, so a
    /// name two packages both define is a linker error).
    /// </summary>
    public static List<(string Name, int Line)> ExportedSymbols(string text)
    {
        var tokens = Tokens(text);
        var result = new List<(string, int)>();
        var stmt = new List<(string Text, int Line)>();
        var i = 0;
        while (i < tokens.Count)
        {
            var (tok, line) = tokens[i];
            if (tok == "{")
            {
                var close = Matching(tokens, i, "{", "}");
                // A function body: after the declarator's ')' or, K&R, after the parameter declarations (a ';' in the statement).
                var head = Head(stmt);
                var isFunction = !head.Any(s => s.Text == "=") && (stmt.Count > 0 && stmt[^1].Text == ")" || head.Count < stmt.Count)
                    && Declarator(head, 0, head.Count) is { IsFunction: true };
                if (isFunction)
                {
                    if (!head.Any(s => s.Text is "static" or "typedef") && Declarator(head, 0, head.Count) is { Name: { } fn }) result.Add(fn);
                    stmt.Clear();
                }
                else stmt.Add(("{}", line));
                i = close + 1;
                continue;
            }
            if (tok == ";")
            {
                if (KnrParameters(stmt)) { stmt.Add((";", line)); i++; continue; }
                if (!stmt.Any(s => s.Text is "static" or "typedef")) result.AddRange(VariableNames(stmt, stmt.Any(s => s.Text == "extern")));
                stmt.Clear();
                i++;
                continue;
            }
            // __attribute__((...)), __asm__("..."), __declspec(...): left out.
            if (tok is "__attribute__" or "__attribute" or "__asm__" or "__asm" or "asm" or "__declspec" && i + 1 < tokens.Count && tokens[i + 1].Text == "(")
            {
                i = Matching(tokens, i + 1, "(", ")") + 1;
                continue;
            }
            stmt.Add((tok, line));
            i++;
        }
        return result;
    }

    /// <summary>The statement up to its first ';' (a K&amp;R definition's head without its parameter declarations).</summary>
    private static List<(string Text, int Line)> Head(List<(string Text, int Line)> stmt)
    {
        var k = stmt.FindIndex(s => s.Text == ";");
        return k < 0 ? stmt : stmt.GetRange(0, k);
    }

    /// <summary>
    /// True when the statement so far is a K&amp;R function head (<c>int f(a, b)</c>, maybe with parameter declarations already read):
    /// a function declarator whose identifier list is followed by more at depth 0, which only parameter declarations can be.
    /// </summary>
    private static bool KnrParameters(List<(string Text, int Line)> stmt)
    {
        if (stmt.Any(s => s.Text == ";")) return true;
        if (stmt.Any(s => s.Text is "=" or "typedef")) return false;
        if (Declarator(stmt, 0, stmt.Count) is not { IsFunction: true, End: var end } || end >= stmt.Count || stmt[end].Text is "," or "(" or "[")
            return false;
        // The parameter list is an identifier list, and the declaration after it declares only names from it: int f(a, b) int a;
        var open = end - 1;
        for (var depth = 0; open >= 0; open--)
        {
            if (stmt[open].Text == ")") depth++;
            else if (stmt[open].Text == "(" && --depth == 0) break;
        }
        if (open < 0) return false;
        var list = stmt.GetRange(open + 1, end - 1 - open - 1);
        if (list.Count == 0 || list.Where((s, n) => n % 2 == 0 ? !IsName(s.Text) : s.Text != ",").Any()) return false;
        var ids = new HashSet<string>(list.Where(s => s.Text != ",").Select(s => s.Text), StringComparer.Ordinal);
        var names = VariableNames(stmt.GetRange(end, stmt.Count - end), false).ToList();
        return names.Count > 0 && names.All(n => ids.Contains(n.Item1));
    }

    /// <summary>The names a declaration at file level defines: one per declarator, skipping prototypes and bare struct/enum tags;
    /// with <paramref name="isExtern"/> only those with an initializer (<c>extern int x = 5;</c> is a definition).</summary>
    private static IEnumerable<(string, int)> VariableNames(List<(string Text, int Line)> stmt, bool isExtern)
    {
        var start = 0;
        for (var k = 0; k <= stmt.Count; k++)
        {
            if (k < stmt.Count)
            {
                var t = stmt[k].Text;
                if (t is "(" or "[") { k = Matching(stmt, k, t, t == "(" ? ")" : "]"); continue; }
                if (t != ",") continue;
            }
            // One declarator (the first with the declaration specifiers): stmt[start..k), its initializer after a '=' at depth 0.
            var end = k;
            var hasInit = false;
            for (var j = start; j < k; j++)
            {
                if (stmt[j].Text is "(" or "[") { j = Matching(stmt, j, stmt[j].Text, stmt[j].Text == "(" ? ")" : "]"); continue; }
                if (stmt[j].Text == "=") { end = j; hasInit = true; break; }
            }
            if (Declarator(stmt, start, end) is { Name: { } name, IsFunction: false } && (!isExtern || hasInit)) yield return name;
            start = k + 1;
        }
    }

    /// <summary>
    /// The name a declarator in <paramref name="stmt"/>[from..to) declares (the specifiers before it skipped: the last identifier
    /// that is not a struct/union/enum tag), whether it declares a function (its name directly followed by a parameter list), and
    /// where the declarator ends. A '(' is a grouping when nothing named comes before it or its first token is '*', '^' or '('
    /// (a parameter list cannot start so); the name is then the one inside, and parameter lists after the grouping belong to the
    /// pointed-to type: <c>void (*hook)(int)</c> is a variable, <c>int (*f(void))(int)</c> a function.
    /// </summary>
    private static (( string, int)? Name, bool IsFunction, int End)? Declarator(List<(string Text, int Line)> stmt, int from, int to)
    {
        (string, int)? name = null;
        var isFunction = false;
        var nameAt = -1;   // index of the name when it is directly in this level (a following '(' is then its parameter list)
        for (var k = from; k < to; k++)
        {
            var t = stmt[k].Text;
            if (t == "[") { k = Math.Min(Matching(stmt, k, "[", "]"), to - 1); nameAt = -1; continue; }
            if (t == "(")
            {
                var close = Math.Min(Matching(stmt, k, "(", ")"), to - 1);
                var first = k + 1 < close ? stmt[k + 1].Text : ")";
                var grouping = first is "*" or "^" or "(" || k == from || !(IsName(stmt[k - 1].Text) || stmt[k - 1].Text is ")" or "]");
                if (grouping && Declarator(stmt, k + 1, close) is { Name: { } inner } d)
                {
                    name = inner;
                    isFunction = d.IsFunction;
                    nameAt = -1;
                }
                else if (nameAt == k - 1)
                {
                    // The parameter list of the name: the declarator ends here (K&R parameter declarations may follow).
                    return (name, true, close + 1);
                }
                k = close;
                continue;
            }
            if (t == "{}") { nameAt = -1; continue; }
            if (IsName(t) && !(k > from && stmt[k - 1].Text is "struct" or "union" or "enum"))
            {
                name = stmt[k];
                nameAt = k;
                isFunction = false;
            }
        }
        return name == null ? null : (name, isFunction, to);
    }

    private static bool IsName(string t) => t.Length > 0 && (char.IsLetter(t[0]) || t[0] == '_') && !NotNames.Contains(t);

    private static int Matching(List<(string Text, int Line)> tokens, int open, string o, string c)
    {
        var depth = 0;
        for (var k = open; k < tokens.Count; k++)
        {
            if (tokens[k].Text == o) depth++;
            else if (tokens[k].Text == c && --depth == 0) return k;
        }
        return tokens.Count - 1;
    }

    /// <summary>Identifiers and punctuation with their lines; comments, string and character literals and preprocessor lines left out.</summary>
    private static List<(string Text, int Line)> Tokens(string s)
    {
        var tokens = new List<(string, int)>();
        var line = 1;
        var atLineStart = true;
        for (var i = 0; i < s.Length;)
        {
            var c = s[i];
            if (c == '\n') { line++; atLineStart = true; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#' && atLineStart)
            {
                // A preprocessor line, with its backslash continuations (and comments in it).
                while (i < s.Length && s[i] != '\n')
                {
                    if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '\n') { line++; i += 2; continue; }
                    if (s[i] == '\\' && i + 2 < s.Length && s[i + 1] == '\r' && s[i + 2] == '\n') { line++; i += 3; continue; }
                    if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        end = end < 0 ? s.Length : end + 2;
                        line += s.Substring(i, end - i).Count(ch => ch == '\n');
                        i = end;
                        continue;
                    }
                    i++;
                }
                continue;
            }
            atLineStart = false;
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                line += s.Substring(i, end - i).Count(ch => ch == '\n');
                i = end;
                continue;
            }
            if (c is '"' or '\'')
            {
                var q = c;
                i++;
                while (i < s.Length && s[i] != q && s[i] != '\n') i += s[i] == '\\' ? 2 : 1;
                i++;
                tokens.Add(("0", line));
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                tokens.Add((s.Substring(start, i - start), line));
                continue;
            }
            tokens.Add((c.ToString(), line));
            i++;
        }
        return tokens;
    }
}
