using ESharp;
using ESharp.Platform;

namespace GuiSample;

/// <summary>
/// A GadTools window (<see cref="GtWindow"/>) on the Workbench screen: a string gadget holding a name, a Hello button and a text field.
/// Clicking Hello writes "Hello, name!" into the field. The string gadget starts with a default name, so a click alone shows the
/// whole round trip (button event, read the string gadget, set the text field). Prints SHOT once the window is open (esharp run
/// --input sends its clicks after that), then handles events until the close gadget. On the host the sample drives the window itself.
/// </summary>
public static unsafe class Program
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
        GtWindow win = new GtWindow { Title = "E# Greeter", Width = 280 };
        GtString name = new GtString { Label = "Name", MaxChars = 32 };
        GtButton hello = new GtButton { Label = "Hello" };
        GtText reply = new GtText { Label = "Reply" };
        name.Text = "Amiga";
        reply.Text = "(click Hello)";
        win.Add(name);
        win.Add(hello);
        win.Add(reply);
        if (!win.Open()) { Dos.PutStr("cannot open the window (no gadtools.library or no Workbench screen)\n"); return; }
        Check(true, "the window opens");
        Check(name.Text == "Amiga", "the string gadget holds the default name");
#if !AMIGA
        win.HostType(name, "Ada");
        win.HostClick(hello);
        win.HostClose();
#endif
        Dos.PutStr("SHOT\n");                                     // esharp run --input sends its clicks after this
        Dos.FlushOutput();
        int clicks = 0;
        while (true)
        {
            GuiEvent e = win.Wait();
            if (e.Kind == GuiEventKind.Close) break;
            if (e.Source == hello)
            {
                clicks++;
                reply.Text = $"Hello, {name.Text}!";
                Check(true, "Hello clicked");
                Dos.PutStr($"reply: {reply.Text}\n");
                Dos.FlushOutput();
            }
        }
        Check(clicks >= 1, "a click arrived");
#if AMIGA
        Check(reply.Text == "Hello, Amiga!", "the reply is Hello, Amiga!");
#else
        Check(reply.Text == "Hello, Ada!", "the reply is Hello, Ada!");
#endif
        Check(true, "the close gadget arrived");
        win.Close();
        Heap.Free(ref win);
        Check(win == null, "the window is freed");
    }
}
