namespace ESharp.Platform;

/// <summary>
/// A simple message box ("Quit game?", "Disk error") without a GUI toolkit, on every target. <see cref="Show"/> waits until a
/// button is chosen and returns its index counted from the left (0 for the first). The <c>buttons</c> argument lists the buttons separated by '|', such as "Yes|No" (up to 8; empty is one "OK" button). By the
/// Amiga's convention the last button is the negative answer ("Cancel", "No"): it is also the answer when the box cannot be
/// shown or is closed without a button, so a game asking "Quit?" goes on.
/// <para>
/// Amiga targets: intuition.library EasyRequestArgs, on the screen of the <see cref="Video"/> window when one is open (else
/// on the default public screen, normally Workbench). The text may have several lines ('\n'); '%' is shown as it is.
/// SDL: SDL_ShowMessageBox, over the game's window. Without a desktop (headless, offscreen video) SDL has no message box: the
/// answer is then the last button and SDL's reason goes to stderr. Host: the answer set with <see cref="HostSet"/>
/// (the last button until then).
/// </para>
/// </summary>
public static unsafe class Requester
{
    /// <summary>Shows <paramref name="text"/> with the <paramref name="buttons"/> ("Yes|No") and waits for one. Returns the chosen
    /// button's index from the left (0 first), or the last button's index when the box cannot be shown.</summary>
    public static int Show(string title, string text, string buttons)
    {
        int count = Count(buttons);
#if AMIGA
        byte* es = Mem.AllocFast((uint)Layout.EasyStructSize);
        byte* body = Escape(text);
        byte* gadgets = Escape(count == 1 && Cast.StringToPtr(buttons)[0] == 0 ? "OK" : buttons);
        int result = 0;
        if (es != null && body != null && gadgets != null)
        {
            *(uint*)es = (uint)Layout.EasyStructSize;
            *(byte**)(es + Layout.EasyTitle) = Cast.StringToPtr(title);
            *(byte**)(es + Layout.EasyTextFormat) = body;
            *(byte**)(es + Layout.EasyGadgetFormat) = gadgets;
            result = Intuition.EasyRequestArgs(Video.Window, es, null, null);
        }
        Mem.Free(gadgets);
        Mem.Free(body);
        Mem.Free(es);
        // EasyRequest counts 1, 2, ... from the left with 0 for the rightmost button.
        return result >= 1 && result < count ? result - 1 : count - 1;
#elif SDL
        int chosen = Crt.es_sdl_message_box(title, text, buttons);
        return chosen >= 0 && chosen < count ? chosen : count - 1;
#else
        _hostShown++;
        return _hostAnswer >= 0 && _hostAnswer < count ? _hostAnswer : count - 1;
#endif
    }

    /// <summary>The number of buttons in a "Yes|No" list (an empty list is one "OK" button; at most 8).</summary>
    public static int Count(string buttons)
    {
        byte* p = Cast.StringToPtr(buttons);
        int n = 1;
        for (int i = 0; p[i] != 0; i++) if (p[i] == (byte)'|') n++;
        return n > 8 ? 8 : n;
    }

#if AMIGA
    // EasyRequest formats the text and the buttons like printf: a copy with every '%' doubled shows it as it is.
    private static byte* Escape(string s)
    {
        byte* p = Cast.StringToPtr(s);
        int n = 0;
        for (int i = 0; p[i] != 0; i++) n += p[i] == (byte)'%' ? 2 : 1;
        byte* d = Mem.AllocFast((uint)(n + 1));
        if (d == null) return null;
        int o = 0;
        for (int i = 0; p[i] != 0; i++)
        {
            d[o++] = p[i];
            if (p[i] == (byte)'%') d[o++] = (byte)'%';
        }
        d[o] = 0;
        return d;
    }
#elif !SDL
    private static int _hostAnswer = -1;
    private static int _hostShown;

    /// <summary>Host: the button index every following <see cref="Show"/> returns (-1, or an index past the last button: the last button).</summary>
    public static void HostSet(int answer) { _hostAnswer = answer; }

    /// <summary>Host: how many times <see cref="Show"/> was called.</summary>
    public static int HostShown { get { return _hostShown; } }
#endif
}
