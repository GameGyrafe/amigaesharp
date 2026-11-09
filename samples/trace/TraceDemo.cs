using ESharp;
using ESharp.Platform;

namespace TraceSample;

/// <summary>
/// Serial trace: Trace.Line/Value write to the serial port (standard error on the host). They exist only in a build with
/// --trace or --debug; otherwise the calls are gone. The program counts to a few values and traces each step, while its
/// normal output (standard output) says what it did. Run it with: esharp run samples/trace --trace --serial trace.txt
/// </summary>
public static class Program
{
    static int Square(int n)
    {
        Trace.Value("square arg", n);
        return n * n;
    }

    [EntryPoint]
    public static void Main()
    {
        Trace.Line("trace start");
        int sum = 0;
        for (int i = 1; i <= 3; i++)
        {
            sum += Square(i);
            Trace.Value("sum", sum);
        }
        Trace.Write("negative: ");
        Trace.Value("n", -42);
        Trace.Line("trace end");
        Dos.PutStr("trace sample done, sum ");
        Dos.PutInt(sum);
        Dos.PutStr("\n");
    }
}
