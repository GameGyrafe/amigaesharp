using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// <c>[InlineAsm]</c> and <c>[Interrupt]</c> methods.
/// <para>
/// An <c>[InlineAsm]</c> method is static and has a C# body. When the target's CPU has assembly in the attribute (m68k:
/// the constructor argument; PowerPC: <c>Ppc</c>), the C function's body is one GCC extended asm statement under
/// <c>#if defined(__mc68000__)</c> / <c>#if defined(__PPC__)</c>, with the C# body in the <c>#else</c> branch; on any other
/// target only the C# body is emitted. Operands: <c>[result]</c> (an early-clobber output, first) then one input per
/// parameter, named after it; integers in data registers (<c>d</c> / <c>r</c>), pointers in address registers (<c>a</c> /
/// <c>b</c>). <c>cc</c> and <c>memory</c> are always clobbered.
/// </para>
/// <para>
/// An <c>[Interrupt]</c> method is emitted as usual plus <c>NAME_isr(void *data)</c>, a C function that calls it and returns an
/// int32 (0 for a void method). Its address, <c>&amp;M</c> in E#, is <c>NAME_ISR</c>: on m68k an entry written in assembly
/// at the end of the file that follows exec's server convention (is_Data in a1, result in d0 with the Z flag set from it),
/// elsewhere (AROS, where servers are plain C functions with is_Data first, and the host gcc of the tests) <c>NAME_isr</c>.
/// PowerPC and sdl report ES0099.
/// </para>
/// </summary>
internal sealed partial class CEmitter
{
    private readonly List<IMethodSymbol> _interrupts = new();

    private static bool IsInlineAsm(IMethodSymbol m) => HasAttribute(m, "InlineAsmAttribute");

    private static bool IsInterrupt(IMethodSymbol m) => HasAttribute(m, "InterruptAttribute");

    private static string? NamedString(AttributeData a, string name) =>
        a.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value.Value as string;

    /// <summary>The assembly of an [InlineAsm] method for this target's CPU, its extra clobbers and the CPU guard; null when there is none (the C# body runs).</summary>
    private (string Code, string? Clobbers, bool Ppc)? AsmFor(IMethodSymbol m)
    {
        var a = GetAttribute(m, "InlineAsmAttribute");
        if (a == null) return null;
        if (IsDefined("M68K") && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string code)
            return (code, NamedString(a, "Clobbers"), false);
        if (IsDefined("PPC") && NamedString(a, "Ppc") is { } ppc)
            return (ppc, NamedString(a, "PpcClobbers"), true);
        return null;
    }

    /// <summary>[InlineAsm] and [Interrupt] methods without a body (extern) cannot be emitted: say so instead of leaving an undefined C function.</summary>
    private void CheckBodilessAsm(IEnumerable<IMethodSymbol> reachable)
    {
        foreach (var m in reachable.Where(m => (IsInlineAsm(m) || IsInterrupt(m)) && !DeclaresBody(m)))
            Unsupported(m.DeclaringSyntaxReferences.First().GetSyntax(),
                IsInlineAsm(m)
                    ? $"[InlineAsm] method '{m.Name}' without a C# body: the body is required, it runs on the host and on targets without assembly for their CPU"
                    : $"[Interrupt] method '{m.Name}' without a body");
    }

