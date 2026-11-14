using ESharp;
using ESharp.Platform;

namespace MainArgsSample;

/// <summary>
/// The standard entry point: <c>static int Main(string[] args)</c>. args are the command-line words without the program name
/// (the same ones the Args class reads, with "quoted text" kept together); the result is the process return code (RC on the Amiga).
/// <c>esharp run samples/mainargs --args "ONE \"two words\" RC=5"</c> prints the words and ends with return code 5.
/// </summary>
public static class Program
{
    // The decimal number from position start of text (0 when there are no digits).
    static int Number(string text, int start)
    {
        int n = 0;
        for (int i = start; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++) n = n * 10 + (text[i] - '0');
        return n;
    }

    [EntryPoint]
    public static int Main(string[] args)
    {
        Dos.PutStr(args.Length == 0 ? "no arguments\n" : $"{args.Length} arguments\n");
        int rc = 0;
        for (int i = 0; i < args.Length; i++)
        {
            Dos.PutStr($"arg {i}: [{args[i]}]\n");
            if (args[i].Length > 3 && args[i][0] == 'R' && args[i][1] == 'C' && args[i][2] == '=') rc = Number(args[i], 3);
        }
        Dos.PutStr($"rc {rc}\n");
        return rc;
    }
}
