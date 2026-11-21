using System;
using System.Collections.Generic;
using System.Linq;

namespace ESharp.Compiler;

/// <summary>
/// A build target: which conditional symbols the source is parsed with, and how the generated C is
/// compiled. CPU and OS are separate axes (68k vs PowerPC, AmigaOS 3 vs 4 vs MorphOS), and the
/// umbrella symbol AMIGA is defined for every Amiga-family target, so shared code can use
/// <c>#if AMIGA</c> and narrow with <c>M68K</c>, <c>PPC</c>, <c>AMIGAOS3</c>, <c>AMIGAOS4</c> or <c>MORPHOS</c>.
/// The desktop target <c>sdl</c> defines <c>SDL</c> and not <c>AMIGA</c>: the platform layer runs on SDL 3 there, and
/// the Amiga-only APIs (GadTools, ASL, iffparse, diskfont, lowlevel) do not exist. The dotnet test host defines <c>HOST</c>.
/// </summary>
public sealed class TargetInfo
{
    public TargetInfo(string name, string description, string[] defines, string compiler,
        string cflags, string libs = "", string pkgConfig = "", string sysroot = "", string version = "")
    {
        PkgConfig = pkgConfig;
        Sysroot = sysroot;
        CompilerVersion = version;
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

    /// <summary>
    /// The compiler's <c>--sysroot</c> in the pinned Docker image (AROS only), or empty. A local toolchain gives its own
    /// (<c>--sysroot</c>, <c>ESHARP_SYSROOT_*</c>, or a folder under <c>--toolchain</c>); see the CLI's Toolchain class.
    /// </summary>
    public string Sysroot { get; }

    /// <summary>What the pinned image's compiler prints for <c>-dumpversion</c>, or empty when any version will do (sdl).</summary>
    public string CompilerVersion { get; }

    /// <summary>pkg-config packages linked statically (<c>pkg-config --static --cflags --libs</c>), or empty.</summary>
    public string PkgConfig { get; }

    public bool IsM68k => Defines.Contains("M68K");

    /// <summary>An Amiga-family target (AMIGA defined): built for FS-UAE, real hardware or AROS.</summary>
    public bool IsAmiga => Defines.Contains("AMIGA");

    /// <summary>With <paramref name="debug"/>, defines DEBUG (poisoned arenas) and turns bounds checks on.</summary>
    public CompileOptions ToOptions(bool allowFloat = false, bool debug = false, long? chipBudget = null, bool trace = false) =>
        new()
        {
            // --debug implies TRACE: a debug build wants its serial trace.
            Defines = (debug ? Defines.Append("DEBUG") : Defines).Concat(debug || trace ? new[] { "TRACE" } : new string[0]).ToArray(),
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
        "m68k-amigaos-gcc", "-noixemul -m68020 -O2 -fwrapv", version: "6.5.0b");

    public static readonly TargetInfo Os4 = new(
        "os4", "AmigaOS 4 on PowerPC",
        new[] { "AMIGA", "PPC", "AMIGAOS4" },
        "ppc-amigaos-gcc", "-O2 -fwrapv -D__USE_INLINE__", "-lauto", version: "11.5.0");

    public static readonly TargetInfo MorphOs = new(
        "morphos", "MorphOS on PowerPC",
        new[] { "AMIGA", "PPC", "MORPHOS" },
        "ppc-morphos-gcc", "-noixemul -O2 -fwrapv", version: "15.2.0");

    public static readonly TargetInfo Aros = new(
        "aros", "AROS on x86-64 (64-bit pointers)",
        new[] { "AMIGA", "X86_64", "AROS" },
        "x86_64-aros-gcc", "-O2 -fwrapv", sysroot: "/opt/x86_64-aros", version: "9.1.0");

    /// <summary>
    /// AROS on 32-bit x86 (i386 ABI, as the linux-i386 hosted and pc-i386 native builds): 32-bit pointers, so the
    /// 32-bit struct layouts apply (measured for i386, see Layout.g.cs). Built in the esharp-aros-i386 image (docker/aros-i386).
    /// </summary>
    public static readonly TargetInfo ArosI386 = new(
        "aros-i386", "AROS on i386 (32-bit pointers)",
        new[] { "AMIGA", "I386", "AROS" },
        "i386-aros-gcc", "-O2 -fwrapv", sysroot: "/opt/i386-aros/sdk", version: "6.5.0");

    /// <summary>
    /// A desktop build through SDL 3 (Linux x86-64 in the esharp-sdl image; SDL is linked statically). Defines SDL and
    /// no CPU or OS symbol: the C is plain C99 plus SDL, so the same source should build on macOS and Windows too (not verified).
    /// </summary>
    public static readonly TargetInfo Sdl = new(
        "sdl", "Desktop through SDL 3 (Linux x86-64 in the esharp-sdl image)",
        new[] { "SDL" },
        "gcc", "-O2 -fwrapv", pkgConfig: "sdl3");

    public static IReadOnlyList<TargetInfo> All { get; } = new[] { M68k, Os4, MorphOs, Aros, ArosI386, Sdl };

    public static TargetInfo? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
