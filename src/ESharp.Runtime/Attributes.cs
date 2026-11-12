using System;

namespace ESharp;

/// <summary>Marks the program's Main method.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EntryPointAttribute : Attribute
{
}

/// <summary>Binds an extern method to an AmigaOS library call (library name, LVO offset).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AmigaLibAttribute : Attribute
{
    public AmigaLibAttribute(string library, int offset)
    {
        Library = library;
        Offset = offset;
    }

    public string Library { get; }
    public int Offset { get; }
}

/// <summary>
/// The library version in which an extern library function first appeared (the "since" version from the
/// system's own interface data). A call to it is a compile error (ES0101) on a target whose libraries are
/// older: m68k builds target Kickstart 3.1, library version 40.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SinceAttribute : Attribute
{
    public SinceAttribute(int libraryVersion) { LibraryVersion = libraryVersion; }

    public int LibraryVersion { get; }
}

/// <summary>
/// Opts code in to library functions newer than the target's baseline (ES0101 otherwise). On a method, a type or
/// (as <c>[assembly: ...]</c>) the whole program it says: "this code runs only where library
/// <paramref name="library"/> (short name such as <c>"graphics"</c>, or <c>"graphics.library"</c>) is at least
/// <paramref name="version"/>". The program must make that true itself, for example by opening the library with
/// <c>Exec.OpenLibrary("graphics.library", version)</c> and returning early on null; the attribute only silences
/// the compile-time check for calls whose <see cref="SinceAttribute"/> is at most <paramref name="version"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresLibAttribute : Attribute
{
    public RequiresLibAttribute(string library, int version) { Library = library; Version = version; }

    public string Library { get; }
    public int Version { get; }
}

/// <summary>
/// The [AmigaLib] calls in this class go through a library base that the program opens itself (a library on disk
/// that may be missing, or a device), not one the startup code opens. <c>SetBase(void*)</c> in the class is an
/// emitter intrinsic that stores the base; it must be set before any call. <paramref name="baseName"/> is the
/// base's usual C name (renamed in the generated C, so the startup code never tries to open it) and
/// <paramref name="baseType"/> its C type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LibraryBaseAttribute : Attribute
{
    public LibraryBaseAttribute(string library, string baseName, string baseType) { Library = library; BaseName = baseName; BaseType = baseType; }

    public string Library { get; }
    public string BaseName { get; }
    public string BaseType { get; }
}

/// <summary>Places data where the custom chips can reach it (chip RAM).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ChipRamAttribute : Attribute
{
}

/// <summary>
/// An extern method that is a C function the emitter itself defines in the generated file's prelude (no library header, no body in E#).
/// Used for operations a single machine instruction does well, such as the 68020 64/32 divide.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeAttribute : Attribute
{
}

/// <summary>
/// A static, parameterless method the program calls after its entry point returns, when it uses anything else of the
/// method's class. Library code uses it to give back what it opened on demand (a device, a message port) without asking every
/// program to close it. Not called when the program ends some other way (exit, a crash).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AtExitAttribute : Attribute
{
}

/// <summary>
/// An exec interrupt server: a static method <c>void M(void* data)</c> (or <c>int</c>/<c>uint</c> returning, or with no
/// parameter). Its address <c>&amp;M</c> (a <c>delegate*&lt;void*, void&gt;</c>, or the type of its own signature) is what
/// <c>Interrupts.AddVBlank</c> and exec's AddIntServer take. On m68k the emitter adds an entry that follows exec's
/// convention (is_Data in a1, the result in d0 with the Z flag set from it: a void method returns 0, so the next server
/// in the chain runs); on AROS the method is called as a plain C function. PowerPC (AmigaOS 4, MorphOS) and sdl report
/// ES0099. The body runs in interrupt context: keep it short, do not allocate, call no library that may wait, and make
/// fields it shares with the main program <c>volatile</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InterruptAttribute : Attribute
{
}

/// <summary>
/// Escape hatch: on m68k (and on PowerPC when <see cref="Ppc"/> is given) the static method's body is the given GCC
/// inline assembly instead of its C# body; the C# body still runs on the host, sdl, AROS and any target without assembly
/// for its CPU, so it is required. Operands: the result (a non-void method) is <c>%[result]</c> (also <c>%0</c>), an
/// output register the assembly must set; each parameter is <c>%[name]</c> (also <c>%1</c>, <c>%2</c>, ... after the
/// result), an input register that must not be changed. Integers up to 32 bits, chars and enums go in data registers
/// (m68k <c>d</c>, PowerPC <c>r</c>), pointers in address registers (m68k <c>a</c>, PowerPC <c>b</c>); use the size suffix
/// of the C# type (<c>.b</c>, <c>.w</c>, <c>.l</c>). Condition codes and memory are always taken as changed; name any
/// other register the code changes in <see cref="Clobbers"/> (m68k) or <see cref="PpcClobbers"/>. A literal <c>%</c> is
/// written <c>%%</c>; lines are separated by newlines.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InlineAsmAttribute : Attribute
{
    public InlineAsmAttribute(string code) => Code = code;

    /// <summary>The 68020 assembly (Motorola syntax as the m68k-amigaos assembler takes it).</summary>
    public string Code { get; }

    /// <summary>PowerPC assembly for AmigaOS 4 and MorphOS; without it those targets run the C# body.</summary>
    public string? Ppc { get; set; }

    /// <summary>Registers the m68k code changes besides its operands, comma separated (for example <c>"d1,a0"</c>).</summary>
    public string? Clobbers { get; set; }

    /// <summary>Registers the PowerPC code changes besides its operands, comma separated (for example <c>"r0,r11"</c>).</summary>
    public string? PpcClobbers { get; set; }
}
