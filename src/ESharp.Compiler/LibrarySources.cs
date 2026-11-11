using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ESharp.Compiler;

/// <summary>Runtime and platform sources embedded in the compiler assembly.</summary>
internal static class LibrarySources
{
    public static IEnumerable<SourceFile> Load()
    {
        var asm = typeof(LibrarySources).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("lib/")) continue;
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
