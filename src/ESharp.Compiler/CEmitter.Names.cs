using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

// Name collisions at member level, and calls of Equals / ReferenceEquals (small, separate from the main emitter to ease merges).
internal sealed partial class CEmitter
{
    /// <summary>
    /// C has one flat namespace for functions, static fields, enum members and type names, and they are all built by joining names
    /// with '_': class A_B with member C and class A with member B_C both become Es_A_B_C. Two different declarations that meet are
    /// reported (ES0099) on the one declared later, rather than emitting C that fails or silently shares a variable.
    /// </summary>
    private void CheckMemberNameCollisions(IReadOnlyList<INamedTypeSymbol> types)
    {
        var names = new List<(string Name, ISymbol Symbol)>();
        foreach (var t in types)
        {
            names.Add((TypeName(t), t));
            foreach (var member in t.GetMembers())
            {
                switch (member)
                {
                    case IFieldSymbol { IsStatic: true, IsConst: false, AssociatedSymbol: null } sf:
                        names.Add((FieldName(sf), sf));
                        break;
                    case IFieldSymbol { IsConst: true, ContainingType.TypeKind: TypeKind.Enum } ef:
                        names.Add((FieldName(ef), ef));
                        break;
                    case IPropertySymbol { IsStatic: true } p when IsAutoProperty(p):
                        names.Add(($"{TypeName(p.ContainingType)}_{p.Name}", p));
                        break;
                    case IMethodSymbol m when !m.IsGenericMethod && !m.IsExtern
                        && m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.Constructor
                            or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.StaticConstructor
                        && DeclaresBody(m) && !HasAttribute(m, "AmigaLibAttribute") && !HasAttribute(m, "NativeAttribute"):
                        names.Add((FuncName(m), m));
                        break;
                }
            }
        }
        foreach (var group in names.GroupBy(n => n.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var first = group.First().Symbol;
            foreach (var (_, other) in group.Skip(1))
            {
                if (SymbolEqualityComparer.Default.Equals(first, other)) continue;
                var at = other.DeclaringSyntaxReferences.FirstOrDefault(r => _user.Contains(r.SyntaxTree))
                    ?? first.DeclaringSyntaxReferences.FirstOrDefault(r => _user.Contains(r.SyntaxTree));
                if (at == null) continue;
                Unsupported(at.GetSyntax(),
                    $"C name '{group.Key}' of '{other.ToDisplayString()}' is already used by '{first.ToDisplayString()}'; rename one of them");
            }
        }
    }

    /// <summary>True when some class of the program declares its own Equals(object): then a call of Equals on a class reference may run user code.</summary>
    private bool AnyUserEqualsOverride() =>
        _compilation.SyntaxTrees.Where(_user.Contains).Any(tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Any(d => d.Identifier.Text == "Equals" && Model(tree).GetDeclaredSymbol(d) is IMethodSymbol { Parameters.Length: 1 } m && m.Parameters[0].Type.SpecialType == SpecialType.System_Object));

    /// <summary>
    /// object.Equals(a, b), a.Equals(b) and object.ReferenceEquals(a, b) on class references, in a program where no class declares
    /// Equals(object): reference equality (what object.Equals does). Everything else is ES0099 with the reason.
    /// </summary>
    private string ObjectEquals(InvocationExpressionSyntax inv, IMethodSymbol m)
    {
        var args = inv.ArgumentList.Arguments;
        ExpressionSyntax? a, b;
        if (m.IsStatic && args.Count == 2) { a = args[0].Expression; b = args[1].Expression; }
        else if (!m.IsStatic && args.Count == 1 && inv.Expression is MemberAccessExpressionSyntax ma) { a = ma.Expression; b = args[0].Expression; }
        else if (!m.IsStatic && args.Count == 1) { a = null; b = args[0].Expression; }   // Equals(x) inside an instance method: this
        else return Unsupported(inv, $"this form of {m.Name}");

        bool IsRef(ExpressionSyntax x) => IsNullConstant(x) || Model(x).GetTypeInfo(x).Type is INamedTypeSymbol t && IsHeapClass(t);
        if (a != null && Model(a).GetTypeInfo(a).Type is { IsValueType: true })
            return Unsupported(inv, "Equals on a struct or enum (ValueType.Equals compares the fields by reflection); compare the fields or write ==");
        if (m.ContainingType.SpecialType != SpecialType.System_Object)
            return Unsupported(inv, $"a call of a user-defined {m.Name}(object); E# has no object type: write an Equals that takes your own class, or compare with ==");
        if (m.Name == "Equals" && AnyUserEqualsOverride())
            return Unsupported(inv, "object.Equals in a program where a class declares Equals(object) (E# has no object type to pass it); compare with ==");
        if ((a != null && !IsRef(a)) || !IsRef(b))
            return Unsupported(inv, $"{m.Name} with a value that is not a class reference (boxing)");
        var left = a == null ? "this" : Expr(a, top: true);
        return $"((const void *)({left}) == (const void *)({Expr(b, top: true)}))";
    }
}
