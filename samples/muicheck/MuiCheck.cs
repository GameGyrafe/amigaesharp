using ESharp;
using ESharp.Platform;

namespace MuiCheck;

/// <summary>
/// Opens muimaster.library and prints its version, for <c>esharp run --mui 3.8|5</c> (tools/mui/README.md). On the host
/// there is no MUI: it says so.
/// </summary>
public static unsafe class Program
{
    [EntryPoint]
    public static void Main()
    {
#if AMIGA
        void* mui = Exec.OpenLibrary("muimaster.library", 0);
        if (mui == null) { Dos.PutStr("muimaster.library: not installed\n"); return; }
        ushort* v = (ushort*)((byte*)mui + 20);                   // struct Library: lib_Version, lib_Revision
        Dos.PutStr("muimaster.library ");
        Dos.PutInt(v[0]);
        Dos.PutStr(".");
        Dos.PutInt(v[1]);
        Dos.PutStr("\n");
        Exec.CloseLibrary(mui);
#else
        Dos.PutStr("muimaster.library: only on an Amiga target\n");
#endif
    }
}
