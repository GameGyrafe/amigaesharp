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
}
