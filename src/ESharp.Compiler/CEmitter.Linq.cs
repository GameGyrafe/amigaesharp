using System;
using System.Collections.Generic;
using System.Linq;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// LINQ over arrays and <c>Enumerable.Range</c>, lowered to loops. A chain such as <c>a.Where(f).Select(g).Sum()</c> or a simple query
/// (<c>from x in a where .. let .. select ..</c>) becomes one fused loop: no sequence is ever built, so the chain must be consumed
/// on the spot by a terminal call (<c>Sum</c>, <c>Count</c>, <c>Any</c>, ...) or by <c>foreach</c>. The lambdas are pasted into the loop
/// body (so they may use and change the locals around them without a closure) and must be expression lambdas, method groups or delegate variables.
/// </summary>
internal sealed partial class CEmitter
{
    private enum StageKind { Where, WhereNot, Select, Take, Skip, QueryWhere, QueryLet, QuerySelect, OrderBy, QueryOrder }

    /// <summary>One step of a chain. <c>Fn</c> is the lambda (or the count of Take/Skip, or the query expression); <c>Out</c> is the element type after the step.</summary>
    private sealed record LinqStage(StageKind Kind, ExpressionSyntax? Fn, ITypeSymbol Out, string? Name = null, List<(ExpressionSyntax Fn, bool Desc, ITypeSymbol Type)>? Keys = null);

    /// <summary>Where the elements come from: an array expression, or a range.</summary>
    private sealed record LinqSource(ExpressionSyntax? Array, ExpressionSyntax? Start, ExpressionSyntax? Count, ITypeSymbol Elem, string? RangeVar, string? Buf = null, string? BufCount = null, SyntaxNode? Anchor = null);

    private const string Unused = "__attribute__((unused)) ";

