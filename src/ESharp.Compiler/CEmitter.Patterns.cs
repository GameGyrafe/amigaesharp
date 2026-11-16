using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ESharp.Compiler;

/// <summary>
/// Recursive patterns (positional, property, type with a designation), list patterns, and <c>switch</c> statements with pattern
/// labels. Each pattern becomes a C condition on a subject that is already a temporary or a side-effect-free expression; a value
/// that takes a call to get (a Deconstruct, a property getter, a Vec indexer) is read once into a temporary inside a GNU
/// statement expression. Pattern variables are plain locals declared before the test (see <see cref="PatternLocalDecls"/>).
/// </summary>
internal sealed partial class CEmitter
{
    private List<INamedTypeSymbol> _heapClasses = new();

    /// <summary>True when <c>x is T</c> with x of type <paramref name="from"/> is a test E# can make: class to class, or interface to class.</summary>
    private static bool CanTypeTest(ITypeSymbol? from, ITypeSymbol? target) =>
        target is INamedTypeSymbol t && IsHeapClass(t) && from is INamedTypeSymbol f && (IsHeapClass(f) || IsInterfaceType(f));

    /// <summary>The C condition of <c>x is T</c> (see <see cref="CanTypeTest"/>); <paramref name="subject"/> is a simple C expression.</summary>
    private string TypeIs(string subject, ITypeSymbol from, INamedTypeSymbol target, SyntaxNode at) =>
        IsInterfaceType(from) ? IfaceIs(subject, (INamedTypeSymbol)from, target) : ClassIs(subject, (INamedTypeSymbol)from, target, at);

    /// <summary>The subject seen as the class <paramref name="target"/>, once <see cref="TypeIs"/> has held.</summary>
    private static string TypeAs(string subject, ITypeSymbol from, INamedTypeSymbol target) =>
        IsInterfaceType(from) ? $"(({TypeName(target)} *){subject}.obj)" : $"(({TypeName(target)} *){subject})";

    /// <summary>
    /// <c>x is T</c> for an interface value x and a class T. An interface value is the object and the function table of the class it was
    /// converted from, and every class has its own table, so the table says the class: x is a T when its table is the one of T or of a class
    /// derived from T. When the table is of a base class of T (the object was converted while typed as the base), the object's own type id
    /// decides, which such a class has because it has a subclass. A null value has no table and is never a T.
    /// </summary>
    private string IfaceIs(string subject, INamedTypeSymbol iface, INamedTypeSymbol target)
    {
        var tests = new List<string>();
        foreach (var c in _heapClasses.Where(c => c.AllInterfaces.Contains(iface, SymbolEqualityComparer.Default)))
        {
            var table = $"{subject}.vt == &{VtableName(iface, c)}";
            if (IsBaseOrSame(c, target)) tests.Add(table);
            else if (IsBaseOrSame(target, c) && IsPoly(target))
                tests.Add($"({table} && es_is((const struct ES_Type *)(({TypeName(c)} *){subject}.obj)->{VtPath(c)}, {TypeIdOf(target)}))");
        }
        return tests.Count == 0 ? "0" : "(" + string.Join(" || ", tests) + ")";
    }

    /// <summary>A C condition that a reference-typed subject is not null, or null for a value type (nothing to test).</summary>
    private static string? NotNullTest(string subject, ITypeSymbol type)
    {
        if (subject.StartsWith('"')) return null;   // a string literal is never null (and gcc's -Waddress says so)
        if (IsInterfaceType(type)) return $"({subject}.obj != NULL)";
        if (IsHeapClass(type) || type.SpecialType == SpecialType.System_String) return $"({subject} != NULL)";
        if (type.TypeKind == TypeKind.Delegate) return $"({subject}.fn != NULL)";
        return null;
    }

