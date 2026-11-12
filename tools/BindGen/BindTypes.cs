using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>What a value is at the machine level, for comparing a C type with a C# one.</summary>
internal enum ValueKind { Void, Pointer, PointerSizedInt, Int8, Int16, Int32, Int64, Float, Double, Unknown }

/// <summary>
/// The type map from Amiga C types to the C# types E# bindings use (the style of src/ESharp.Platform): pointers to
/// structs are <c>void*</c> (E# has no declarations of the system structs; the fields it reads come from Layout.g.cs),
/// <c>STRPTR</c> is <c>byte*</c> (the library may write to it), <c>CONST_STRPTR</c> and other read-only text is
/// <c>string</c>, <c>BPTR</c>/<c>IPTR</c> are <c>nuint</c>, <c>BOOL</c> is <c>int</c> and tag lists are <c>void*</c>
/// (written with <c>TagWriter</c>). An unknown typedef maps to <c>nuint</c> with a marker comment.
/// </summary>
internal static class BindTypes
{
    private static readonly Dictionary<string, string> Scalars = new()
    {
        ["VOID"] = "void", ["void"] = "void",
        ["LONG"] = "int", ["int"] = "int", ["signed int"] = "int", ["signed"] = "int", ["LONGBITS"] = "uint",
        ["ULONG"] = "uint", ["unsigned int"] = "uint", ["unsigned"] = "uint", ["Tag"] = "uint", ["ULONGBITS"] = "uint",
        ["WORD"] = "short", ["SHORT"] = "short", ["short"] = "short", ["COUNT"] = "short",
        ["UWORD"] = "ushort", ["USHORT"] = "ushort", ["unsigned short"] = "ushort", ["UCOUNT"] = "ushort", ["WORDBITS"] = "ushort",
        ["BYTE"] = "sbyte", ["char"] = "sbyte", ["signed char"] = "sbyte",
        ["UBYTE"] = "byte", ["unsigned char"] = "byte", ["TEXT"] = "byte", ["BYTEBITS"] = "byte",
        ["BOOL"] = "int",
        ["APTR"] = "void*", ["CONST_APTR"] = "void*", ["RAWARG"] = "void*",
        ["STRPTR"] = "byte*", ["CONST_STRPTR"] = "string", ["PLANEPTR"] = "byte*", ["ClassID"] = "string",
        ["BPTR"] = "nuint", ["BSTR"] = "nuint", ["IPTR"] = "nuint", ["SIPTR"] = "nint",
        ["FLOAT"] = "float", ["float"] = "float", ["DOUBLE"] = "double", ["double"] = "double",
        ["QUAD"] = "long", ["UQUAD"] = "ulong",
        ["DisplayInfoHandle"] = "void*", ["Msg"] = "void*",
    };

    // Pointer element types that keep their own pointer type (ULONG * -> uint*); everything else is void*.
    private static readonly HashSet<string> TypedElements = new() { "int", "uint", "short", "ushort", "sbyte", "byte", "nuint", "nint", "void*", "byte*", "float", "double", "long", "ulong" };

    public static bool IsTypeWord(string w) => Scalars.ContainsKey(w) || w is "long" or "struct" or "union" or "unsigned" or "signed" or "const" or "CONST";

    /// <summary>The C type without qualifiers, with one space before the stars: "struct Screen *".</summary>
    public static string Normalize(string c, out bool isConst)
    {
        isConst = Regex.IsMatch(c, @"\b(CONST|const)\b");
        string t = Regex.Replace(c, @"\b(CONST|const|volatile|register)\b", " ");
        t = Regex.Replace(t, @"\s*\*", " *");
        t = Regex.Replace(t, @"\* \*", "**");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        while (t.Contains("* *")) t = t.Replace("* *", "**");
        return t;
    }

    /// <summary>The C# type for a C type, and whether the map knew it. An empty C type (no prototype) is unknown.</summary>
    public static (string Cs, bool Known) Map(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return ("nuint /* no prototype */", false);
        if (c.Contains('(')) return ("void*", true);   // a function pointer
        string t = Normalize(c, out bool isConst);
        int stars = t.Count(ch => ch == '*');
        string bas = t.TrimEnd('*', ' ');
        if (stars == 0)
        {
            if (Scalars.TryGetValue(bas, out var s)) return (s, true);
            if (bas == "long") return ("int", true);                   // 32-bit on the Amiga targets E# reads prototypes for
            if (bas == "unsigned long") return ("uint", true);
            if (bas.StartsWith("struct ") || bas.StartsWith("union ")) return ("nuint /* " + bas + " by value */", false);
            return ("nuint /* " + bas + " */", false);
        }
        // Text the library only reads is a C# string; text it may write to stays a byte pointer.
        if (stars == 1 && bas is "UBYTE" or "char" or "TEXT" or "unsigned char" or "BYTE") return (isConst ? "string" : "byte*", true);
        // A struct, union or unknown typedef is opaque: "struct Screen *" is void*, "struct Gadget **" is void**.
        string elem = Scalars.TryGetValue(bas, out var e) ? e : bas == "long" ? "int" : bas == "unsigned long" ? "uint" : "void";
        if (elem == "string") elem = "byte*";
        if (elem != "void" && !TypedElements.Contains(elem)) elem = "void";
        return (elem + new string('*', stars), true);
    }

