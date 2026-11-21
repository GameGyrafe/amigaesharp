using ESharp;
using ESharp.Platform;

namespace HwPackSummary;

/// <summary>
/// Helper for the hardware test pack's RunAll (AmigaDOS has no way to count lines): reads one result file and prints
/// "NAME: N ok, M FAIL, K skip" (plus the count an emulated A1200 gives, when known). Usage: Summary NAME FILE [EXPECTED], where EXPECTED is a number
/// or "text" for a program that prints no ok lines.
/// Counts lines that start with "ok ", "FAIL " or "skip "; a missing or empty file is reported as such. FAIL lines are
/// repeated in the output. A result that is only the line "skip: MUI not installed" (the MUI samples) counts as skipped, not as a count difference. Return code: 0 fine, 5 the ok count differs from EXPECTED, 10 a FAIL line or no output.
/// </summary>
public static unsafe class Program
{
    static bool StartsWith(byte* p, int at, int size, string word)
    {
        if (at + word.Length > size) return false;
        for (int i = 0; i < word.Length; i++) if (p[at + i] != (byte)word[i]) return false;
        return true;
    }

    static bool HasLine(byte* p, int size, string word)
    {
        for (int i = 0; i < size; i++)
            if ((i == 0 || p[i - 1] == 10) && StartsWith(p, i, size, word)) return true;
        return false;
    }

    [EntryPoint]
    public static int Main(string[] args)
    {
        if (args.Length < 2) { Dos.PutStr("usage: Summary NAME FILE [EXPECTED]\n"); return 5; }
        int size;
        byte* p = Files.ReadAll(args[1], out size, false);
        Dos.PutStr(args[0]);
        if (p == null) { Dos.PutStr(": no output (missing or empty result file)\n"); return 10; }
        int ok = 0, fail = 0, skip = 0;
        for (int i = 0; i < size; i++)
        {
            if (i > 0 && p[i - 1] != 10) continue;
            if (StartsWith(p, i, size, "ok ")) ok++;
            else if (StartsWith(p, i, size, "FAIL ")) fail++;
            else if (StartsWith(p, i, size, "skip ")) skip++;
        }
        if (fail > 0)
        {
            // the FAIL lines themselves, so SUMMARY.txt shows what failed without opening the result file
            Dos.PutStr($": {ok} ok, {fail} FAIL, {skip} skip <-- FAIL\n");
            for (int i = 0; i < size; i++)
            {
                if (i > 0 && p[i - 1] != 10) continue;
                if (!StartsWith(p, i, size, "FAIL ")) continue;
                int end = i;
                while (end < size && p[end] != 10) end++;
                Dos.PutStr("    ");
                if (end < size)
                {
                    p[end] = 0;                                  // the line ends at its newline: end the string there
                    Dos.PutStr(Cast.PtrToString(p + i));
                    p[end] = 10;
                }
                Dos.PutStr("\n");
            }
            Mem.Free(p);
            return 10;
        }
        // A MUI sample on a machine without MUI prints one "skip: MUI not installed" line and no ok lines: that is optional, not a failure.
        bool noMui = ok == 0 && fail == 0 && HasLine(p, size, "skip: MUI not installed");
        Mem.Free(p);
        if (noMui)
        {
            Dos.PutStr(": skipped, MUI is not installed (optional, see README.txt)\n");
            return 0;
        }
        if (args.Length > 2 && args[2] == "text")
        {
            Dos.PutStr($": text output only, {size} bytes\n");
            return 0;
        }
        Dos.PutStr($": {ok} ok, {fail} FAIL, {skip} skip");
        if (args.Length > 2)
        {
            int expected = 0;
            for (int i = 0; i < args[2].Length; i++) expected = expected * 10 + (args[2][i] - '0');
            Dos.PutStr(ok == expected ? " (as the emulator)" : $" (emulator: {expected} ok)");
            if (ok != expected) { Dos.PutStr(" <-- count differs (see README.txt)\n"); return 5; }
        }
        Dos.PutStr("\n");
        return 0;
    }
}
