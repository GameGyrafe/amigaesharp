using Microsoft.CodeAnalysis;

namespace ESharp.Analyzers;

/// <summary>Diagnostic descriptors for the E# language subset.</summary>
public static class Descriptors
{
    private const string Category = "ESharp.Subset";

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Exceptions = Error(
        "ES0001", "Exceptions are not supported",
        "E# has no exceptions ('{0}'); use result codes instead");

    public static readonly DiagnosticDescriptor Linq = Error(
        "ES0002", "LINQ is not supported",
        "E# turns only simple LINQ over arrays into loops (Where, Select, Take, Skip, Count, Sum, Min, Max, Any, All, First, Last, Contains, Aggregate, simple queries): '{0}' is not supported; use a plain loop");

    public static readonly DiagnosticDescriptor FloatingPoint = Error(
        "ES0003", "Floating point is off by default",
        "'{0}' needs an FPU, which a stock Amiga lacks; use a fixed-point type, or set ESharpAllowFloat=true");

    public static readonly DiagnosticDescriptor AsyncAwait = Error(
        "ES0004", "async/await is not supported",
        "E# has no async/await ('{0}')");

    public static readonly DiagnosticDescriptor Dynamic = Error(
        "ES0005", "dynamic is not supported",
        "E# has no 'dynamic'");

    public static readonly DiagnosticDescriptor Reflection = Error(
        "ES0006", "Reflection is not supported",
        "E# has no reflection ('{0}')");

    public static readonly DiagnosticDescriptor HeapClass = Error(
        "ES0007", "This kind of class is not supported yet",
        "Class '{0}': {1}. Supported: static classes and plain heap classes (fields, properties, methods, constructors; single inheritance from another plain class of the program)");

    public static readonly DiagnosticDescriptor Generics = Error(
        "ES0008", "Generic classes and library generics are not supported",
        "'{0}' is generic: only generic structs and generic static methods declared in E# source are supported (they are monomorphised)");

    public static readonly DiagnosticDescriptor GenericDepth = Error(
        "ES0098", "Generic instantiation too deep",
        "Expanding generic '{0}' did not finish: recursive instantiation?");

    public static readonly DiagnosticDescriptor Interfaces = Error(
        "ES0009", "This kind of interface is not supported",
        "Interface '{0}': {1}. Supported: interfaces of plain methods, implemented by classes (variables of the interface type) or by structs and classes (generic constraints)");

    public static readonly DiagnosticDescriptor SlowArithmetic = new(
        "ES0100", "Slow arithmetic on 68020",
        "'{0}' is software 64-bit arithmetic on a 68020 and is slow",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LibraryTooOld = Error(
        "ES0101", "Library function is newer than the target's libraries",
        "'{0}' needs library version {1} but this target has version {2}");

    public static readonly DiagnosticDescriptor AmigaOnly = Error(
        "ES0102", "Amiga-only API",
        "'{0}' is Amiga-only and does not exist on target '{1}'; put the code in #if AMIGA");

    public static readonly DiagnosticDescriptor ReferenceField = Error(
        "ES0010", "Structs cannot hold references",
        "Field '{0}' of struct '{1}' is a reference type; E# structs have value semantics only");

    public static readonly DiagnosticDescriptor FixedArrays = Error(
        "ES0011", "Arrays must be fixed-size",
        "{0}");

    public static readonly DiagnosticDescriptor BorrowedDelegate = Error(
        "ES0012", "A borrowed delegate parameter cannot be kept",
        "'{0}' is a borrowed delegate parameter: the caller's closure may live on the caller's stack, so it cannot be {1}; mark the parameter [Owned] to take ownership of it");

    public static readonly DiagnosticDescriptor ClosureNeverFreed = new(
        "ES0013", "A stored closure is never freed",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChipBudgetExceeded = Error(
        "ES0030", "Chip RAM budget exceeded",
        "The program reserves {0} bytes of chip RAM at constant sizes; the budget is {1}");

    public static readonly DiagnosticDescriptor NoEntryPoint = Error(
        "ES0020", "No entry point",
        "No method is marked [EntryPoint]");

    public static readonly DiagnosticDescriptor MultipleEntryPoints = Error(
        "ES0021", "Multiple entry points",
        "More than one method is marked [EntryPoint]");

    public static readonly DiagnosticDescriptor BadEntryPoint = Error(
        "ES0022", "Entry point signature",
        "The [EntryPoint] method must be static, return void or int and take no parameters or one string[] (args): {0}");

    public static readonly DiagnosticDescriptor Unsupported = Error(
        "ES0099", "Construct not supported by the emitter",
        "'{0}' is not supported by the E# C emitter yet");

    public static System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> SubsetRules =>
        System.Collections.Immutable.ImmutableArray.Create(
            Exceptions, Linq, FloatingPoint, AsyncAwait, Dynamic, Reflection,
            HeapClass, Generics, Interfaces, SlowArithmetic, LibraryTooOld, ReferenceField, FixedArrays,
            BorrowedDelegate, ClosureNeverFreed);
}
