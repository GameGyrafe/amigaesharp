namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// utility.library bindings: dates, and case-insensitive text. The library is in the Kickstart ROM; <see cref="Date"/>
/// and <see cref="Strings"/> open it on first use through a base of their own. Vectors from the classic fd file.
/// </summary>
[LibraryBase("utility", "UtilityBase", "struct Library")]
public static unsafe class Utility
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    private static void* _base;

    /// <summary>Opens utility.library (v39). False only if it cannot be opened (it is in ROM).</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("utility.library", 39);
        if (_base == null) return false;
        SetBase(_base);
        return true;
    }

    public static void Close()
    {
        if (_base == null) return;
        SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }

    /// <summary>Fills a ClockData from seconds since 1 January 1978.</summary>
    [AmigaLib("utility", -120), Since(36)] public static extern void Amiga2Date(uint seconds, void* clockData);
    /// <summary>Seconds since 1 January 1978 for a ClockData (not checked: see CheckDate).</summary>
    [AmigaLib("utility", -126), Since(36)] public static extern uint Date2Amiga(void* clockData);
    /// <summary>As Date2Amiga, but 0 if the ClockData is not a real date.</summary>
    [AmigaLib("utility", -132), Since(36)] public static extern uint CheckDate(void* clockData);
    /// <summary>Compares two strings ignoring case (with the locale's rules if locale.library has set them up).</summary>
    [AmigaLib("utility", -162), Since(37)] public static extern int Stricmp(string a, string b);
    [AmigaLib("utility", -168), Since(37)] public static extern int Strnicmp(string a, string b, int length);
    [AmigaLib("utility", -174), Since(37)] public static extern byte ToUpper(uint character);
    [AmigaLib("utility", -180), Since(37)] public static extern byte ToLower(uint character);
}
#endif

/// <summary>A calendar date and time of day. <see cref="Weekday"/> is 0 for Sunday to 6 for Saturday.</summary>
public struct DateParts
{
    public int Year;
    public int Month;
    public int Day;
    public int Hour;
    public int Minute;
    public int Second;
    public int Weekday;
}

/// <summary>
/// The system clock and calendar dates. Times are whole seconds since 1 January 1978 00:00, the Amiga's epoch, in
/// the local time the system clock is set to (AmigaOS 3 has no time zones). <see cref="Split"/> and <see cref="TryJoin"/>
/// convert to and from a <see cref="DateParts"/> with utility.library. On the host the clock is a fake that reads
/// whatever <see cref="HostSetNow"/> set (0 at start), so tests are repeatable.
/// </summary>
public static unsafe class Date
{
    /// <summary>Seconds in a day.</summary>
    public const uint Day = 86400;

    /// <summary>Closes utility.library, which <see cref="Date"/> and <see cref="Strings"/> open on first use. Call it before the program ends.</summary>
#if AMIGA
    public static void Close() { Utility.Close(); }
#else
    public static void Close() { }
#endif

#if AMIGA
    /// <summary>The time now, in seconds since 1 January 1978 (dos.library DateStamp).</summary>
    public static uint Now()
    {
        int[] ds = new int[(Layout.DateStampSize + 3) / 4];
        fixed (int* p = ds)
        {
            Dos.DateStamp(p);
            byte* b = (byte*)p;
            uint days = (uint)*(int*)(b + Layout.DsDays), minute = (uint)*(int*)(b + Layout.DsMinute), tick = (uint)*(int*)(b + Layout.DsTick);
            return days * Day + minute * 60 + tick / Layout.DosTicksPerSecond;
        }
    }

    /// <summary>The calendar date and time of <paramref name="seconds"/> since 1 January 1978.</summary>
    public static DateParts Split(uint seconds)
    {
        DateParts d = new DateParts();
        if (!Utility.Open()) return d;
        ushort[] cd = new ushort[(Layout.ClockDataSize + 1) / 2];
        fixed (ushort* p = cd)
        {
            Utility.Amiga2Date(seconds, p);
            byte* b = (byte*)p;
            d.Year = *(ushort*)(b + Layout.CdYear);
            d.Month = *(ushort*)(b + Layout.CdMonth);
            d.Day = *(ushort*)(b + Layout.CdMday);
            d.Hour = *(ushort*)(b + Layout.CdHour);
            d.Minute = *(ushort*)(b + Layout.CdMin);
            d.Second = *(ushort*)(b + Layout.CdSec);
            d.Weekday = *(ushort*)(b + Layout.CdWday);
        }
        return d;
    }