    /// <summary>A constant (or, as the parser reads it, a type name) compared with the subject: a constant pattern or a <c>case</c> label.</summary>
    private string ConstantTest(ExpressionSyntax value, SyntaxNode at, string subject, ITypeSymbol type)
    {
        if (Model(value).GetSymbolInfo(value).Symbol is INamedTypeSymbol tpType)
        {
            // 'not Rect' and 'case Rect:': the parser reads a type name there as a constant.
            if (SymbolEqualityComparer.Default.Equals(tpType, type)) return NotNullTest(subject, type) ?? "1";
            if (CanTypeTest(type, tpType)) return $"({TypeIs(subject, type, tpType, at)})";
            return Unsupported(at, "type test");
        }
        if (value is LiteralExpressionSyntax nullLit && nullLit.IsKind(SyntaxKind.NullLiteralExpression))
            return IsInterfaceType(type) ? $"({subject}.obj == NULL)" : type.TypeKind == TypeKind.Delegate ? $"({subject}.fn == NULL)" : $"({subject} == NULL)";
        if (type.SpecialType == SpecialType.System_String)
            return IsNullConstant(value) ? $"({subject} == NULL)" : StrEq(subject, Expr(value, top: true));   // "x" matches by the characters
        if (Model(value).GetTypeInfo(value).Type?.SpecialType == SpecialType.System_String)
            return Unsupported(at, "a string constant pattern on a value that is not typed string");
        return $"({subject} == {Expr(value)})";
    }

    /// <summary><c>T { ... }</c>, <c>T(...)</c>, <c>(...)</c>, <c>{ ... } name</c>: type test, then deconstruction, then properties, then the designation.</summary>
    private string RecursivePatternTest(RecursivePatternSyntax rec, string subject, ITypeSymbol type)
    {
        var parts = new List<string>();
        var subj = subject;
        var st = type;
        var rt = rec.Type != null ? Model(rec).GetTypeInfo(rec.Type).Type : null;
        if (rt != null && !SymbolEqualityComparer.Default.Equals(rt, type))
        {
            if (CanTypeTest(type, rt) && rt is INamedTypeSymbol rc)
            {
                // A class test, then the tests on the object seen as the derived class.
                parts.Add(TypeIs(subject, type, rc, rec));
                subj = TypeAs(subject, type, rc);
                st = rc;
            }
            else return Unsupported(rec, "type test");
        }
        else if (NotNullTest(subj, st) is { } nn) parts.Add(nn);   // '{ }' and friends never match null

        if (rec.PositionalPatternClause is { } pos)
        {
            var subs = pos.Subpatterns;
            parts.Add(Deconstruction(rec, subs.Count, subj, st, (i, e, t) => PatternTest(subs[i].Pattern, e, t)));
        }
        if (rec.PropertyPatternClause is { } props)
            foreach (var sub in props.Subpatterns) parts.Add(PropertySubpattern(sub, subj, st));
        if (rec.Designation != null) parts.Add(BindDesignation(rec.Designation, subj, st, rec));
        return parts.Count == 0 ? "1" : "(" + string.Join(" && ", parts) + ")";
    }

    /// <summary>A designation that may be a parenthesized list (<c>var (a, b)</c>), which deconstructs the subject.</summary>
    private string BindDesignation(VariableDesignationSyntax designation, string subject, ITypeSymbol type, SyntaxNode at)
    {
        if (designation is ParenthesizedVariableDesignationSyntax list)
            return Deconstruction(at, list.Variables.Count, subject, type, (i, e, t) => BindDesignation(list.Variables[i], e, t, at));
        return Bind(designation, subject, at);
    }

    /// <summary>
    /// The parts of <paramref name="subject"/> as a positional pattern sees them: the fields of a tuple, or the out parameters of an
    /// instance <c>Deconstruct</c> (called once, into temporaries). <paramref name="element"/> gives the test for each part.
    /// </summary>
    private string Deconstruction(SyntaxNode at, int count, string subject, ITypeSymbol type, Func<int, string, ITypeSymbol, string> element)
    {
        var parts = new List<string>();
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            if (tuple.TupleElements.Length != count) return Unsupported(at, "positional pattern with a different number of parts than the tuple");
            for (var i = 0; i < count; i++) parts.Add(element(i, $"({subject}).Item{i + 1}", tuple.TupleElements[i].Type));
            return parts.Count == 0 ? "1" : "(" + string.Join(" && ", parts) + ")";
        }
        var m = (Model(at).GetOperation(at) as IRecursivePatternOperation)?.DeconstructSymbol as IMethodSymbol;
        // 'var (a, b)' has no recursive pattern operation of its own: look the instance Deconstruct up by its number of parts.
        m ??= type.GetMembers("Deconstruct").OfType<IMethodSymbol>().Where(d => !d.IsStatic && d.Parameters.Length == count).Take(2).ToList() is [var only] ? only : null;
        if (m == null) return Unsupported(at, "positional pattern without a Deconstruct method (or on ITuple)");
        if (m.IsExtensionMethod || m.IsStatic) return Unsupported(at, "positional pattern through an extension Deconstruct; make it an instance method");
        if (m.IsGenericMethod || m.ReturnsByRef || m.Parameters.Length != count || m.Parameters.Any(p => p.RefKind != RefKind.Out))
            return Unsupported(at, "this Deconstruct method (only void Deconstruct(out ...) with one out parameter per part)");
        if (IsInterfaceType(type) || IsInterfaceType(m.ContainingType) || type.TypeKind == TypeKind.TypeParameter)
            return Unsupported(at, "positional pattern on an interface");

