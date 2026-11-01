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

/// <summary>Places data where the custom chips can reach it (chip RAM).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ChipRamAttribute : Attribute
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
