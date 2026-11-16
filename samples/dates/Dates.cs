using ESharp;
using ESharp.Platform;

namespace DatesSample;

/// <summary>
/// The system clock, calendar dates and case-insensitive text (utility.library and dos.library DateStamp). Prints
/// "ok NAME" or "FAIL NAME". The date checks use fixed dates, so they do not depend on what the clock is set to
/// (an A1200 without a clock battery starts at 1 January 1978).
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static DateParts Make(int year, int month, int day, int hour, int minute, int second)
    {
        DateParts d = new DateParts();
        d.Year = year; d.Month = month; d.Day = day; d.Hour = hour; d.Minute = minute; d.Second = second;
        return d;
    }

    static bool Same(DateParts a, DateParts b)
    {
        return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.Hour == b.Hour && a.Minute == b.Minute && a.Second == b.Second;
    }

    [EntryPoint]
    public static void Main()
    {
        DateParts start = Date.Split(0);
        Check(Same(start, Make(1978, 1, 1, 0, 0, 0)) && start.Weekday == 0, "second 0 is Sunday 1 January 1978");
        Check(Same(Date.Split(365 * Date.Day), Make(1979, 1, 1, 0, 0, 0)), "a year later is 1 January 1979");

        uint s;
        Check(Date.TryJoin(Make(1978, 1, 1, 0, 0, 0), out s) && s == 0, "1 January 1978 is second 0");
        Check(Date.TryJoin(Make(2000, 2, 29, 12, 34, 56), out s), "29 February 2000 is a date");
        DateParts back = Date.Split(s);
        Check(Same(back, Make(2000, 2, 29, 12, 34, 56)) && back.Weekday == 2, "and splits back to itself, a Tuesday");
        Dos.PutStr($"2000-02-29 12:34:56 is second {s}\n");
        Check(s == 699366896, "it is second 699366896");
        Check(Date.TryJoin(Make(2026, 11, 9, 23, 59, 59), out s) && Date.Split(s + 1).Day == 10, "one second after 23:59:59 is the next day");
        Check(!Date.TryJoin(Make(2001, 2, 29, 0, 0, 0), out s), "29 February 2001 is not a date");
        Check(!Date.TryJoin(Make(2024, 4, 31, 0, 0, 0), out s), "31 April is not a date");
        Check(!Date.TryJoin(Make(2024, 1, 1, 24, 0, 0), out s), "hour 24 is not a time");
        Check(!Date.TryJoin(Make(1977, 12, 31, 0, 0, 0), out s), "1977 is before the Amiga's clock starts");

        uint t0 = Date.Now();
        DateParts now = Date.Split(t0);
        Dos.PutStr($"now {now.Year}-{now.Month}-{now.Day} {now.Hour}:{now.Minute}:{now.Second}\n");
        Dos.Delay(150);                                                   // 3 seconds
        uint t1 = Date.Now();
        Check(t1 - t0 >= 2 && t1 - t0 <= 5, "the clock moves on about 3 seconds in Delay(150)");

        Check(Strings.CompareIgnoreCase("Amiga", "aMIGA") == 0 && Strings.EqualsIgnoreCase("E#", "e#"), "case is ignored");
        Check(Strings.CompareIgnoreCase("apple", "Banana") < 0 && Strings.CompareIgnoreCase("b", "A") > 0, "order ignores case");
        Check(Strings.CompareIgnoreCase("Amiga", "Amigas") < 0, "a shorter string sorts first");
        Check(Strings.CompareIgnoreCase("AmigaOS", "AMIGA500", 5) == 0, "a length compares only the start");
        Check(Strings.ToUpper('q') == 'Q' && Strings.ToLower('Q') == 'q' && Strings.ToUpper('1') == '1', "ToUpper and ToLower");
        Check(Strings.ToUpper(0xE9) == 0xC9 && Strings.ToLower(0xC4) == 0xE4, "accented letters fold too");
        // Characters next to the letter ranges do not fold under any language driver. (0xF7, the division sign, is left out of the
        // checks: with locale.library running its answer is the locale's, and the Workbench startup runs IPrefs.)
        Check(Strings.ToUpper('{') == '{' && Strings.ToLower('@') == '@', "characters next to the letters do not fold");
        Dos.PutStr($"info: ToUpper(0xF7) = 0x{Strings.ToUpper(0xF7):X2} (0xF7 under the ROM utility.library, the locale's rule once IPrefs has run)\n");
        Date.Close();
    }
}
