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
        "E# has no LINQ ('{0}'); use plain loops");

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
        "ES0007", "Classes are not supported yet",
        "Class '{0}' must be static: heap classes arrive in language phase 2");

    public static readonly DiagnosticDescriptor Generics = Error(
        "ES0008", "Generics are not supported yet",
        "'{0}' is generic: monomorphised generics arrive in language phase 2");

    public static readonly DiagnosticDescriptor Interfaces = Error(
        "ES0009", "Interfaces are not supported yet",
        "Interface '{0}': static-dispatch interfaces arrive in language phase 2");

    public static readonly DiagnosticDescriptor SlowArithmetic = new(
        "ES0100", "Slow arithmetic on 68000",
        "'{0}' is software 64-bit arithmetic on a 68000 and is slow",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReferenceField = Error(
        "ES0010", "Structs cannot hold references",
        "Field '{0}' of struct '{1}' is a reference type; E# structs have value semantics only");

    public static readonly DiagnosticDescriptor NoEntryPoint = Error(
        "ES0020", "No entry point",
        "No method is marked [EntryPoint]");

    public static readonly DiagnosticDescriptor MultipleEntryPoints = Error(
        "ES0021", "Multiple entry points",
        "More than one method is marked [EntryPoint]");

    public static readonly DiagnosticDescriptor Unsupported = Error(
        "ES0099", "Construct not supported by the emitter",
        "'{0}' is not supported by the E# C emitter yet");

    public static System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> SubsetRules =>
        System.Collections.Immutable.ImmutableArray.Create(
            Exceptions, Linq, FloatingPoint, AsyncAwait, Dynamic, Reflection,
            HeapClass, Generics, Interfaces, SlowArithmetic, ReferenceField);
}
