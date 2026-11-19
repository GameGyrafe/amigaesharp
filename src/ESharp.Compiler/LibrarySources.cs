using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ESharp.Compiler;

/// <summary>Runtime and platform sources embedded in the compiler assembly.</summary>
internal static class LibrarySources
{
    /// <summary>The paths the library files are parsed under (resource names, ESharp.Compiler.csproj), for the analyzer: ';'-separated prefixes.</summary>
    public const string PathPrefixes = Root + "Runtime/;" + Root + "Platform/";

    /// <summary>
    /// Starts every library path. No path the CLI builds from a project file (relative or absolute) starts with it, and
    /// <see cref="UserPath"/> makes sure of that for any caller.
    /// </summary>
    public const string Root = "<esharp-lib>/";

    /// <summary>A program file's path, kept out of the library's namespace of paths.</summary>
    public static string UserPath(string path) => path.StartsWith(Root, System.StringComparison.Ordinal) ? "./" + path : path;

    public static IEnumerable<SourceFile> Load()
    {
        var asm = typeof(LibrarySources).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(Root)) continue;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return new SourceFile(name, reader.ReadToEnd());
        }
    }

    /// <summary>A C runtime file (CRuntime/*.h) that the emitter pastes into the generated C.</summary>
    public static string CRuntime(string fileName)
    {
        using var stream = typeof(LibrarySources).Assembly.GetManifestResourceStream("crt/" + fileName)
            ?? throw new System.InvalidOperationException("missing embedded C runtime " + fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
