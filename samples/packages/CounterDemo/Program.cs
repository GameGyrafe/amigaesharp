using ESharp;
using ESharp.Platform;
using Counter;

namespace CounterDemo;

/// <summary>The game's window, derived from the package's CounterWindow, itself derived from the platform's GtWindow.</summary>
public sealed class Score : CounterWindow
{
    public int Plusses;

    public Score() : base("E# Counter") { }

    protected override void OnPlus() { Plusses++; }

    protected override bool OnCloseRequest() { return base.OnCloseRequest() && Closes >= 1; }
}

/// <summary>
/// The real life of a window class from a package: construct, Open, deliver events through the three levels of hooks, Close (which frees
/// the gadgets), then free the window itself with no alias left pointing at it. Every check prints "ok NAME" or "FAIL NAME";
/// esharp test compares the host and Amiga runs. Events are handed to Dispatch directly, so no input is needed on the Amiga.
/// </summary>
public static class Program
{
    static int failures;

    static void Check(bool ok, string name)
    {
        if (!ok) failures++;
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static int Main()
    {
        Dos.PutStr("Counter demo: a GtWindow-derived class from a package\n");

        Score win = new Score();
        Check(win.Width == 200 && win.Title == "E# Counter" && win.Count == 0 && !win.IsOpen, "platform, package and game constructors ran");

        if (!win.Open())
        {
            // No gadtools.library or no Workbench screen: nothing more to run, and not a pass either.
            Check(false, "the window opens");
            win.Close();                                        // frees the two gadgets the constructors added: Heap.Free of the window alone would leak them
            win.Plus = null;
            win.Reset = null;
            Heap.Free(ref win);
            return 5;
        }
        Check(win.IsOpen, "the window opens");
        Check(!win.Open(), "a second Open is refused");

        Check(win.Dispatch(new GuiEvent(GuiEventKind.Clicked, win.Plus)) && win.Dispatch(new GuiEvent(GuiEventKind.Clicked, win.Plus)) &&
              win.Count == 2 && win.Plusses == 2, "two clicks: package hook counts, game override called");
        Check(win.Dispatch(new GuiEvent(GuiEventKind.Clicked, win.Reset)) && win.Count == 0 && win.Plusses == 2, "reset: package hook only");

        GtWindow asBase = win;
        Check(asBase is CounterWindow && asBase is Score && (asBase as CounterWindow) == win, "is and as along the chain");
        asBase = null;                                          // no alias to the window outlives it

        Check(!win.Dispatch(new GuiEvent(GuiEventKind.Close, null)) && win.Closes == 1, "close request accepted through three levels");

        win.Close();                                            // closes the window and frees the gadgets that were added
        win.Plus = null;                                        // those two now dangle: drop them
        win.Reset = null;
        Check(!win.IsOpen, "the window is closed");

        Heap.Free(ref win);
        Check(win == null, "the window is freed");

        Dos.PutStr(failures == 0 ? "all checks passed\n" : "some checks failed\n");
        return failures == 0 ? 0 : 5;
    }
}
