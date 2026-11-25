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
        "Class '{0}': {1}. Supported: static classes and plain heap classes (fields, properties, methods, constructors; single inheritance from another plain class of the program or an [Extensible] library class)");

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

    public static readonly DiagnosticDescriptor LibraryInternal = Error(
        "ES0103", "Internal library member",
        "'{0}' is internal to E#'s library and cannot be used from a program (the dotnet host build rejects it too)");

    public static readonly DiagnosticDescriptor ReferenceField = Error(
        "ES0010", "Structs cannot hold references",
        "Field '{0}' of struct '{1}' is a reference type; E# structs have value semantics only");

    public static readonly DiagnosticDescriptor FixedArrays = Error(
        "ES0011", "Arrays must be fixed-size",
        "{0}");

    public static readonly DiagnosticDescriptor BorrowedDelegate = Error(
        "ES0012", "A borrowed delegate parameter cannot be kept",
        "'{0}' is a borrowed delegate parameter: the caller's closure may live on the caller's stack, so it cannot be {1}; mark the parameter [Owned] to take ownership of it");

    public static readonly DiagnosticDescriptor DelegateTypeArgument = Error(
        "ES0014", "Generic code over a delegate type",
        "'{0}' instantiates generic code with the delegate type '{1}': the closures it would hold or pass on have no owner E# can track; use an array of delegates, a class with delegate fields, or a delegate-typed parameter");

    public static readonly DiagnosticDescriptor ClosureNeverFreed = new(
        "ES0013", "A stored closure is never freed",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RemoveLambda = new(
        "ES0015", "Removing a lambda never matches",
        "'{0} -= <lambda>' never removes a handler: each lambda is a function of its own, so it equals no handler that was added; keep the handler in a variable or field, add that and remove the same variable",
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

    // E# library packages (issue #31). Reported by the compiler (esharp build), which sees the package sources; not by the IDE analyzer.

    public static readonly DiagnosticDescriptor PackageTarget = Error(
        "ES0110", "Package does not support the target",
        "Package '{0}' {1} does not support target '{2}' (it lists: {3})");

    public static readonly DiagnosticDescriptor PackageCopyleft = Error(
        "ES0111", "GPL/LGPL package",
        "Package '{0}' {1} has the licence '{2}' (GPL family): its code would be compiled into the program. Set <EsAllowCopyleft>true</EsAllowCopyleft> in the program's csproj to allow it");

    public static readonly DiagnosticDescriptor PackageSdk = Error(
        "ES0112", "Package needs a newer E#",
        "Package '{0}' {1} needs E# {2} or newer; this esharp is {3}");

    public static readonly DiagnosticDescriptor PackageFeature = Error(
        "ES0113", "Package uses a platform-level feature it does not declare",
        "Package '{0}' uses {1} ('{2}') but does not declare it; the library's csproj must list {1} in EsFeatures");

    public static readonly DiagnosticDescriptor PackageEntryPoint = Error(
        "ES0114", "Entry point in a package",
        "Package '{0}' declares an [EntryPoint] ('{1}'); a library cannot have one");

    public static readonly DiagnosticDescriptor PackageNameClash = Error(
        "ES0115", "Type declared by a package and by the program or another package",
        "'{0}' is declared in {1} and in {2}; a package and the program, or two packages, cannot declare the same type (use a namespace of your own)");

    public static readonly DiagnosticDescriptor PackageInternal = Error(
        "ES0116", "Internal package member",
        "'{0}' is internal to package '{1}' and cannot be used from {2} (the dotnet host build rejects it too)");

    public static readonly DiagnosticDescriptor PackageDerive = Error(
        "ES0117", "Class derived from a package class not marked [Extensible]",
        "'{0}' derives from '{1}' of package '{2}', which is not marked [Extensible]; only package classes made for it (marked [Extensible] in the package) can be base classes of another package's or the program's classes");

    public static readonly DiagnosticDescriptor PackageInvalid = Error(
        "ES0118", "Package cannot be used",
        "Package '{0}' {1}: {2}");

    public static readonly DiagnosticDescriptor PackageNativeTarget = Error(
        "ES0119", "Package has no C files for the target",
        "Package '{0}' {1} has native C, but none for target '{2}' (no .c file in its native/common or native/{2} folder); it has: {3}");

    public static readonly DiagnosticDescriptor PackageNativeName = Error(
        "ES0120", "Package C symbol without the package's prefix",
        "Package '{0}' {1}: {2} defines '{3}', which does not start with the package's native prefix '{4}'; every function and variable its C files export must start with it (or be static), so the C of two packages and of the program cannot clash");

    public static readonly DiagnosticDescriptor PackageNativeSignature = Error(
        "ES0121", "Package [Native] extern with a type C cannot be given",
        "Package '{0}': the [Native] extern '{1}' ({2}) has {3}; a package's own C function takes and returns only numbers, bool, string (const char *) and pointers to those or void (no ref, out, in, struct or generic types)");

    public static readonly DiagnosticDescriptor PackageNativePrefix = Error(
        "ES0122", "Two packages with overlapping native prefixes",
        "Packages '{0}' (native prefix '{1}') and '{2}' (native prefix '{3}') could export the same C names; give one of them another EsNativePrefix");

    public static readonly DiagnosticDescriptor PackageNativeForeign = Error(
        "ES0123", "[Native] extern for a package's C function",
        "The [Native] extern '{0}' names '{1}', a C function of package '{2}' (native prefix '{3}'); only the package declares its own C functions: call its C# method instead");

    public static readonly DiagnosticDescriptor PackageBinaryNotUsed = new(
        "ES0124", "Package compiled from its sources instead of its binary",
        "Package '{0}' {1} has precompiled binaries, but {2}; its sources are compiled with the program instead",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PackageNotPrecompilable = Error(
        "ES0125", "Package cannot be precompiled",
        "This package cannot be built as a binary for its targets: {0}; set EsPrecompile to false in the library project (it then ships its sources only)");

    public static System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> SubsetRules =>
        System.Collections.Immutable.ImmutableArray.Create(
            Exceptions, Linq, FloatingPoint, AsyncAwait, Dynamic, Reflection,
            HeapClass, Generics, Interfaces, SlowArithmetic, LibraryTooOld, ReferenceField, FixedArrays,
            BorrowedDelegate, ClosureNeverFreed, DelegateTypeArgument, RemoveLambda, LibraryInternal, PackageDerive);
}
