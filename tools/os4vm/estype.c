/*
 * estype: copies text to the AmigaOS 4 kernel debug output, which QEMU writes to its serial file when the kernel was started
 * with the "serial" option (BBoot passes QEMU's -append text to the kernel). Part of the E# os4 run harness (tools/os4vm).
 *
 *   estype            copies standard input (used as "program | estype"), each line prefixed with "ES: "
 *   estype FILE       copies a file the same way
 *   estype -t TEXT    writes TEXT as one line, without the prefix (start/end markers)
 *
 * Debug output is polled by the kernel, so nothing is lost; SER: (serial.device) dropped characters in the VM. The prefix
 * lets the host tell program lines from kernel and driver messages that share the port.
 */
#include <proto/exec.h>
#include <proto/dos.h>
#include <string.h>

int main(int argc, char **argv)
{
    static char line[4096];
    BPTR in;

    if (argc > 2 && strcmp(argv[1], "-t") == 0)
    {
        IExec->DebugPrintF("%s\n", argv[2]);
        return 0;
    }
    in = argc > 1 ? IDOS->Open(argv[1], MODE_OLDFILE) : IDOS->Input();
    if (in == 0)
    {
        IExec->DebugPrintF("estype: cannot open %s\n", argv[1]);
        return 10;
    }
    int first = argc == 1;
    while (IDOS->FGets(in, line, sizeof line - 1) != NULL)
    {
        size_t n = strlen(line);
        /* The AmigaOS 4.1 shell's pipe ("a | b") delivers one empty line before the first command's output (seen with
           Echo and with E# programs; a redirect to a file has none), so standard input drops exactly that line. */
        if (first)
        {
            first = 0;
            if (strcmp(line, "\n") == 0) continue;
        }
        IExec->DebugPrintF(n > 0 && line[n - 1] == '\n' ? "ES: %s" : "ES: %s\n", line);
    }
    if (argc > 1) IDOS->Close(in);
    return 0;
}
