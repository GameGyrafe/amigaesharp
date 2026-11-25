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

    /// <summary>
    /// The packages' C files and headers for this target (<see cref="PackageNatives.Outputs"/>): the build writes each one at its
    /// path under the output folder and compiles those marked <see cref="NativeOutput.Compile"/> with the program's C.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<NativeOutput> Native { get; init; } = System.Array.Empty<NativeOutput>();

    /// <summary>Library mode: what the binary's manifest records (init function, generic copies, runtime pieces, library bases).</summary>
    public LibraryBuildInfo? Library { get; init; }

    /// <summary>The packages' binaries the program links instead of their sources (for the C compiler command, after the program's C).</summary>
    public System.Collections.Generic.IReadOnlyList<PackageBinary> Linked { get; init; } = System.Array.Empty<PackageBinary>();

    /// <summary>An Amiga library build: its .fd file, C# binding and function table.</summary>
    public AmigaLibraryOutput? AmigaLibrary { get; init; }

    public bool Success => C != null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
