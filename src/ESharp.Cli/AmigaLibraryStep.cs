using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>
/// Amiga shared libraries from E# (issue #154). A project whose csproj sets <c>EsOutputType</c> to <c>AmigaLibrary</c> builds a <c>.library</c>
/// (m68k AmigaOS 3.x, AROS x86-64 or i386, or AmigaOS 4), its <c>.fd</c> file and a C# binding. Its other properties (literal values, as for the icon, IconStep):
/// <c>EsLibraryName</c> (default: the folder's name in lower case + .library), <c>EsLibraryVersion</c> and <c>EsLibraryRevision</c>
/// (default: the first two numbers of <c>Version</c>, else 1.0), <c>EsLibraryDate</c> (dd.mm.yyyy; default: today) and <c>EsLibraryBinding</c>
/// (the binding's C# struct). A program uses the library by naming its folder in <c>esharp.include</c>: on the host and on sdl the library's
/// sources are compiled into the program; on an Amiga target they are not, the binding calls the <c>.library</c>, which the build makes and
/// puts next to the program.
/// </summary>
internal static class AmigaLibraryStep
{
    public sealed record Project(string Dir, string ProjectName, AmigaLibraryOptions Options);

    /// <summary>The AmigaLibrary project in <paramref name="dir"/>, or null when its csproj (the first, by name) does not say so. Throws InvalidDataException on a bad value.</summary>
    public static Project? Read(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var csproj = Directory.GetFiles(dir, "*.csproj").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (csproj == null) return null;
        XDocument doc;
        try { doc = XDocument.Load(csproj); }
        catch (System.Xml.XmlException e) { throw new InvalidDataException($"{csproj}: {e.Message}"); }
        string? Prop(string n)
        {
            var v = doc.Descendants().Where(e => e.Name.LocalName == n).Select(e => e.Value.Trim()).LastOrDefault(x => x.Length > 0);
            if (v != null && v.Contains("$(")) throw new InvalidDataException($"{csproj}: {n} '{v}' uses an MSBuild property; esharp reads only literal values");
            return v;
        }
        var kind = Prop("EsOutputType");
        if (kind == null || kind.Equals("Exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (!kind.Equals("AmigaLibrary", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{csproj}: EsOutputType '{kind}' is not known (AmigaLibrary builds an Amiga shared library; leave it out for a program)");
        var folder = new DirectoryInfo(Path.GetFullPath(dir)).Name;
        int version = 1, revision = 0;
        if (Prop("Version") is { } v)
        {
            var parts = v.Split('.', '-', '+');
            if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var maj)) version = maj;
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var min)) revision = min;
        }
        if (Prop("EsLibraryVersion") is { } lv && !int.TryParse(lv, NumberStyles.None, CultureInfo.InvariantCulture, out version))
            throw new InvalidDataException($"{csproj}: EsLibraryVersion '{lv}' must be a number");
        if (Prop("EsLibraryRevision") is { } lr && !int.TryParse(lr, NumberStyles.None, CultureInfo.InvariantCulture, out revision))
            throw new InvalidDataException($"{csproj}: EsLibraryRevision '{lr}' must be a number");
        var options = new AmigaLibraryOptions
        {
            Name = Prop("EsLibraryName") ?? folder.ToLowerInvariant() + ".library",
            Version = version,
            Revision = revision,
            Date = Prop("EsLibraryDate") ?? DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture),
            BindingName = Prop("EsLibraryBinding"),
        };
        if (options.Problem() is { } problem) throw new InvalidDataException($"{csproj}: {problem}");
        return new Project(Path.GetFullPath(dir), Path.GetFileNameWithoutExtension(csproj), options);
    }

    /// <summary>The targets a .library builds for: m68k (AmigaOS 3.x), aros (AROS x86-64), aros-i386 and os4 (AmigaOS 4).</summary>
    public static bool BuildsFor(TargetInfo target) => target == Targets.M68k || target == Targets.Aros || target == Targets.ArosI386 || target == Targets.Os4;

    /// <summary>The AmigaLibrary projects among the folders that a program's <c>esharp.include</c> names.</summary>
    public static List<Project> Referenced(string programPath)
    {
        var result = new List<Project>();
        if (File.Exists(programPath)) programPath = Path.GetDirectoryName(Path.GetFullPath(programPath))!;
        var include = Path.Combine(programPath, "esharp.include");
        if (!File.Exists(include)) return result;
        foreach (var raw in File.ReadAllLines(include))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (Read(Path.Combine(programPath, line)) is { } p && result.All(r => r.Dir != p.Dir)) result.Add(p);
        }
        return result;
    }

    /// <summary>True when <paramref name="file"/> lies in the folder of <paramref name="project"/>.</summary>
    public static bool Contains(Project project, string file)
    {
        var rel = Path.GetRelativePath(project.Dir, Path.GetFullPath(file));
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }

    /// <summary>The library's sources (as esharp build collects a project's: obj/ and bin/ left out).</summary>
    public static List<SourceFile> Sources(Project project)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(project.Dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => sep + Path.GetRelativePath(project.Dir, f) is var rel && !rel.Contains($"{sep}obj{sep}") && !rel.Contains($"{sep}bin{sep}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new SourceFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f), File.ReadAllText(f)))
            .ToList();
    }

    /// <summary>
    /// Compiles the library for m68k in memory (no C compiler) and writes its binding to <paramref name="bindingDir"/>; returns the binding's
    /// path, or null with the errors printed.
    /// </summary>
    public static string? WriteBinding(Project project, string bindingDir)
    {
        var result = EsCompiler.Compile(Sources(project), new CompileOptions { Defines = Targets.M68k.Defines, TargetName = "m68k", AmigaLibrary = project.Options, CFileName = project.ProjectName + ".c" });
        if (!result.Success || result.AmigaLibrary == null)
        {
            foreach (var d in result.Diagnostics) Console.Error.WriteLine(d.ToString());
            Console.Error.WriteLine($"esharp: the library {project.Options.Name} in {project.Dir} has errors: no binding");
            return null;
        }
        Directory.CreateDirectory(bindingDir);
        var path = Path.Combine(bindingDir, project.Options.Binding + ".cs");
        File.WriteAllText(path, result.AmigaLibrary.Binding);
        return path;
    }
}
