using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// C# names that mean something else in the generated C: C keywords that C# allows as identifiers (<c>auto</c>, <c>register</c>,
/// <c>signed</c>, <c>unsigned</c>, <c>inline</c>, ..., and C# keywords written with <c>@</c>, such as <c>@long</c>) and macros that
/// the C compilers or the Amiga headers predefine (<c>amiga</c>, <c>mc68000</c>, <c>TRUE</c>, ...). Locals, parameters and fields keep
/// their own name in C, so such a name is renamed before the C is written: <c>auto</c> becomes <c>es_auto</c>. Types, methods,
/// static fields and enum members get a type prefix in C (<c>Es_Type_auto</c>) and are left alone, so an enum member's name, which
/// <c>ToString</c> and interpolation print, stays as written.
/// </summary>
internal static class CKeywords
{
    public static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        // C11 keywords (the ones that are not also C# keywords can be plain C# identifiers; the others need '@')
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for",
        "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed", "sizeof", "static", "struct",
        "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
        "_Alignas", "_Alignof", "_Atomic", "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local",
        // GNU C and C23 words, and what <stdbool.h> and <stddef.h> define
        "asm", "typeof", "alignas", "alignof", "constexpr", "nullptr", "static_assert", "thread_local", "bool", "true", "false", "NULL", "offsetof",
        // predefined by the cross compilers (m68k-amigaos, ppc, x86_64-aros) and the host gcc in gnu11 mode, or by the Amiga headers
        "amiga", "AMIGA", "amigaos", "MCH_AMIGA", "mc68000", "mc68010", "mc68020", "mc68030", "mc68040", "mc68060", "m68k",
        "unix", "linux", "i386", "TRUE", "FALSE",
    };

    /// <summary>The C name used for a reserved one.</summary>
    public static string Rename(string name) => "es_" + name;

    /// <summary>The compilation with every local, parameter, label and instance member named by a reserved word renamed (trees with none are kept).</summary>
    public static (CSharpCompilation Compilation, HashSet<SyntaxTree> User) Apply(CSharpCompilation compilation, HashSet<SyntaxTree> user)
    {
        var newUser = new HashSet<SyntaxTree>();
        foreach (var tree in compilation.SyntaxTrees.ToList())
        {
            var root = tree.GetRoot();
            if (!root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && Reserved.Contains(t.ValueText)))
            {
                if (user.Contains(tree)) newUser.Add(tree);
                continue;
            }
            var model = compilation.GetSemanticModel(tree);
            var renamed = new Renamer(model).Visit(root);
            var fresh = tree.WithRootAndOptions(renamed, tree.Options);
            compilation = compilation.ReplaceSyntaxTree(tree, fresh);
            if (user.Contains(tree)) newUser.Add(fresh);
        }
        return (compilation, newUser);
    }

    private static bool Renamed(ISymbol? s) => s switch
    {
        ILocalSymbol or IParameterSymbol or ILabelSymbol or IRangeVariableSymbol => true,
        IFieldSymbol f => !f.IsStatic && !f.IsConst && f.ContainingType?.TypeKind != TypeKind.Enum,
        IPropertySymbol p => !p.IsStatic && !p.IsIndexer,
        _ => false,
    };

    private sealed class Renamer : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        public Renamer(SemanticModel model) => _model = model;

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken) || !Reserved.Contains(token.ValueText) || token.Parent == null) return token;
            var parent = token.Parent;
            ISymbol? symbol = parent is IdentifierNameSyntax id
                ? _model.GetSymbolInfo(id).Symbol ?? _model.GetSymbolInfo(id).CandidateSymbols.FirstOrDefault()
                : _model.GetDeclaredSymbol(parent);
            if (!Renamed(symbol)) return token;
            // Only E# source is renamed; a member of a .NET type keeps its name (E# reports a use of it anyway).
            if (symbol is IFieldSymbol or IPropertySymbol && !symbol.Locations.Any(l => l.IsInSource)) return token;
            return SyntaxFactory.Identifier(token.LeadingTrivia, Rename(token.ValueText), token.TrailingTrivia);
        }
    }
}
