namespace ESharp.Platform;

/// <summary>
/// The program's command-line arguments, without the program name: <c>Game SEED 42 AUTO</c> from a Shell gives Count 3. They are the
/// C <c>argv</c> words (the startup code splits the line at spaces and keeps "quoted text" together). A program started from
/// Workbench gets its icon's tool types instead (<c>SEED=42</c>, <c>AUTO</c>; a value in quotes loses them as in a Shell; an entry in parentheses is switched
/// off and skipped, as are empty ones and Workbench's own TOOLPRI, STARTPRI, DONOTWAIT, TOOLWINDOW and WINDOW; at most 64 words and about 2 KB in all, the rest is dropped). Matching of names is ASCII case-insensitive, as AmigaDOS does.
/// <c>esharp run --args "SEED 42"</c> passes a line to the program in FS-UAE, on SDL and in the os4 VM; on the dotnet host Args is the real command line
/// (<c>HostSet</c> overrides it for tests).
/// </summary>
public static class Args
{
#if HOST
    private static string[]? _host;

    // Without HostSet the host program's own command line (the words after the program), so Args and Main(string[] args) agree.
    private static string[] HostArgs
    {
        get
        {
            if (_host == null)
            {
                var all = System.Environment.GetCommandLineArgs();
                _host = all.Length > 1 ? all[1..] : System.Array.Empty<string>();
            }
            return _host;
        }
    }

    /// <summary>Back to the real command line (what a program sees when nothing called <see cref="HostSet"/>).</summary>
    public static void HostReset() { _host = null; }

    /// <summary>Host fake: sets the arguments the program sees (tests call this to be repeatable; without it they are the real command line).</summary>
    public static void HostSet(params string[] args) { _host = args; }

    /// <summary>The number of arguments.</summary>
    public static int Count { get { return HostArgs.Length; } }

    /// <summary>Argument <paramref name="index"/> (0 is the first after the program name), or null when there is none.</summary>
    public static string Get(int index) { var a = HostArgs; return index >= 0 && index < a.Length ? a[index] : null!; }
#else
    [Native] private static extern int es_args_count();
    [Native] private static extern string es_args_get(int index);

    /// <summary>The number of arguments.</summary>
    public static int Count { get { return es_args_count(); } }

    /// <summary>Argument <paramref name="index"/> (0 is the first after the program name), or null when there is none.</summary>
    public static string Get(int index) { return es_args_get(index); }
#endif

    /// <summary>True when one argument is <paramref name="name"/> (a switch such as <c>AUTO</c>), ignoring case.</summary>
    public static bool Has(string name)
    {
        return IndexOf(name) >= 0;
    }

    /// <summary>
    /// The value given for <paramref name="key"/>: the argument after it (<c>SEED 42</c>) or the text after <c>=</c> (<c>SEED=42</c>),
    /// ignoring the case of the key. Null when the key is not there or has no value.
    /// </summary>
    public static string Value(string key)
    {
        int n = Count;
        for (int i = 0; i < n; i++)
        {
            string a = Get(i);
            if (SameIgnoringCase(a, key, key.Length) && a.Length == key.Length) return i + 1 < n ? Get(i + 1) : null!;
            if (a.Length > key.Length && a[key.Length] == '=' && SameIgnoringCase(a, key, key.Length)) return a.Substring(key.Length + 1);
        }
        return null!;
    }

    /// <summary>The value of <paramref name="key"/> as a decimal number (an optional '-' and digits), or <paramref name="fallback"/>.</summary>
    public static int Int(string key, int fallback)
    {
        string v = Value(key);
        if (v == null || v.Length == 0) return fallback;
        int i = 0;
        bool negative = false;
        if (v[0] == '-') { negative = true; i = 1; }
        if (i >= v.Length) return fallback;
        int result = 0;
        for (; i < v.Length; i++)
        {
            int c = v[i];
            if (c < '0' || c > '9') return fallback;
            result = result * 10 + (c - '0');
        }
        return negative ? -result : result;
    }

    private static int IndexOf(string name)
    {
        int n = Count;
        for (int i = 0; i < n; i++)
        {
            string a = Get(i);
            if (a.Length == name.Length && SameIgnoringCase(a, name, name.Length)) return i;
        }
        return -1;
    }

    // The first count characters of a and b are equal ignoring ASCII case (a may be longer).
    private static bool SameIgnoringCase(string a, string b, int count)
    {
        if (a.Length < count || b.Length < count) return false;
        for (int i = 0; i < count; i++)
        {
            int x = a[i];
            int y = b[i];
            if (x >= 'a' && x <= 'z') x -= 32;
            if (y >= 'a' && y <= 'z') y -= 32;
            if (x != y) return false;
        }
        return true;
    }
}