    public static ValueKind KindOfCs(string cs)
    {
        cs = cs.Trim();
        if (cs.EndsWith('*') || cs == "string") return ValueKind.Pointer;
        return cs switch
        {
            "void" => ValueKind.Void,
            "nuint" or "nint" => ValueKind.PointerSizedInt,
            "sbyte" or "byte" or "bool" => ValueKind.Int8,
            "short" or "ushort" or "char" => ValueKind.Int16,
            "int" or "uint" => ValueKind.Int32,
            "long" or "ulong" => ValueKind.Int64,
            "float" => ValueKind.Float,
            "double" => ValueKind.Double,
            _ => ValueKind.Unknown,
        };
    }

    /// <summary>The machine kind of a C type (BOOL is 16-bit, though the binding uses int).</summary>
    public static ValueKind KindOfC(string c)
    {
        var (cs, known) = Map(c);
        if (!known) return ValueKind.Unknown;
        string t = Normalize(c, out _);
        if (t == "BOOL") return ValueKind.Int16;
        if (t is "IPTR" or "SIPTR") return ValueKind.Int64;   // an integer as wide as a pointer: 64-bit on AROS x86-64
        return KindOfCs(cs);
    }

    private static int Size(ValueKind k) => k switch { ValueKind.Int8 => 1, ValueKind.Int16 => 2, ValueKind.Int32 => 4, ValueKind.Int64 => 8, _ => 0 };

    private static bool IsInt(ValueKind k) => Size(k) > 0;

    /// <summary>
    /// Compares the C# type of a hand-written binding with the C type of the prototype. Null when they agree. Otherwise
    /// the difference, and <paramref name="problem"/> is true when it changes what is passed or returned (pointer against
    /// integer, a truncated result, a string where the library writes, a pointer-sized value in 32 bits); a widening or a
    /// different signedness or pointer element type is only a note.
    /// </summary>
    public static string? Compare(string cType, string cs, bool isReturn, out bool problem)
    {
        problem = false;
        var (gen, known) = Map(cType);
        if (!known) return null;   // nothing to compare against
        cs = Regex.Replace(cs, @"\s+", "");
        if (cs == gen) return null;
        var ck = KindOfC(cType);
        var hk = KindOfCs(cs);
        // nuint/nint against an integer type: compare as the widest case (64-bit). Against BPTR it is an address.
        if (hk == ValueKind.PointerSizedInt && IsInt(ck)) hk = ValueKind.Int64;
        string what = $"C# {cs}, prototype {Normalize(cType, out _)} (generator: {gen})";
        if (ck == ValueKind.Pointer && hk == ValueKind.Pointer)
        {
            if (cs == "string" && gen == "byte*") { problem = true; return what + ": a string where the library may write"; }
            if (cs == "void*" || gen == "void*") return null;   // void* for any pointer is the platform's style
            return what;
        }
        if (ck == hk)
        {
            if (ck == ValueKind.Int16 && cs == "int" && gen == "int") return null;   // BOOL
            return what + ": signedness";
        }
        if (isReturn && hk == ValueKind.Void) return what + ": result ignored";
        if (isReturn && ck == ValueKind.Void) { problem = true; return what + ": the function returns nothing"; }
        if (IsInt(ck) && IsInt(hk))
        {
            if (isReturn && Size(hk) < Size(ck)) { problem = true; return what + ": result truncated"; }
            if (!isReturn && Size(hk) > Size(ck)) return what + ": wider than the parameter (truncated when passed)";
            return what + ": " + (isReturn ? "widened" : "narrower than the parameter");
        }
        problem = true;
        return what + ": " + (ck, hk) switch
        {
            (ValueKind.Pointer, _) or (_, ValueKind.Pointer) => "pointer against non-pointer",
            (ValueKind.PointerSizedInt, _) or (_, ValueKind.PointerSizedInt) => "pointer-sized against fixed-size (64-bit targets)",
            _ => "different kinds",
        };
    }
}
