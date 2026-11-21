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

    /// <summary>Test hook: name the program files as library files (no ES0103 for internal library members).</summary>
    internal bool UserFilesAreLibrary { get; init; }
}
