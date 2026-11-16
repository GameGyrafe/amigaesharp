using ESharp;
using ESharp.Platform;

namespace GuiClassSample;

/// <summary>
/// The window of samples/gui written as a class of its own: <see cref="Greeter"/> derives from the platform's <see cref="GtWindow"/>
/// (marked [Extensible]), adds its gadgets in its constructor and overrides the event hooks that <see cref="GtWindow.Run()"/> calls.
/// Same layout as samples/gui, so the same scripted clicks work: Hello, then the close gadget.
/// </summary>
public sealed class Greeter : GtWindow
{
    public GtString Name;
    public GtButton Hello;
    public GtText Reply;
    public int Clicks;
    public int Changes;
    public int CloseRequests;
    /// <summary>Refuse the first close request (the host run uses it to check that Run goes on waiting).</summary>
    public bool RefuseFirstClose;

    public Greeter(string title) : base()
    {
        Title = title;
        Width = 280;
        Name = new GtString { Label = "Name", MaxChars = 32 };
        Hello = new GtButton { Label = "Hello" };
        Reply = new GtText { Label = "Reply" };
        Name.Text = "Amiga";
        Reply.Text = "(click Hello)";
        Add(Name);
        Add(Hello);
        Add(Reply);
    }

    protected override void OnClicked(GtGadget gadget)
    {
        if (gadget != Hello) return;
        Clicks++;
        Reply.Text = $"Hello, {Name.Text}!";
        Dos.PutStr($"reply: {Reply.Text}\n");
        Dos.FlushOutput();
    }

    protected override void OnChanged(GtGadget gadget)
    {
        if (gadget == Name) Changes++;
    }

    protected override bool OnCloseRequest()
    {
        CloseRequests++;
        return !(RefuseFirstClose && CloseRequests == 1);
    }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        Greeter win = new Greeter("E# Greeter (class)");
        GtWindow asBase = win;
        Check(win.Width == 280 && win.Title == "E# Greeter (class)", "the derived constructor ran after the platform one");
        Check(asBase is Greeter && (asBase as Greeter) == win, "is and as from GtWindow to the derived class");
        if (!win.Open()) { Dos.PutStr("cannot open the window (no gadtools.library or no Workbench screen)\n"); Heap.Free(ref win); return; }
        Check(win.IsOpen, "the window opens");
#if !AMIGA
        win.RefuseFirstClose = true;
        win.HostType(win.Name, "Ada");
        win.HostClick(win.Name);
        win.HostClick(win.Hello);
        win.HostClose();                                          // refused: Run goes on
        win.HostClick(win.Hello);
        win.HostClose();
#endif
        Dos.PutStr("SHOT\n");                                     // esharp run --input sends its clicks after this
        Dos.FlushOutput();
        win.Run();
        Check(win.Clicks >= 1, "OnClicked was called for Hello");
#if AMIGA
        Check(win.Reply.Text == "Hello, Amiga!", "the reply is Hello, Amiga!");
        Check(win.CloseRequests == 1, "OnCloseRequest was asked once");
#else
        Check(win.Clicks == 2 && win.Changes == 1, "OnClicked twice and OnChanged once");
        Check(win.Reply.Text == "Hello, Ada!", "the reply is Hello, Ada!");
        Check(win.CloseRequests == 2, "the first close was refused and Run went on");
#endif
        win.Close();
        Check(!win.IsOpen, "the window is closed");
        Heap.Free(ref win);
        Check(win == null, "the derived window is freed");
    }
}
