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

/// <summary>Marks an interrupt handler.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InterruptAttribute : Attribute
{
}

/// <summary>Escape hatch: the method body is hand-written 68000 assembly.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InlineAsmAttribute : Attribute
{
    public InlineAsmAttribute(string code) => Code = code;

    public string Code { get; }
}