    /// <summary>An operand's constraint letter: integers in data registers, pointers in address registers; null when the type cannot be an operand.</summary>
    private static string? AsmConstraint(ITypeSymbol t, bool ppc)
    {
        if (t is IPointerTypeSymbol) return ppc ? "b" : "a";
        if (t is INamedTypeSymbol { TypeKind: TypeKind.Enum } e) t = e.EnumUnderlyingType!;
        return t.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Char
                or SpecialType.System_IntPtr or SpecialType.System_UIntPtr => ppc ? "r" : "d",
            _ => null,
        };
    }

    private static bool IsAsmName(string s) =>
        s.Length > 0 && s.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_') && !char.IsDigit(s[0]);

    /// <summary>
    /// Checks an [InlineAsm] or [Interrupt] method. For [InlineAsm] with assembly for this target, writes the asm version and
    /// "#else": the caller then writes the C# body and "#endif". Returns true when it opened such a block.
    /// </summary>
    private bool BeginSpecialMethod(IMethodSymbol m, SyntaxNode decl)
    {
        if (IsInterrupt(m)) CheckInterrupt(m, decl);
        if (!IsInlineAsm(m)) return false;
        if (IsInterrupt(m)) { Unsupported(decl, "[InlineAsm] together with [Interrupt]; write the interrupt body in C# and call an [InlineAsm] method from it"); return false; }
        if (!m.IsStatic || m.MethodKind != MethodKind.Ordinary || m.IsGenericMethod)
        {
            Unsupported(decl, "[InlineAsm] on anything but a static, non-generic method");
            return false;
        }
        var asm = AsmFor(m);
        var ppc = asm?.Ppc ?? IsDefined("PPC");
        var ok = true;
        foreach (var p in m.Parameters)
        {
            if (p.RefKind != RefKind.None || AsmConstraint(p.Type, ppc) == null)
            {
                Unsupported(decl, $"[InlineAsm] parameter '{p.Name}' of type {p.Type.ToDisplayString()}: only integers up to 32 bits, chars, enums and pointers (passed by value) can be asm operands");
                ok = false;
            }
            else if (!IsAsmName(p.Name) || p.Name == "result")
            {
                Unsupported(decl, $"[InlineAsm] parameter name '{p.Name}': operands are named after the parameters, so use a plain ASCII name other than 'result'");
                ok = false;
            }
        }
        if (!m.ReturnsVoid && (m.ReturnsByRef || m.ReturnsByRefReadonly || AsmConstraint(m.ReturnType, ppc) == null))
        {
            Unsupported(decl, $"[InlineAsm] result of type {m.ReturnType.ToDisplayString()}: only integers up to 32 bits, chars, enums and pointers");
            ok = false;
        }
        if (!ok || asm == null) return false;

        var (code, clobbers, isPpc) = asm.Value;
        var outputs = new List<string>();
        var inputs = new List<string>();
        _out.Append(isPpc ? "#if defined(__PPC__)\n" : "#if defined(__mc68000__)\n");
        if (!m.ReturnsVoid)
        {
            Line(Decl(m.ReturnType, "es_asm_result", decl) + ";");
            outputs.Add($"[result] \"=&{AsmConstraint(m.ReturnType, isPpc)}\"(es_asm_result)");
        }
        foreach (var p in m.Parameters) inputs.Add($"[{p.Name}] \"{AsmConstraint(p.Type, isPpc)}\"({p.Name})");
        var clobberList = (clobbers ?? "").Split(',').Select(c => c.Trim()).Where(c => c.Length > 0)
            .Select(c => CString(c)).Concat(new[] { "\"cc\"", "\"memory\"" });
        // One instruction per line; a tab after each newline keeps instructions out of the label column.
        var lines = code.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        var template = CString(string.Join("\n\t", lines));

        Line($"__asm__ __volatile__({template}");
        Line($"    : {string.Join(", ", outputs)}");
        Line($"    : {string.Join(", ", inputs)}");
        Line($"    : {string.Join(", ", clobberList)});");
        if (!m.ReturnsVoid) Line("return es_asm_result;");
        _out.Append("#else\n");
        _lastLineFile = null;
        return true;
    }

    private void EndSpecialMethod(bool opened)
    {
        if (opened) _out.Append("#endif\n");
    }

    private void CheckInterrupt(IMethodSymbol m, SyntaxNode decl)
    {
        if (IsDefined("PPC"))
        {
            Unsupported(decl, "[Interrupt] on PowerPC (AmigaOS 4, MorphOS): their interrupt servers use another calling convention, not done yet; keep the server under #if M68K || AROS");
            return;
        }
        if (IsDefined("SDL"))
        {
            Unsupported(decl, "[Interrupt] on the sdl target, which has no interrupt servers; keep it under #if AMIGA");
            return;
        }
        if (!m.IsStatic || m.MethodKind != MethodKind.Ordinary || m.IsGenericMethod)
        {
            Unsupported(decl, "[Interrupt] on anything but a static, non-generic method");
            return;
        }
        if (m.Parameters.Length > 1 || m.Parameters.Length == 1 &&
            (m.Parameters[0].RefKind != RefKind.None || m.Parameters[0].Type is not (IPointerTypeSymbol or { SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr })))
        {
            Unsupported(decl, "[Interrupt] method parameters: none, or one pointer (the server's is_Data)");
            return;
        }
        if (!m.ReturnsVoid && m.ReturnType.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            Unsupported(decl, "[Interrupt] method result: void (the server returns 0, so the chain goes on), int or uint");
            return;
        }
        if (!_interrupts.Contains(m, SymbolEqualityComparer.Default)) _interrupts.Add(m);
    }

    private static string IsrName(IMethodSymbol m) => FuncName(m) + "_isr";

    private static string IsrAddress(IMethodSymbol m) => FuncName(m) + "_ISR";

    private static string IsrEntry(IMethodSymbol m) => FuncName(m) + "_entry";

    /// <summary>Declarations of every [Interrupt] method's server function and the macro for its address (before any method uses it).</summary>
    private void EmitInterruptDeclarations(IEnumerable<IMethodSymbol> methods)
    {
        var list = methods.Where(IsInterrupt).ToList();
        if (list.Count == 0) return;
        foreach (var m in list)
        {
            Line($"int32_t {IsrName(m)}(void *es_data);");
            _out.Append("#if defined(__mc68000__)\n");
            Line($"extern void {IsrEntry(m)}(void);   /* exec interrupt server entry (end of file): is_Data in a1, result in d0 and the Z flag */");
            Line($"#define {IsrAddress(m)} ((void *){IsrEntry(m)})");
            _out.Append("#else\n");
            Line($"#define {IsrAddress(m)} ((void *){IsrName(m)})");
            _out.Append("#endif\n");
        }
        Line("");
    }

    /// <summary>After an [Interrupt] method: the C function exec (or the m68k entry) calls with is_Data.</summary>
    private void EmitInterruptServer(IMethodSymbol m, SyntaxNode decl)
    {
        if (!_interrupts.Contains(m, SymbolEqualityComparer.Default)) return;
        var arg = m.Parameters.Length == 1 ? $"({CType(m.Parameters[0].Type, decl)})es_data" : "";
        Line($"int32_t {IsrName(m)}(void *es_data)");
        Line("{");
        if (m.Parameters.Length == 0) Line("    (void)es_data;");
        Line(m.ReturnsVoid ? $"    {FuncName(m)}({arg});" : $"    return (int32_t){FuncName(m)}({arg});");
        if (m.ReturnsVoid) Line("    return 0;");
        Line("}");
        Line("");
    }

    /// <summary>
    /// The m68k entries of the [Interrupt] methods, last in the file (top-level asm switches to .text). exec calls a server
    /// with is_Data in a1 and may use d0/d1/a0/a1/a5/a6 freely; it looks at the Z flag of d0 to stop the chain. The C function
    /// keeps d2-d7/a2-a6 as the C convention wants, so the entry only passes a1 on the stack and sets Z from the result.
    /// </summary>
    private void EmitInterruptEntries()
    {
        if (_interrupts.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var m in _interrupts)
            sb.Append($"\\t.text\\n\\t.even\\n\\t.globl _{IsrEntry(m)}\\n_{IsrEntry(m)}:\\n\\tmove.l a1,-(sp)\\n\\tjsr _{IsrName(m)}\\n\\taddq.l #4,sp\\n\\ttst.l d0\\n\\trts\\n");
        Line("#if defined(__mc68000__)");
        Line($"__asm__(\"{sb}\");");
        Line("#endif");
    }

    /// <summary><c>&amp;M</c>: the address of an [Interrupt] method's server; other methods have no address in E#.</summary>
    private string? FunctionAddress(PrefixUnaryExpressionSyntax u)
    {
        if (!u.IsKind(SyntaxKind.AddressOfExpression)) return null;
        var info = Model(u).GetSymbolInfo(u.Operand);
        var m = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (m == null) return null;
        if (!IsInterrupt(m))
            return Unsupported(u, $"address of method '{m.Name}': only [Interrupt] methods have an address in E# (and it is only for exec, not to call)");
        return IsrAddress(m);
    }
}
