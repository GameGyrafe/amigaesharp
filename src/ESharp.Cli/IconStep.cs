using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ESharp.Assets;

namespace ESharp.Cli;

/// <summary>
/// The Workbench icon of a built Amiga program (issue #84): <c>esharp build</c> writes <c>&lt;name&gt;.info</c> next to the program, a Tool icon with a stack
/// size, tool types and a picture. The settings are MSBuild-style properties in the project's csproj (read as plain XML, no MSBuild
/// evaluation: literal values only; $(...), Condition and Directory.Build.props are not looked at): <c>EsStackSize</c> (bytes, default 65536), <c>EsToolTypes</c> (semicolon-separated, e.g. <c>SEED=42;AUTO</c>) and
/// <c>EsIcon</c> (a PNG relative to the project, at most 256 x 256; <c>none</c> writes no icon at all) and <c>EsNewIcons</c> (<c>false</c> leaves out the
/// NewIcons image in the tool types, issue #101). Without a PNG the icon is E#'s own default art.
/// </summary>
public static class IconStep
{
    public const int DefaultStackSize = 65536;

    public sealed record Settings(int StackSize, IReadOnlyList<string> ToolTypes, string? IconPng, bool Disabled, bool NewIcons = true);

    /// <summary>Reads the properties from the csproj in <paramref name="projectDir"/> (the first one, by name); defaults when there is none. Throws InvalidDataException for a bad value.</summary>
    public static Settings ReadSettings(string? projectDir)
    {
        var stack = DefaultStackSize;
        var types = new List<string>();
        string? png = null; var disabled = false; var newIcons = true;
        var csproj = projectDir != null && Directory.Exists(projectDir)
            ? Directory.GetFiles(projectDir, "*.csproj").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() : null;
        if (csproj != null)
        {
            XDocument doc;
            try { doc = XDocument.Load(csproj); }
            catch (System.Xml.XmlException e) { throw new InvalidDataException($"{csproj}: {e.Message}"); }
            string? Prop(string n) => doc.Descendants().Where(e => e.Name.LocalName == n).Select(e => e.Value.Trim()).LastOrDefault(v => v.Length > 0);
            foreach (var name in new[] { "EsStackSize", "EsToolTypes", "EsIcon", "EsNewIcons" })
                if (Prop(name) is { } v && v.Contains("$("))
                    throw new InvalidDataException($"{csproj}: {name} '{v}' uses an MSBuild property; esharp reads only literal values (no $(...), no Condition, no Directory.Build.props)");
            if (Prop("EsStackSize") is { } s)
            {
                if (!int.TryParse(s, out stack) || stack < 4096 || stack > 16 * 1024 * 1024)
                    throw new InvalidDataException($"{csproj}: EsStackSize '{s}' must be a number of bytes from 4096 to 16777216");
            }
            if (Prop("EsToolTypes") is { } t)
                types.AddRange(t.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0));
            if (Prop("EsNewIcons") is { } ni)
            {
                if (!bool.TryParse(ni, out newIcons))
                    throw new InvalidDataException($"{csproj}: EsNewIcons '{ni}' must be true or false");
            }
            if (Prop("EsIcon") is { } i)
            {
                if (i.Equals("none", StringComparison.OrdinalIgnoreCase)) disabled = true;
                else png = Path.GetFullPath(Path.Combine(projectDir!, i.Replace('\\', '/')));
            }
        }
        return new Settings(stack, types, png, disabled, newIcons);
    }

    /// <summary>The icon file bytes for <paramref name="settings"/>.</summary>
    public static byte[] Build(Settings settings)
    {
        var png = settings.IconPng != null ? File.ReadAllBytes(settings.IconPng) : null;
        var (classic, colour) = png != null ? IconArt.FromPng(png) : IconArt.Default();
        // The NewIcons image (issue #101) for Workbench 3.x with NewIcons: the default art as it is (7 colours), a PNG reduced again.
        var newIcons = !settings.NewIcons ? null : png != null ? IconArt.NewIconsFromPng(png) : colour;
        var icon = new DiskObject { Type = IconType.Tool, StackSize = settings.StackSize, Classic = classic, Colour = colour, NewIcons = newIcons };
        icon.ToolTypes.AddRange(settings.ToolTypes);
        return icon.Write();
    }

    /// <summary>Writes <c>&lt;outDir&gt;/&lt;name&gt;.info</c> for an Amiga target (nothing for sdl or when the project says EsIcon none). Returns the path, null when none was written, or throws on a bad setting.</summary>
    public static string? Write(string? projectDir, string outDir, string name, bool amiga)
    {
        if (!amiga) return null;
        var settings = ReadSettings(projectDir);
        if (settings.Disabled) return null;
        if (settings.IconPng != null && !File.Exists(settings.IconPng)) throw new InvalidDataException($"EsIcon: '{settings.IconPng}' does not exist");
        var path = Path.Combine(outDir, name + ".info");
        File.WriteAllBytes(path, Build(settings));
        return path;
    }
}
