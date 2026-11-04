using System;
using System.Collections.Generic;
using System.Linq;

namespace ESharp.Compiler;

/// <summary>
/// A build target: which conditional symbols the source is parsed with, and how the generated C is
/// compiled. CPU and OS are separate axes (68k vs PowerPC, AmigaOS 3 vs 4 vs MorphOS), and the
/// umbrella symbol AMIGA is defined for every Amiga-family target, so shared code can use
/// <c>#if AMIGA</c> and narrow with <c>M68K</c>, <c>PPC</c>, <c>AMIGAOS3</c>, <c>AMIGAOS4</c> or <c>MORPHOS</c>.
/// </summary>
public sealed class TargetInfo
{
    public TargetInfo(string name, string description, string[] defines, string compiler,
        string cflags, string libs = "")
    {
        Name = name;
        Description = description;
        Defines = defines;
        Compiler = compiler;
        CFlags = cflags;
        Libs = libs;
    }

    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<string> Defines { get; }
    public string Compiler { get; }
    public string CFlags { get; }

    /// <summary>Linker libraries, placed after the source file.</summary>
    public string Libs { get; }

    public bool IsM68k => Defines.Contains("M68K");

    /// <summary>With <paramref name="debug"/>, defines DEBUG (poisoned arenas) and turns bounds checks on.</summary>
    public CompileOptions ToOptions(bool allowFloat = false, bool debug = false, long? chipBudget = null) =>
        new()
        {
            Defines = debug ? Defines.Append("DEBUG").ToArray() : Defines,
            AllowFloat = allowFloat,
            BoundsChecks = debug,
            ChipBudget = chipBudget,
        };
}

public static class Targets
{
    public static readonly TargetInfo M68k = new(
        "m68k", "AmigaOS 3.1+ on 68020+ (amiga-gcc)",
        new[] { "AMIGA", "M68K", "AMIGAOS3" },
        "m68k-amigaos-gcc", "-noixemul -m68020 -O2 -fwrapv");

    public static readonly TargetInfo Os4 = new(
        "os4", "AmigaOS 4 on PowerPC",
        new[] { "AMIGA", "PPC", "AMIGAOS4" },
        "ppc-amigaos-gcc", "-O2 -fwrapv -D__USE_INLINE__", "-lauto");

    public static readonly TargetInfo MorphOs = new(
        "morphos", "MorphOS on PowerPC",
        new[] { "AMIGA", "PPC", "MORPHOS" },
        "ppc-morphos-gcc", "-noixemul -O2 -fwrapv");

    public static readonly TargetInfo Aros = new(
        "aros", "AROS on x86-64 (64-bit pointers)",
        new[] { "AMIGA", "X86_64", "AROS" },
        "x86_64-aros-gcc", "--sysroot=/opt/x86_64-aros -O2 -fwrapv");

    public static IReadOnlyList<TargetInfo> All { get; } = new[] { M68k, Os4, MorphOs, Aros };

    public static TargetInfo? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
