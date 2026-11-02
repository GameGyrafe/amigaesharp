using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>One place where the program reserves chip RAM. <see cref="Bytes"/> is -1 when the size is only known at run time.</summary>
public sealed record ChipEntry(string Kind, string What, long Bytes, string Location);

/// <summary>
/// Adds up the chip RAM a program asks for where the numbers are constants: static <c>[ChipRam]</c>
/// data, <c>new Arena(size, true)</c>, <c>new Pool&lt;T&gt;(capacity, true)</c> and <c>Mem.AllocChip(size)</c>.
/// Each call site counts once, however often it runs. Sizes computed at run time are listed but not summed.
/// Only meaningful on 68k, where chip RAM is a separate pool; PowerPC targets report nothing.
/// </summary>
internal static class ChipBudget
{
    public static List<ChipEntry> Analyze(CSharpCompilation compilation, IEnumerable<SyntaxTree> user, bool m68k)
    {
        var result = new List<ChipEntry>();
        if (!m68k) return result;

        foreach (var tree in user)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case VariableDeclaratorSyntax v when model.GetDeclaredSymbol(v) is IFieldSymbol { IsStatic: true } f &&
                                                         f.GetAttributes().Any(a => a.AttributeClass?.Name == "ChipRamAttribute"):
                        result.Add(new ChipEntry("static", $"{f.ContainingType.Name}.{f.Name}", StaticSize(f, v, model), Where(node)));
                        break;

                    case ObjectCreationExpressionSyntax oc when model.GetTypeInfo(oc).Type is INamedTypeSymbol t:
                        if (t.Name == "Arena" && t.ContainingNamespace?.ToDisplayString() == "ESharp.Platform")
                            Reservation(result, "arena", "new Arena", oc, model, 0, 1, _ => 1);
                        else if (t.Name == "Pool" && t.TypeArguments.Length == 1 && t.ContainingNamespace?.ToDisplayString() == "ESharp.Platform")
                        {
                            var item = SizeOf(t.TypeArguments[0]);
                            // Must match the layout in Pool.cs: free list (4) + item + in-use flag (1) per slot.
                            Reservation(result, "pool", $"new Pool<{t.TypeArguments[0].Name}>", oc, model, 0, 1, _ => item < 0 ? -1 : 4 + item + 1);
                        }
                        break;

                    case InvocationExpressionSyntax inv when model.GetSymbolInfo(inv).Symbol is IMethodSymbol { Name: "AllocChip" } m &&
                                                             m.ContainingType.Name == "Mem" && inv.ArgumentList.Arguments.Count == 1:
                    {
                        var size = Const(inv.ArgumentList.Arguments[0].Expression, model);
                        result.Add(new ChipEntry("alloc", "Mem.AllocChip", size ?? -1, Where(node)));
                        break;
                    }
                }
            }
        }
        return result;
    }

    public static long Total(IEnumerable<ChipEntry> entries) => entries.Where(e => e.Bytes >= 0).Sum(e => e.Bytes);

    private static void Reservation(List<ChipEntry> result, string kind, string what, ObjectCreationExpressionSyntax oc,
        SemanticModel model, int sizeArg, int chipArg, System.Func<long, long> perUnit)
    {
        var args = oc.ArgumentList?.Arguments;
        if (args is not { Count: 2 }) return;
        var chip = model.GetConstantValue(args.Value[chipArg].Expression);
        if (chip.HasValue && chip.Value is false) return; // fast RAM
        var count = Const(args.Value[sizeArg].Expression, model);
        var unit = perUnit(1);
        result.Add(new ChipEntry(kind, what, count == null || unit < 0 ? -1 : count.Value * unit, Where(oc)));
    }

    private static long? Const(ExpressionSyntax e, SemanticModel model)
    {
        var c = model.GetConstantValue(e);
        return c.HasValue && c.Value != null ? System.Convert.ToInt64(c.Value) : null;
    }

    private static string Where(SyntaxNode n)
    {
        var span = n.GetLocation().GetMappedLineSpan();
        return $"{span.Path}({span.StartLinePosition.Line + 1})";
    }

    private static long StaticSize(IFieldSymbol f, VariableDeclaratorSyntax v, SemanticModel model)
    {
        if (f.Type is not IArrayTypeSymbol arr) return SizeOf(f.Type);
        var elem = SizeOf(arr.ElementType);
        if (elem < 0) return -1;
        long? count = v.Initializer?.Value switch
        {
            ArrayCreationExpressionSyntax { Type.RankSpecifiers: { Count: 1 } r } ac when r[0].Sizes.Count == 1 && r[0].Sizes[0] is not OmittedArraySizeExpressionSyntax
                => Const(r[0].Sizes[0], model),
            ArrayCreationExpressionSyntax { Initializer: { } i } => i.Expressions.Count,
            ImplicitArrayCreationExpressionSyntax { Initializer: { } i } => i.Expressions.Count,
            InitializerExpressionSyntax i => i.Expressions.Count,
            _ => null,
        };
        return count == null ? -1 : count.Value * elem;
    }

    /// <summary>Size in bytes with m68k-amigaos-gcc layout (alignment capped at 2), or -1 if unknown.</summary>
    private static long SizeOf(ITypeSymbol t) => Layout(t).size;

    private static (long size, long align) Layout(ITypeSymbol t)
    {
        const int maxAlign = 2;
        static (long, long) Prim(long n) => (n, System.Math.Min(n, maxAlign));
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte: return Prim(1);
            case SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char: return Prim(2);
            case SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single or SpecialType.System_String: return Prim(4);
            case SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double: return Prim(8);
        }
        switch (t)
        {
            case IPointerTypeSymbol: return Prim(4);
            case INamedTypeSymbol { TypeKind: TypeKind.Enum } e: return Layout(e.EnumUnderlyingType!);
            case INamedTypeSymbol { Name: "Span" or "ReadOnlySpan" }: return Prim(8);
            case INamedTypeSymbol { TypeKind: TypeKind.Struct } s:
            {
                long offset = 0, align = 1;
                var storage = s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst && f.AssociatedSymbol == null)
                    .Concat(s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && f.AssociatedSymbol is IPropertySymbol));
                foreach (var f in storage)
                {
                    var (size, a) = f.IsFixedSizeBuffer
                        ? (Layout(((IPointerTypeSymbol)f.Type).PointedAtType).size * f.FixedSize, Layout(((IPointerTypeSymbol)f.Type).PointedAtType).align)
                        : Layout(f.Type);
                    if (size < 0) return (-1, 1);
                    offset = (offset + a - 1) / a * a + size;
                    if (a > align) align = a;
                }
                return ((offset + align - 1) / align * align, align);
            }
        }
        return (-1, 1);
    }
}