    private bool IsLinqCall(InvocationExpressionSyntax inv, out IMethodSymbol method, out ExpressionSyntax? receiver)
    {
        receiver = null;
        method = null!;
        if (Model(inv).GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;
        // On an array, a.Contains(x) may bind to MemoryExtensions.Contains (the span overload) instead of Enumerable.Contains.
        var container = m.ContainingType?.ToDisplayString();
        if (container != "System.Linq.Enumerable" && !(container == "System.MemoryExtensions" && m.Name == "Contains" && m.Parameters.Length == 1)) return false;
        method = m;
        if (m.ReducedFrom != null && inv.Expression is MemberAccessExpressionSyntax ma) receiver = ma.Expression;
        return true;
    }

    /// <summary>A lambda that is an argument of a LINQ call is pasted into the loop, so it needs no function of its own.</summary>
    private bool IsInlinedLinqLambda(SyntaxNode lam) =>
        lam.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } } && IsLinqCall(inv, out _, out _);

    private static ExpressionSyntax Unparen(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
        return e;
    }

    /// <summary>Is this expression a chain or query that <see cref="EmitLoop"/> can turn into a loop (so a foreach may use it)?</summary>
    private bool IsLinqSource(ExpressionSyntax e)
    {
        e = Unparen(e);
        return e is QueryExpressionSyntax || (e is InvocationExpressionSyntax inv && IsLinqCall(inv, out var m, out _) &&
            m.Name is "Where" or "Select" or "Take" or "Skip" or "Range" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending");
    }

    /// <summary>Splits a chain into its source and steps, or reports why it cannot.</summary>
    private bool ParseChain(ExpressionSyntax e, out LinqSource source, out List<LinqStage> stages)
    {
        stages = new List<LinqStage>();
        source = null!;
        var reversed = new List<LinqStage>();
        var pendingThen = new List<(ExpressionSyntax Fn, bool Desc, ITypeSymbol Type)>();
        var cur = Unparen(e);
        while (true)
        {
            if (cur is QueryExpressionSyntax q)
            {
                if (pendingThen.Count > 0) { Unsupported(q, "ThenBy without OrderBy before it"); return false; }
                return ParseQuery(q, out source, out stages, reversed);
            }

            if (cur is InvocationExpressionSyntax inv && IsLinqCall(inv, out var m, out var recv))
            {
                var args = inv.ArgumentList.Arguments;
                if (m.Name == "Range" && recv == null && args.Count == 2)
                {
                    source = new LinqSource(null, args[0].Expression, args[1].Expression, m.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } rt ? rt.TypeArguments[0] : _compilation.GetSpecialType(SpecialType.System_Int32), null);
                    break;
                }
                if (recv == null) { Unsupported(inv, $"Enumerable.{m.Name}(...) written as a plain call; write it as a method call on the array"); return false; }
                var outType = Model(inv).GetTypeInfo(inv).Type is INamedTypeSymbol { TypeArguments.Length: 1 } seq ? seq.TypeArguments[0] : null;
                if (pendingThen.Count > 0 && m.Name is not ("OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending"))
                {
                    Unsupported(inv, "ThenBy without OrderBy right before it");
                    return false;
                }
                switch (m.Name)
                {
                    case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" when args.Count == 1 && outType != null:
                    {
                        var kt = KeyType(args[0].Expression);
                        if (kt == null || !Orderable(kt)) { Unsupported(args[0], "a sort key that is not a number, char, bool or enum"); return false; }
                        var key = (args[0].Expression, m.Name.EndsWith("Descending"), kt);
                        if (m.Name.StartsWith("ThenBy")) pendingThen.Insert(0, key);
                        else
                        {
                            var keys = new List<(ExpressionSyntax, bool, ITypeSymbol)> { key };
                            keys.AddRange(pendingThen);
                            pendingThen.Clear();
                            reversed.Add(new LinqStage(StageKind.OrderBy, null, outType, null, keys));
                        }
                        break;
                    }
                    case "Where" when args.Count == 1 && outType != null: reversed.Add(new LinqStage(StageKind.Where, args[0].Expression, outType)); break;
                    case "Select" when args.Count == 1 && outType != null: reversed.Add(new LinqStage(StageKind.Select, args[0].Expression, outType)); break;
                    case "Take" when args.Count == 1 && outType != null: reversed.Add(new LinqStage(StageKind.Take, args[0].Expression, outType)); break;
                    case "Skip" when args.Count == 1 && outType != null: reversed.Add(new LinqStage(StageKind.Skip, args[0].Expression, outType)); break;
                    default:
                        Unsupported(inv, $"{m.Name} in the middle of a LINQ chain (only Where, Select, Take, Skip and OrderBy can be followed by more steps)");
                        return false;
                }
                cur = Unparen(recv);
                continue;
            }

            var type = Model(cur).GetTypeInfo(cur).Type;
            if (pendingThen.Count > 0) { Unsupported(cur, "ThenBy without OrderBy right before it"); return false; }
            if (type is IArrayTypeSymbol arr)
            {
                if (!IsSimple(cur)) { Unsupported(cur, "LINQ over a complex expression; assign the array to a local first"); return false; }
                source = new LinqSource(cur, null, null, arr.ElementType, null);
                break;
            }
            Unsupported(cur, "LINQ over anything but an array or Enumerable.Range");
            return false;
        }
        reversed.Reverse();
        stages = reversed;
        return true;
    }

    private ITypeSymbol? KeyType(ExpressionSyntax fn) =>
        Model(fn).GetTypeInfo(fn).ConvertedType is INamedTypeSymbol { DelegateInvokeMethod: { } dm } ? dm.ReturnType : null;

    private static bool Orderable(ITypeSymbol t) => t.TypeKind == TypeKind.Enum || t.SpecialType is
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Char or SpecialType.System_Boolean or SpecialType.System_Single or SpecialType.System_Double;

    private bool ParseQuery(QueryExpressionSyntax q, out LinqSource source, out List<LinqStage> stages, List<LinqStage> outer)
    {
        stages = new List<LinqStage>();
        source = null!;
        var model = Model(q);
        var from = q.FromClause;
        if (from.Type != null) { Unsupported(from, "a typed range variable in a query"); return false; }
        if (model.GetDeclaredSymbol(from) is not IRangeVariableSymbol rv) { Unsupported(from, "query"); return false; }
        var src = Unparen(from.Expression);
        // A query may itself range over a chain: from x in a.Where(..) select ..
        if (IsLinqSource(src))
        {
            // Inner chain first, then the query's own steps.
            if (!ParseChain(src, out source, out var inner)) return false;
            stages.AddRange(inner);
            source = source with { RangeVar = source.RangeVar };
        }
        else if (Model(src).GetTypeInfo(src).Type is IArrayTypeSymbol arr)
        {
            if (!IsSimple(src)) { Unsupported(src, "LINQ over a complex expression; assign the array to a local first"); return false; }
            source = new LinqSource(src, null, null, arr.ElementType, null);
        }
        else { Unsupported(src, "LINQ over anything but an array or Enumerable.Range"); return false; }

        var rvType = stages.Count > 0 ? stages[^1].Out : source.Elem;
        stages.Add(new LinqStage(StageKind.QueryLet, null, rvType, rv.Name));   // binds the range variable to the element
        var sawLet = false;
        foreach (var clause in q.Body.Clauses)
        {
            switch (clause)
            {
                case OrderByClauseSyntax ob:
                {
                    if (sawLet) { Unsupported(ob, "orderby after a let (the let variable would be gone after sorting); put the let after the orderby"); return false; }
                    var keys = new List<(ExpressionSyntax, bool, ITypeSymbol)>();
                    foreach (var o in ob.Orderings)
                    {
                        var kt = Model(o.Expression).GetTypeInfo(o.Expression).Type;
                        if (kt == null || !Orderable(kt)) { Unsupported(o, "a sort key that is not a number, char, bool or enum"); return false; }
                        keys.Add((o.Expression, o.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword), kt));
                    }
                    stages.Add(new LinqStage(StageKind.QueryOrder, null, rvType, null, keys));
                    stages.Add(new LinqStage(StageKind.QueryLet, null, rvType, rv.Name));   // the range variable again, from the sorted element
                    break;
                }
                case WhereClauseSyntax w: stages.Add(new LinqStage(StageKind.QueryWhere, w.Condition, rvType)); break;
                case LetClauseSyntax l when model.GetDeclaredSymbol(l) is IRangeVariableSymbol lv:
                    sawLet = true;
                    stages.Add(new LinqStage(StageKind.QueryLet, l.Expression, Model(l.Expression).GetTypeInfo(l.Expression).Type ?? rvType, lv.Name)); break;
                default: Unsupported(clause, "this query clause"); return false;
            }
        }
        if (q.Body.SelectOrGroup is not SelectClauseSyntax sel || q.Body.Continuation != null) { Unsupported(q, "query group or continuation"); return false; }
        var selType = Model(sel.Expression).GetTypeInfo(sel.Expression).Type;
        if (selType == null) { Unsupported(sel, "query"); return false; }
        stages.Add(new LinqStage(StageKind.QuerySelect, sel.Expression, selType));
        stages.AddRange(outer.AsEnumerable().Reverse());
        return true;
    }

    /// <summary>The call of a lambda or method group on already-evaluated values; the lambda's parameters are declared first (the caller has opened a block).</summary>
    private string InlineFn(ExpressionSyntax fn, params string[] values)
    {
        fn = Unparen(fn);
        if (fn is LambdaExpressionSyntax lam)
        {
            if (Model(lam).GetSymbolInfo(lam).Symbol is not IMethodSymbol lm) return Unsupported(lam, "lambda");
            for (var i = 0; i < lm.Parameters.Length && i < values.Length; i++)
                Line($"{Unused}{Decl(lm.Parameters[i].Type, lm.Parameters[i].Name, lam)} = {values[i]};");
            ExpressionSyntax? body = lam.ExpressionBody;
            if (body == null && lam.Block is { Statements: { Count: 1 } stmts } && stmts[0] is ReturnStatementSyntax { Expression: { } rexp }) body = rexp;
            if (body == null) return Unsupported(lam, "a LINQ lambda with a statement body; use an expression lambda or write a loop");
            return Expr(body, top: true);
        }
        var type = Model(fn).GetTypeInfo(fn).Type;
        if (type?.TypeKind == TypeKind.Delegate && IsSimple(fn))
        {
            var d = _linqSnap.TryGetValue(fn, out var snap) ? snap : Expr(fn);
            return $"{d}.fn({string.Join(", ", values.Prepend($"{d}.env"))})";
        }
        if (Model(fn).GetSymbolInfo(fn).Symbol is IMethodSymbol { IsStatic: true } group && Model(fn).GetTypeInfo(fn).ConvertedType?.TypeKind == TypeKind.Delegate)
            return $"{FuncName(group)}({string.Join(", ", values)})";
        return Unsupported(fn, "this kind of LINQ argument; use a lambda, a static method or a delegate variable");
    }

    private readonly Dictionary<ExpressionSyntax, string> _linqSnap = new();

    /// <summary>
    /// Issue #175: a delegate variable that is a step, key or the function of a chain (<c>a.Where(d)</c>), and that += / -= or Heap.Free can
    /// release (see <see cref="HoldsWhenShared"/>), is loaded once before the loop and held until it ends, as dotnet keeps the value it was
    /// given: a += / -= or Heap.Free(ref d) made while the loop runs (by d itself, or by the body of a foreach over the chain) cannot free
    /// what the loop calls, and the loop goes on calling that value. <paramref name="scoped"/>: the hold is dropped by a cleanup when the
    /// enclosing block ends (a foreach body may leave by return or break); else the caller releases the returned temporaries.
    /// </summary>
    private List<string> LinqSnapshots(IEnumerable<ExpressionSyntax?> fns, bool scoped)
    {
        var held = new List<string>();
        foreach (var f in fns)
        {
            if (f == null) continue;
            var fn = Unparen(f);
            if (_linqSnap.ContainsKey(fn) || Model(fn).GetTypeInfo(fn).Type is not { TypeKind: TypeKind.Delegate } dt || !IsSimple(fn) || !HoldsWhenShared(fn)) continue;
            // Loaded before the loop, so before the source, the Take / Skip counts and the seed: only when none of what C# evaluates
            // before d runs code. Else a variable that is only freed is read in the loop, as before (one with += / -= is refused then).
            if (!LinqReadFirst(fn)) continue;
            _usesClosures = true;
            var t = $"_q{++_temp}";
            Line($"{CType(dt, fn)} {t} = {Expr(fn, top: true)};");
            if (scoped) Line($"void *{t}_h __attribute__((cleanup(es_frame_drop))) = {t}.env;");
            Line(scoped ? $"es_frame_hold({t}_h);" : $"es_frame_hold({t}.env);");
            _linqSnap[fn] = t;
            held.Add(t);
        }
        return held;
    }

    private static IEnumerable<ExpressionSyntax?> StageFns(List<LinqStage> stages) =>
        stages.SelectMany(st => st.Kind is StageKind.Where or StageKind.WhereNot or StageKind.Select ? new[] { st.Fn }
            : st.Kind == StageKind.OrderBy && st.Keys != null ? st.Keys.Select(k => (ExpressionSyntax?)k.Fn) : Array.Empty<ExpressionSyntax?>());

    private static int FnParameterCount(ExpressionSyntax fn) => Unparen(fn) switch
    {
        SimpleLambdaExpressionSyntax => 1,
        ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.Count,
        _ => 1,
    };

    /// <summary>
    /// Writes the loop for a chain, and calls <paramref name="sink"/> with the element expression and its type where the loop body should use each element.
    /// The sink may <c>break</c> out of the loop.
    /// </summary>
    private void EmitLoop(LinqSource source, List<LinqStage> stages, Action<string, ITypeSymbol> sink)
    {
        var barrier = stages.FindIndex(x => x.Kind is StageKind.OrderBy or StageKind.QueryOrder);
        if (barrier >= 0) { EmitSorted(source, stages, barrier, sink); return; }
        var anchor = source.Anchor ?? source.Array ?? source.Start!;
        var id = ++_temp;
        var counters = new Dictionary<int, string>();
        for (var k = 0; k < stages.Count; k++)
        {
            var st = stages[k];
            if (st.Kind is StageKind.Take or StageKind.Skip)
            {
                counters[k] = $"_c{id}_{k}";
                Line($"int32_t {counters[k]} = {Expr(st.Fn!, top: true)};");
            }
            else if (st.Kind is StageKind.Where or StageKind.WhereNot or StageKind.Select && FnParameterCount(st.Fn!) == 2)
            {
                counters[k] = $"_c{id}_{k}";
                Line($"int32_t {counters[k]} = 0;");
            }
        }

        var i = $"_i{id}";
        var n = $"_n{id}";
        string first;
        if (source.Array != null)
        {
            _usesLen = true;
            var array = ExprCore(source.Array, false);   // the array itself, not the span it may be converted to for a MemoryExtensions call
            Line($"for (int32_t {i} = 0, {n} = ES_LEN({array}); {i} < {n}; {i}++)");
            first = $"{array}[{i}]";
        }
        else if (source.Buf != null)
        {
            Line($"for (int32_t {i} = 0, {n} = {source.BufCount}; {i} < {n}; {i}++)");
            first = $"{source.Buf}[{i}]";
        }
        else
        {
            Line($"for (int32_t {i} = 0, {n} = {Expr(source.Count!, top: true)}; {i} < {n}; {i}++)");
            first = $"({CType(source.Elem, source.Start!)})({Expr(source.Start!)} + {i})";
        }
        Line("{");
        _indent++;
        // Take(n): stop before pulling another element once n have come through.
        foreach (var (k, c) in counters.Where(kv => stages[kv.Key].Kind == StageKind.Take)) Line($"if ({c} <= 0) break;");

        var cur = $"_e{id}_0";
        var curType = source.Elem;
        Line($"{Unused}{Decl(curType, cur, anchor)} = {first};");
        for (var k = 0; k < stages.Count; k++)
        {
            var st = stages[k];
            switch (st.Kind)
            {
                case StageKind.Where:
                case StageKind.WhereNot:
                {
                    Line("{");
                    _indent++;
                    var args = FnParameterCount(st.Fn!) == 2 ? new[] { cur, $"{counters[k]}++" } : new[] { cur };
                    var cond = InlineFn(st.Fn!, args);
                    Line(st.Kind == StageKind.Where ? $"if (!({cond})) continue;" : $"if ({cond}) continue;");
                    _indent--;
                    Line("}");
                    break;
                }
                case StageKind.Select:
                {
                    var next = $"_e{id}_{k + 1}";
                    Line($"{Unused}{Decl(st.Out, next, st.Fn!)};");
                    Line("{");
                    _indent++;
                    var args = FnParameterCount(st.Fn!) == 2 ? new[] { cur, $"{counters[k]}++" } : new[] { cur };
                    var value = InlineFn(st.Fn!, args);
                    Line($"{next} = {value};");
                    _indent--;
                    Line("}");
                    cur = next;
                    curType = st.Out;
                    break;
                }
                case StageKind.Take:
                    Line($"{counters[k]}--;");
                    break;
                case StageKind.Skip:
                    Line($"if ({counters[k]} > 0) {{ {counters[k]}--; continue; }}");
                    break;
                case StageKind.QueryLet when st.Fn == null:   // the range variable
                    Line($"{Unused}{Decl(st.Out, st.Name!, anchor)} = {cur};");
                    break;
                case StageKind.QueryLet:
                    Line($"{Unused}{Decl(st.Out, st.Name!, st.Fn)} = {Expr(st.Fn, top: true)};");
                    break;
                case StageKind.QueryWhere:
                    Line($"if (!({Expr(st.Fn!, top: true)})) continue;");
                    break;
                case StageKind.QuerySelect:
                {
                    var next = $"_e{id}_{k + 1}";
                    Line($"{Unused}{Decl(st.Out, next, st.Fn!)} = {Expr(st.Fn!, top: true)};");
                    cur = next;
                    curType = st.Out;
                    break;
                }
            }
        }
        sink(cur, curType);
        _indent--;
        Line("}");
    }

    /// <summary>
    /// A sort is a barrier: the steps before it run as one loop that stores each element and its keys in stack arrays (as large as the source
    /// could be), a stable insertion sort orders them, and the steps after it run as a second loop over the sorted copy.
    /// </summary>
    private void EmitSorted(LinqSource source, List<LinqStage> stages, int barrier, Action<string, ITypeSymbol> sink)
    {
        var st = stages[barrier];
        var keys = st.Keys!;
        var id = ++_temp;
        string buf = $"_b{id}", cnt = $"_m{id}", z = $"_z{id}";
        ITypeSymbol elem = source.Elem;
        for (var k = 0; k < barrier; k++) if (stages[k].Kind is StageKind.Select or StageKind.QuerySelect) elem = stages[k].Out;
        var anchor = keys[0].Fn;

        string bound;
        if (source.Buf != null) bound = source.BufCount!;
        else if (source.Array != null) { _usesLen = true; bound = $"ES_LEN({ExprCore(source.Array, false)})"; }
        else bound = Expr(source.Count!, top: true);

        Line("{");
        _indent++;
        Line($"int32_t {z} = {bound};");
        Line($"int32_t {cnt} = 0;");
        Line($"{CType(elem, anchor)} {buf}[{z} > 0 ? {z} : 1];");
        for (var j = 0; j < keys.Count; j++) Line($"{CType(keys[j].Type, anchor)} _k{id}_{j}[{z} > 0 ? {z} : 1];");

        EmitLoop(source, stages.Take(barrier).ToList(), (cur, _) =>
        {
            Line("{");
            _indent++;
            for (var j = 0; j < keys.Count; j++)
            {
                Line("{");
                _indent++;
                var value = st.Kind == StageKind.OrderBy ? InlineFn(keys[j].Fn, cur) : Expr(keys[j].Fn, top: true);
                Line($"_k{id}_{j}[{cnt}] = {value};");
                _indent--;
                Line("}");
            }
            Line($"{buf}[{cnt}] = {cur};");
            Line($"{cnt}++;");
            _indent--;
            Line("}");
        });

        // Stable insertion sort: an element moves past its neighbour only when that one is strictly greater (or, descending, smaller).
        string s = $"_s{id}", p = $"_p{id}";
        var shift = "0";
        for (var j = keys.Count - 1; j >= 0; j--)
        {
            string prev = $"_k{id}_{j}[{p} - 1]", curk = $"_t{id}_{j}";
            shift = $"({prev} != {curk} ? ({prev} {(keys[j].Desc ? "<" : ">")} {curk}) : {shift})";
        }
        Line($"for (int32_t {s} = 1; {s} < {cnt}; {s}++)");
        Line("{");
        _indent++;
        Line($"{CType(elem, anchor)} _te{id} = {buf}[{s}];");
        for (var j = 0; j < keys.Count; j++) Line($"{CType(keys[j].Type, anchor)} _t{id}_{j} = _k{id}_{j}[{s}];");
        Line($"int32_t {p} = {s};");
        Line($"while ({p} > 0 && {shift})");
        Line("{");
        _indent++;
        Line($"{buf}[{p}] = {buf}[{p} - 1];");
        for (var j = 0; j < keys.Count; j++) Line($"_k{id}_{j}[{p}] = _k{id}_{j}[{p} - 1];");
        Line($"{p}--;");
        _indent--;
        Line("}");
        Line($"{buf}[{p}] = _te{id};");
        for (var j = 0; j < keys.Count; j++) Line($"_k{id}_{j}[{p}] = _t{id}_{j};");
        _indent--;
        Line("}");

        var sorted = new LinqSource(null, null, null, elem, null, buf, cnt, anchor);
        EmitLoop(sorted, stages.Skip(barrier + 1).ToList(), sink);
        _indent--;
        Line("}");
    }

    /// <summary>Runs <paramref name="emit"/> and returns the lines it wrote as one C statement expression that yields <paramref name="result"/>.</summary>
    private string AsStatementExpression(Action emit, string result)
    {
        var start = _out.Length;
        var indent = _indent;
        _indent = 0;
        emit();
        _indent = indent;
        var text = _out.ToString(start, _out.Length - start);
        _out.Length = start;
        return "({ " + string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())) + $" {result}; }})";
    }

    /// <summary>A terminal call such as <c>a.Where(..).Sum()</c>, or a LINQ step used where nothing consumes it.</summary>
    private string Linq(InvocationExpressionSyntax inv, IMethodSymbol m, ExpressionSyntax? recv)
    {
        if (recv == null) return Unsupported(inv, $"Enumerable.{m.Name}(...) written as a plain call; write it as a method call on the array");
        var args = inv.ArgumentList.Arguments;
        var resultType = Model(inv).GetTypeInfo(inv).Type!;

        if (m.Name is "Where" or "Select" or "Take" or "Skip" or "Range" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
            return Unsupported(inv, $"{m.Name} whose result is kept: E# builds no sequences or arrays from them (arrays have a fixed size), so consume it right away with foreach or a call such as Sum, Count, Any or First");
        if (!ParseChain(recv, out var source, out var stages)) return "0";

        // An optional predicate or selector argument becomes one more step of the chain.
        ITypeSymbol elem = stages.Count > 0 ? stages[^1].Out : source.Elem;
        ITypeSymbol SelectorResult(ExpressionSyntax fn) =>
            Model(fn).GetTypeInfo(fn).ConvertedType is INamedTypeSymbol { DelegateInvokeMethod: { } dm } ? dm.ReturnType : resultType;
        var tid = ++_temp;
        string R = $"_r{tid}", H = $"_h{tid}", X = $"_x{tid}";
        var decl = Decl(resultType, R, inv);
        string init = $"{decl} = {{0}};";
        Action<string, ITypeSymbol> sink;
        var pre = new List<string>();
        var extra = (string?)null;
        ExpressionSyntax? aggregateFn = null;

        switch (m.Name)
        {
            case "Count":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Where, args[0].Expression, elem));
                init = $"{decl} = 0;";
                sink = (c, _) => Line($"{R}++;");
                break;
            case "Sum":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Select, args[0].Expression, SelectorResult(args[0].Expression)));
                init = $"{decl} = 0;";
                sink = (c, _) => Line($"{R} += {c};");
                break;
            case "Min" or "Max":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Select, args[0].Expression, SelectorResult(args[0].Expression)));
                if (!Orderable(stages.Count > 0 ? stages[^1].Out : elem))
                    return Unsupported(inv, $"{m.Name} of values that are not numbers, chars, bools or enums");
                pre.Add($"bool {H} = 0;");
                var cmp = m.Name == "Min" ? "<" : ">";
                sink = (c, _) => Line($"if (!{H} || {c} {cmp} {R}) {{ {R} = {c}; {H} = 1; }}");
                break;
            case "Any":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Where, args[0].Expression, elem));
                init = $"{decl} = 0;";
                sink = (c, _) => Line($"{R} = 1; break;");
                break;
            case "All" when args.Count == 1:
                stages.Add(new LinqStage(StageKind.WhereNot, args[0].Expression, elem));
                init = $"{decl} = 1;";
                sink = (c, _) => Line($"{R} = 0; break;");
                break;
            case "First" or "FirstOrDefault":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Where, args[0].Expression, elem));
                sink = (c, _) => Line($"{R} = {c}; break;");
                break;
            case "Last" or "LastOrDefault":
                if (args.Count == 1) stages.Add(new LinqStage(StageKind.Where, args[0].Expression, elem));
                sink = (c, _) => Line($"{R} = {c};");
                break;
            case "Contains" when args.Count == 1:
                extra = Expr(args[0].Expression, top: true);
                pre.Add($"{Decl(elem, X, inv)} = {extra};");
                init = $"{decl} = 0;";
                if (elem.SpecialType == SpecialType.System_String)
                    sink = (c, _) => Line($"if ({StrEq(c, X)}) {{ {R} = 1; break; }}");   // strings match by the characters
                else if (IsHeapClass(elem) || elem.TypeKind is TypeKind.Enum || elem.SpecialType != SpecialType.None)
                    sink = (c, _) => Line($"if ({c} == {X}) {{ {R} = 1; break; }}");
                else
                    return Unsupported(inv, "Contains on elements that are not numbers, chars, bools, enums, strings or classes");
                break;
            case "Aggregate" when args.Count is 1 or 2:
            {
                var fn = args[^1].Expression;
                aggregateFn = fn;
                var seeded = args.Count == 2;
                if (seeded) init = $"{decl} = {Expr(args[0].Expression, top: true)};";
                else pre.Add($"bool {H} = 0;");
                sink = (c, _) =>
                {
                    Line("{");
                    _indent++;
                    var combined = seeded ? InlineFn(fn, R, c) : null;
                    if (!seeded)
                    {
                        Line($"if (!{H}) {{ {R} = {c}; {H} = 1; }}");
                        Line("else");
                        Line("{");
                        _indent++;
                        combined = InlineFn(fn, R, c);
                        Line($"{R} = {combined};");
                        _indent--;
                        Line("}");
                    }
                    else Line($"{R} = {combined};");
                    _indent--;
                    Line("}");
                };
                break;
            }
            default:
                return Unsupported(inv, $"this form of {m.Name}");
        }

        return AsStatementExpression(() =>
        {
            var held = LinqSnapshots(StageFns(stages).Append(aggregateFn), scoped: false);
            Line(init);
            foreach (var p in pre) Line(p);
            EmitLoop(source, stages, sink);
            foreach (var h in held) { Line($"es_frame_release({h}.env);"); _linqSnap.Remove(_linqSnap.First(kv => kv.Value == h).Key); }
        }, R);
    }

    /// <summary><c>foreach (var x in chain)</c>: the loop of the chain with the foreach body as its sink.</summary>
    private void EmitLinqForEach(ForEachStatementSyntax fe)
    {
        if (!ParseChain(fe.Expression, out var source, out var stages)) return;
        var local = (ILocalSymbol)Model(fe).GetDeclaredSymbol(fe)!;
        var block = StageFns(stages).Any(f => f != null && Unparen(f) is var u && IsSimple(u) && Model(u).GetTypeInfo(u).Type?.TypeKind == TypeKind.Delegate && HoldsWhenShared(u) && LinqReadFirst(u));
        if (block) { Line("{"); _indent++; }
        var held = LinqSnapshots(StageFns(stages), scoped: true);
        EmitLoop(source, stages, (cur, _) =>
        {
            Line("{");
            _indent++;
            Line($"{Decl(local.Type, local.Name, fe)} = {cur};");
            Statement(fe.Statement);
            _indent--;
            Line("}");
        });
        foreach (var h in held) _linqSnap.Remove(_linqSnap.First(kv => kv.Value == h).Key);
        if (block) { _indent--; Line("}"); }
    }
}
