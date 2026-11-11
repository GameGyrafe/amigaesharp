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