    /// <summary>
    /// Seconds since 1 January 1978 for a date and time (<see cref="DateParts.Weekday"/> is ignored). False if it is
    /// not a real date (31 April, 29 February in a year that is not a leap year, an hour of 24), or before 1978.
    /// </summary>
    public static bool TryJoin(DateParts d, out uint seconds)
    {
        seconds = 0;
        if (d.Year < 1978 || d.Year > 2113 || !Utility.Open()) return false;   // utility counts in 32 bits from 1978
        ushort[] cd = new ushort[(Layout.ClockDataSize + 1) / 2];
        fixed (ushort* p = cd)
        {
            byte* b = (byte*)p;
            *(ushort*)(b + Layout.CdYear) = (ushort)d.Year;
            *(ushort*)(b + Layout.CdMonth) = (ushort)d.Month;
            *(ushort*)(b + Layout.CdMday) = (ushort)d.Day;
            *(ushort*)(b + Layout.CdHour) = (ushort)d.Hour;
            *(ushort*)(b + Layout.CdMin) = (ushort)d.Minute;
            *(ushort*)(b + Layout.CdSec) = (ushort)d.Second;
            *(ushort*)(b + Layout.CdWday) = 0;
            seconds = Utility.Date2Amiga(p);
        }
        // Split the result back rather than trusting CheckDate: it returns 0 for 1 January 1978 00:00:00, a real
        // date, and hosted AROS's CheckDate accepts 29 February 2001.
        DateParts back = Split(seconds);
        return back.Year == d.Year && back.Month == d.Month && back.Day == d.Day
            && back.Hour == d.Hour && back.Minute == d.Minute && back.Second == d.Second;
    }
#else
    private static uint _now;
    private static long _ticks;

    /// <summary>Host: sets what <see cref="Now"/> returns. <c>Dos.Delay</c> moves it on.</summary>
    public static void HostSetNow(uint seconds) { _now = seconds; _ticks = 0; }

    internal static void HostDelay(int ticks) { _ticks += ticks; }

    public static uint Now() { return _now + (uint)(_ticks / 50); }

    private static readonly System.DateTime Epoch = new System.DateTime(1978, 1, 1);

    public static DateParts Split(uint seconds)
    {
        var t = Epoch.AddSeconds(seconds);
        return new DateParts { Year = t.Year, Month = t.Month, Day = t.Day, Hour = t.Hour, Minute = t.Minute, Second = t.Second, Weekday = (int)t.DayOfWeek };
    }

    public static bool TryJoin(DateParts d, out uint seconds)
    {
        seconds = 0;
        if (d.Year < 1978 || d.Year > 2113 || d.Month < 1 || d.Month > 12 || d.Day < 1 || d.Day > System.DateTime.DaysInMonth(d.Year, d.Month)
            || d.Hour < 0 || d.Hour > 23 || d.Minute < 0 || d.Minute > 59 || d.Second < 0 || d.Second > 59) return false;
        double s = (new System.DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second) - Epoch).TotalSeconds;
        if (s > uint.MaxValue) return false;
        seconds = (uint)s;
        return true;
    }
#endif
}

/// <summary>
/// Case-insensitive text (utility.library): ISO-8859-1, so accented letters fold too (é and É), and with locale.library
/// running, the user's language rules. On the host the ISO-8859-1 rules are used.
/// </summary>
public static class Strings
{
#if AMIGA
    /// <summary>Negative, zero or positive as <paramref name="a"/> sorts before, the same as or after <paramref name="b"/>, ignoring case.</summary>
    public static int CompareIgnoreCase(string a, string b) { return Utility.Open() ? Utility.Stricmp(a, b) : 0; }

    /// <summary>As <see cref="CompareIgnoreCase(string, string)"/> for at most the first <paramref name="length"/> characters.</summary>
    public static int CompareIgnoreCase(string a, string b, int length) { return Utility.Open() ? Utility.Strnicmp(a, b, length) : 0; }

    /// <summary>The upper case of an ISO-8859-1 character (0..255).</summary>
    public static int ToUpper(int c) { return Utility.Open() ? Utility.ToUpper((uint)c) : c; }

    public static int ToLower(int c) { return Utility.Open() ? Utility.ToLower((uint)c) : c; }
#else
    public static int CompareIgnoreCase(string a, string b) { return CompareIgnoreCase(a, b, int.MaxValue); }

    public static int CompareIgnoreCase(string a, string b, int length)
    {
        for (int i = 0; i < length; i++)
        {
            int x = i < a.Length ? ToLower(a[i]) : 0, y = i < b.Length ? ToLower(b[i]) : 0;
            if (x != y) return x - y;
            if (x == 0) return 0;
        }
        return 0;
    }

    public static int ToUpper(int c) { return (c >= 'a' && c <= 'z') || (c >= 0xE0 && c <= 0xFE && c != 0xF7) ? c - 32 : c; }

    public static int ToLower(int c) { return (c >= 'A' && c <= 'Z') || (c >= 0xC0 && c <= 0xDE && c != 0xD7) ? c + 32 : c; }
#endif

    public static bool EqualsIgnoreCase(string a, string b) { return CompareIgnoreCase(a, b) == 0; }
}
