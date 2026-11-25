using System.Collections.Generic;

namespace ESharp.Compiler;

public sealed class SourceFile
{
    public SourceFile(string path, string text)
    {
        Path = path;
        Text = text;
    }

    /// <summary>Path as it should appear in #line directives and diagnostics.</summary>
    public string Path { get; }
    public string Text { get; }
}

public sealed class CompileOptions
{
    /// <summary>Conditional-compilation symbols. Defaults to AMIGA.</summary>
    public IReadOnlyList<string> Defines { get; init; } = new[] { "AMIGA" };

    /// <summary>Allow float/double (needs an FPU or a software float library).</summary>
    public bool AllowFloat { get; init; }

    /// <summary>Check array and span indexes at run time; a bad index prints its source line and exits with 20.</summary>
    public bool BoundsChecks { get; init; }

    /// <summary>Fail the build when the constant chip RAM reservations exceed this many bytes (68k only).</summary>
    public long? ChipBudget { get; init; }

    /// <summary>The generated C file's name, for the #line directives that point back at it after each user function (default program.c).</summary>
    public string? CFileName { get; init; }

    /// <summary>E# library packages compiled with the program (from the project's restore, see <see cref="PackageLoader"/>).</summary>
    public IReadOnlyList<EsPackage> Packages { get; init; } = System.Array.Empty<EsPackage>();

    /// <summary>The target's name (m68k, os4, ...), checked against the targets each package lists; null skips that check.</summary>
    public string? TargetName { get; init; }

    /// <summary>Allow packages under a GPL-family licence (the program's EsAllowCopyleft property).</summary>
    public bool AllowCopyleft { get; init; }

    /// <summary>The E# version building the program, checked against each package's minimum; null skips that check.</summary>
    public string? SdkVersion { get; init; }

    /// <summary>
    /// Library mode (<c>esharp precompile</c>, issue #154): no program sources; the C of this package (the only one in <see cref="Packages"/>: a
    /// package with E# dependencies is not precompiled, error ES0125) for its binary, with an init function instead of main (see <see cref="CompileResult.Library"/>).
    /// </summary>
    public EsPackage? LibraryBuild { get; init; }

    /// <summary>Link a package's precompiled binary for the target instead of compiling its sources, where one fits (see <see cref="Stamp"/>).</summary>
    public bool UseBinaries { get; init; } = true;

    /// <summary>This E#'s stamp (<see cref="PackageBinaries.Stamp"/>): a binary made by another E# is not used. Null: no binary is used.</summary>
    public string? Stamp { get; init; }

    /// <summary>
    /// An Amiga shared library build (<c>EsOutputType</c> <c>AmigaLibrary</c>, issue #154): no entry point; the C is a <c>.library</c> whose
    /// functions are the program's [LibraryFunction] methods, each opener with its own copy of the E# data (m68k AmigaOS 3.x only for now).
    /// </summary>
    public AmigaLibraryOptions? AmigaLibrary { get; init; }

    /// <summary>Test hook: name the program files as library files (no ES0103 for internal library members).</summary>
    internal bool UserFilesAreLibrary { get; init; }
}
