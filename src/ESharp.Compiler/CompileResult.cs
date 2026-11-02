using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ESharp.Compiler;

public sealed class CompileResult
{
    public CompileResult(string? c, ImmutableArray<Diagnostic> diagnostics)
    {
        C = c;
        Diagnostics = diagnostics;
    }

    /// <summary>The generated C, or null if there were errors.</summary>
    public string? C { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Chip RAM the program reserves where the sizes are constants (68k only).</summary>
    public System.Collections.Generic.IReadOnlyList<ChipEntry> Chip { get; init; } = System.Array.Empty<ChipEntry>();

    public bool Success => C != null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