        var id = _patternTemp++;
        var sb = new StringBuilder("({ ");
        string receiver;
        if (IsRef(type)) receiver = subject;
        else if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct })
        {
            // A struct method takes its receiver by address: Deconstruct a copy, so any subject expression works.
            sb.Append($"{Decl(type, "es_dr" + id, at)} = {subject}; ");
            receiver = "&es_dr" + id;
        }
        else return Unsupported(at, "positional pattern on this type");
        var outs = new List<string>();
        for (var i = 0; i < count; i++)
        {
            sb.Append($"{Decl(m.Parameters[i].Type, $"es_d{id}_{i}", at)}; ");
            outs.Add($"&es_d{id}_{i}");
        }
        sb.Append(InstanceCall(m, receiver, outs)).Append("; ");
        for (var i = 0; i < count; i++) parts.Add(element(i, $"es_d{id}_{i}", m.Parameters[i].Type));
        sb.Append(parts.Count == 0 ? "1" : string.Join(" && ", parts)).Append("; })");
        return sb.ToString();
    }

    /// <summary><c>Name: pattern</c> or the extended form <c>A.B: pattern</c> (which tests A for null on the way, as C# does).</summary>
    private string PropertySubpattern(SubpatternSyntax sub, string subject, ITypeSymbol type)
    {
        var names = new List<SimpleNameSyntax>();
        if (sub.NameColon != null) names.Add(sub.NameColon.Name);
        else if (sub.ExpressionColon != null)
        {
            var x = sub.ExpressionColon.Expression;
            while (x is MemberAccessExpressionSyntax ma) { names.Insert(0, ma.Name); x = ma.Expression; }
            if (x is not SimpleNameSyntax first) return Unsupported(sub, "extended property pattern");
            names.Insert(0, first);
        }
        else return Unsupported(sub, "property subpattern without a name");

        var parts = new List<string>();
        var value = subject;
        var vt = type;
        for (var k = 0; k < names.Count; k++)
        {
            var member = Model(sub).GetSymbolInfo(names[k]).Symbol;
            var read = MemberValue(member, value, vt, names[k], out var memberType, out var isCall);
            if (read == null || memberType == null) return "0";
            if (k == names.Count - 1)
            {
                parts.Add(isCall ? Once(read, memberType, sub, e => PatternTest(sub.Pattern, e, memberType)) : PatternTest(sub.Pattern, read, memberType));
                break;
            }
            if (isCall)
            {
                return Unsupported(names[k], "extended property pattern through a property with a getter; spell it as nested property patterns");
            }
            value = read;
            vt = memberType;
            if (NotNullTest(value, vt) is { } nn) parts.Add(nn);
        }
        return parts.Count == 1 ? parts[0] : "(" + string.Join(" && ", parts) + ")";
    }

    /// <summary>The test <paramref name="test"/> on a value that costs a call: the value goes into a temporary first.</summary>
    private string Once(string value, ITypeSymbol type, SyntaxNode at, Func<string, string> test)
    {
        var tmp = "es_pv" + _patternTemp++;
        return $"({{ {Decl(type, tmp, at)} = {value}; {test(tmp)}; }})";
    }

    /// <summary>The C expression that reads field or property <paramref name="member"/> of <paramref name="subject"/>, or null (reported) when it cannot.</summary>
    private string? MemberValue(ISymbol? member, string subject, ITypeSymbol type, SyntaxNode at, out ITypeSymbol? memberType, out bool isCall)
    {
        memberType = null;
        isCall = false;
        if (IsInterfaceType(type)) { Unsupported(at, "property pattern on an interface"); return null; }
        switch (member)
        {
            case IFieldSymbol { IsStatic: false } f:
                memberType = f.Type;
                return IsRef(type) ? $"{subject}->{MemberPath(type, f.ContainingType, FieldCName(f))}" : $"({subject}).{FieldCName(f)}";
            case IPropertySymbol { Name: "Length", IsStatic: false } when type is IArrayTypeSymbol:
                memberType = _compilation.GetSpecialType(SpecialType.System_Int32);
                _usesLen = true;
                return $"ES_LEN({subject})";
            case IPropertySymbol { Name: "Length", IsStatic: false } when IsSpan(type):
                memberType = _compilation.GetSpecialType(SpecialType.System_Int32);
                return $"({subject}).length";
            case IPropertySymbol { Name: "Length", IsStatic: false } when type.SpecialType == SpecialType.System_String:
                memberType = _compilation.GetSpecialType(SpecialType.System_Int32);
                _usesStrlen = true;
                isCall = true;
                return $"es_strlen({subject})";
            case IPropertySymbol { IsStatic: false, IsIndexer: false } p when IsAutoProperty(p):
                memberType = p.Type;
                return IsRef(type) ? $"{subject}->{MemberPath(type, p.ContainingType, p.Name)}" : $"({subject}).{p.Name}";
            case IPropertySymbol { IsStatic: false, IsIndexer: false, GetMethod: { } get } p when !p.ReturnsByRef && !p.ReturnsByRefReadonly:
            {
                memberType = p.Type;
                isCall = true;
                if (IsRef(type)) return InstanceCall(get, subject, Array.Empty<string>());
                if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct }) break;
                var tmp = "es_pr" + _patternTemp++;
                return $"({{ {Decl(type, tmp, at)} = {subject}; {InstanceCall(get, "&" + tmp, Array.Empty<string>())}; }})";
            }
        }
        Unsupported(at, "property pattern on this member");
        return null;
    }

    /// <summary>
    /// <c>[a, b]</c>, <c>[first, ..]</c>, <c>[.., last]</c>, <c>[_, .. var middle, _]</c> over arrays, spans, strings and any type with a
    /// Length or Count and an int indexer (Vec). The length is read once; elements before the slice count from the start, those after it
    /// from the end. A slice with a subpattern is a view only for spans (no copy); over anything else it would allocate and is ES0099.
    /// </summary>
    private string ListPatternTest(ListPatternSyntax lp, string subject, ITypeSymbol type)
    {
        var pats = lp.Patterns;
        var sliceAt = -1;
        for (var i = 0; i < pats.Count; i++)
            if (pats[i] is SlicePatternSyntax)
            {
                if (sliceAt >= 0) return Unsupported(pats[i], "two slices in one list pattern");
                sliceAt = i;
            }
        var before = sliceAt < 0 ? pats.Count : sliceAt;
        var after = sliceAt < 0 ? 0 : pats.Count - sliceAt - 1;

        var id = _patternTemp++;
        var len = "es_ln" + id;
        var pre = new StringBuilder();
        string length;
        Func<string, string> item;
        ITypeSymbol elem;
        var itemIsCall = false;
        if (type is IArrayTypeSymbol { Rank: 1 } arr)
        {
            // Arrays are fixed-size C arrays, so the subject here is the array itself and sizeof gives the length.
            _usesLen = true;
            length = $"ES_LEN({subject})";
            item = i => $"{subject}[{i}]";
            elem = arr.ElementType;
        }
        else if (IsSpan(type))
        {
            length = $"({subject}).length";
            item = i => $"({subject}).ptr[{i}]";
            elem = ((INamedTypeSymbol)type).TypeArguments[0];
        }
        else if (type.SpecialType == SpecialType.System_String)
        {
            _usesStrlen = true;
            length = $"es_strlen({subject})";
            item = i => $"((uint8_t)({subject})[{i}])";
            elem = _compilation.GetSpecialType(SpecialType.System_Char);
        }
        else
        {
            var op = Model(lp).GetOperation(lp) as IListPatternOperation;
            if (op?.LengthSymbol is not IPropertySymbol lenProp || lenProp.GetMethod == null || lenProp.IsStatic)
                return Unsupported(lp, "list pattern on a type without a Length or Count property");
            if (op.IndexerSymbol is not IPropertySymbol { IsIndexer: true, GetMethod: { } getItem } indexer || indexer.Parameters.Length != 1 ||
                indexer.Parameters[0].Type.SpecialType != SpecialType.System_Int32 || indexer.ReturnsByRef || indexer.ReturnsByRefReadonly)
                return Unsupported(lp, "list pattern on a type without an int indexer that returns a value");
            if (IsInterfaceType(type) || IsInterfaceType(indexer.ContainingType)) return Unsupported(lp, "list pattern on an interface");
            string receiver;
            if (IsRef(type)) receiver = subject;
            else if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct })
            {
                pre.Append($"{Decl(type, "es_lr" + id, lp)} = {subject}; ");
                receiver = "&es_lr" + id;
            }
            else return Unsupported(lp, "list pattern on this type");
            length = IsAutoProperty(lenProp)
                ? (IsRef(type) ? $"{receiver}->{MemberPath(type, lenProp.ContainingType, lenProp.Name)}" : $"({receiver})->{lenProp.Name}")
                : InstanceCall(lenProp.GetMethod, receiver, Array.Empty<string>());
            item = i => InstanceCall(getItem, receiver, new[] { i });
            elem = indexer.Type;
            itemIsCall = true;
        }

        var parts = new List<string> { sliceAt < 0 ? $"{len} == {before}" : $"{len} >= {before + after}" };
        string Element(PatternSyntax p, string index)
        {
            if (p is DiscardPatternSyntax) return "1";
            return itemIsCall ? Once(item(index), elem, p, e => PatternTest(p, e, elem)) : PatternTest(p, item(index), elem);
        }
        for (var i = 0; i < before; i++) parts.Add(Element(pats[i], i.ToString()));
        for (var j = 0; j < after; j++) parts.Add(Element(pats[sliceAt + 1 + j], $"{len} - {after - j}"));
        if (sliceAt >= 0 && ((SlicePatternSyntax)pats[sliceAt]).Pattern is { } slicePattern and not DiscardPatternSyntax)
        {
            if (!IsSpan(type))
                return Unsupported(slicePattern, "a slice with a subpattern here (it would copy); only spans have a cheap slice view");
            var spanType = SpanType((INamedTypeSymbol)type, lp);
            parts.Add(Once($"(({spanType}){{ ({subject}).ptr + {before}, {len} - {before + after} }})", type, slicePattern,
                e => PatternTest(slicePattern, e, type)));
        }
        pre.Append($"int32_t {len} = {length}; ");
        var test = $"({{ {pre}{string.Join(" && ", parts.Where(p => p != "1"))}; }})";
        return NotNullTest(subject, type) is { } nn ? $"({nn} && {test})" : test;
    }

    /// <summary>
    /// Declarations of the pattern variables under <paramref name="roots"/> (not those of nested switch expressions or lambdas, which
    /// declare their own). A name used in several arms or sections is declared once; with two different C types it is ES0099.
    /// </summary>
    private List<string> PatternLocalDecls(IEnumerable<SyntaxNode> roots)
    {
        var seen = new Dictionary<string, string>();
        var lines = new List<string>();
        foreach (var root in roots)
        {
            // `out var n` / `out T n` / `out _` inside a when clause: declared like the pattern variables.
            foreach (var de in root.DescendantNodesAndSelf().OfType<DeclarationExpressionSyntax>())
            {
                if (!de.Ancestors().OfType<WhenClauseSyntax>().Any()) continue;
                if (de.Ancestors().TakeWhile(a => a != root).Any(a => a is SwitchExpressionArmSyntax or LambdaExpressionSyntax or SwitchExpressionSyntax)) continue;
                if (de.Designation is ParenthesizedVariableDesignationSyntax) { Unsupported(de, "a deconstructing out declaration in a when clause"); continue; }
                string name, decl;
                if (de.Designation is SingleVariableDesignationSyntax single && Model(de).GetDeclaredSymbol(single) is ILocalSymbol ol)
                { name = ol.Name; decl = Decl(ol.Type, ol.Name, de); }
                else if (Model(de).GetTypeInfo(de).Type is { } dt)
                { name = DiscardName(de); decl = Decl(dt, name, de); }
                else continue;
                if (seen.TryGetValue(name, out var prev))
                {
                    if (prev != decl) Unsupported(de, $"out variable '{name}' declared again with another type in the same switch; rename one");
                    continue;
                }
                seen[name] = decl;
                lines.Add(Unused + decl + ";");
            }
            foreach (var id in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (id.Identifier.Text != "_" || id.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax } }) continue;
                if (!id.Ancestors().OfType<WhenClauseSyntax>().Any()) continue;
                if (id.Ancestors().TakeWhile(a => a != root).Any(a => a is SwitchExpressionArmSyntax or LambdaExpressionSyntax or SwitchExpressionSyntax)) continue;
                if (Model(id).GetSymbolInfo(id).Symbol is not IDiscardSymbol discard) continue;
                var dn = DiscardName(id);
                if (seen.ContainsKey(dn)) continue;
                seen[dn] = "";
                lines.Add(Unused + Decl(discard.Type, dn, id) + ";");
            }
            foreach (var d in root.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
            {
                if (d.Parent is DeclarationExpressionSyntax) continue;
                if (d.Ancestors().TakeWhile(a => a != root).Any(a => a is SwitchExpressionArmSyntax or LambdaExpressionSyntax or SwitchExpressionSyntax)) continue;
                if (Model(d).GetDeclaredSymbol(d) is not ILocalSymbol local) continue;
                var decl = Decl(local.Type, local.Name, d);
                if (seen.TryGetValue(local.Name, out var earlier))
                {
                    if (earlier != decl) Unsupported(d, $"pattern variable '{local.Name}' declared again with another type in the same switch; rename one");
                    continue;
                }
                seen[local.Name] = decl;
                lines.Add(Unused + decl + ";");
            }
        }
        return lines;
    }

    /// <summary>An array subject must be used in place (it is a C array); anything else that is not a plain name goes into a temporary.</summary>
    private bool UsePatternSubjectInPlace(ExpressionSyntax e, ITypeSymbol type)
    {
        if (!IsPure(e)) return false;
        if (type is IArrayTypeSymbol) return true;
        // A getter (or a Vec indexer) in the subject would run once per test: keep it to fields and locals.
        return !e.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Any(x =>
            Model(x).GetSymbolInfo(x).Symbol is IPropertySymbol p && !IsAutoProperty(p) &&
            !(p.Name == "Length" && (IsSpan(p.ContainingType) || p.ContainingType.SpecialType == SpecialType.System_Array)) &&
            !(p.IsIndexer && IsSpan(p.ContainingType)));
    }

    /// <summary>
    /// <c>switch</c> with pattern labels (and <c>when</c>): the subject is evaluated once, the labels are tried in order (the default
    /// last) giving the number of the one that matched, and a C <c>switch</c> on that number keeps <c>break</c> as it is.
    /// </summary>
    private void EmitPatternSwitch(SwitchStatementSyntax sw)
    {
        var type = Model(sw).GetTypeInfo(sw.Expression).Type;
        if (type == null) { Unsupported(sw, "switch on an expression without a type"); return; }
        var id = _patternTemp++;
        var subject = "es_ps" + id;
        var inPlace = UsePatternSubjectInPlace(sw.Expression, type);
        Line("{");
        _indent++;
        if (inPlace) subject = Expr(sw.Expression);
        else Line($"{Decl(type, subject, sw)} = {Expr(sw.Expression, top: true)};");
        foreach (var d in PatternLocalDecls(sw.Sections.SelectMany(s => s.Labels))) Line(d);

        var numbers = new Dictionary<SwitchLabelSyntax, int>();
        var tests = new List<string>();
        foreach (var label in sw.Sections.SelectMany(s => s.Labels))
        {
            string test;
            switch (label)
            {
                case CasePatternSwitchLabelSyntax cp:
                    test = PatternTest(cp.Pattern, subject, type);
                    if (cp.WhenClause != null) test = $"({test}) && ({Expr(cp.WhenClause.Condition, top: true)})";
                    break;
                case CaseSwitchLabelSyntax c:
                    test = ConstantTest(c.Value, c, subject, type);
                    break;
                default:
                    continue;
            }
            numbers[label] = tests.Count;
            tests.Add(test);
        }
        var pick = new StringBuilder();
        for (var i = 0; i < tests.Count; i++) pick.Append($"({tests[i]}) ? {i} : ");
        pick.Append("-1");
        if (tests.Count == 0 && !inPlace) Line($"(void){subject};");
        Line($"switch ({pick})");
        Line("{");
        _indent++;
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
                Line(numbers.TryGetValue(label, out var n) ? $"case {n}:" : "default:");
            Line("{");
            _indent++;
            foreach (var s in section.Statements) Statement(s);
            _indent--;
            Line("}");
        }
        _indent--;
        Line("}");
        _indent--;
        Line("}");
    }

    /// <summary>The Deconstruct, Length/Count and indexer members that a pattern calls without naming them (for the reachability walk).</summary>
    private static IEnumerable<ISymbol> PatternMembers(SemanticModel model, PatternSyntax pattern)
    {
        switch (model.GetOperation(pattern))
        {
            case IRecursivePatternOperation { DeconstructSymbol: { } d }:
                yield return d;
                break;
            case IListPatternOperation lp:
                if (lp.LengthSymbol != null) yield return lp.LengthSymbol;
                if (lp.IndexerSymbol != null) yield return lp.IndexerSymbol;
                break;
        }
    }
}
