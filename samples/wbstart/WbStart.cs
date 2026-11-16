using ESharp;
using ESharp.Platform;

namespace WbStartSample;

/// <summary>
/// Workbench start (issue #84): a window that shows how the program was started and what <see cref="Args"/> found. Started from
/// the icon (<c>wbstart.info</c>, written by <c>esharp build</c>; the tool types SEED=42 and AUTO come from WbStart.csproj) the
/// arguments are the icon's tool types; from a Shell they are the command line. Close the window with its close gadget. Has no
/// console output, so it also works when started by double-click (a Workbench start has no stdout).
/// </summary>
public static unsafe class Program
{
#if M68K
    // The task's stack bounds (struct Task: SPLower at offset 58, SPUpper at 62 on the classic Amiga), to show the icon's stack size took effect.
    [AmigaLib("exec", -294)] static extern void* FindTask(byte* name);

    static int StackBytes()
    {
        byte* task = (byte*)FindTask(null);
        return (int)(*(uint*)(task + 62) - *(uint*)(task + 58));
    }
#else
    static int StackBytes() { return 0; }
#endif

    [EntryPoint]
    public static void Main()
    {
        GtWindow win = new GtWindow { Title = "E# Workbench start", Width = 300 };
        GtText count = new GtText { Label = "Arguments" };
        GtText seed = new GtText { Label = "SEED" };
        GtText auto = new GtText { Label = "AUTO" };
        count.Text = $"{Args.Count}";
        seed.Text = Args.Value("SEED") == null ? "(none)" : Args.Value("SEED");
        auto.Text = Args.Has("AUTO") ? "yes" : "no";
        win.Add(count);
        win.Add(seed);
        GtText stack = new GtText { Label = "Stack" };
        stack.Text = StackBytes() == 0 ? "(not read)" : $"{StackBytes()}";
        win.Add(auto);
        win.Add(stack);
        if (!win.Open()) { Dos.PutStr("cannot open the window\n"); return; }
        Dos.PutStr($"arguments {Args.Count}, SEED {seed.Text}, AUTO {auto.Text}, stack {stack.Text}\n");
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
#if !AMIGA
        win.HostClose();
#endif
        while (win.Wait().Kind != GuiEventKind.Close) { }
        win.Close();
        Heap.Free(ref win);
    }
}
