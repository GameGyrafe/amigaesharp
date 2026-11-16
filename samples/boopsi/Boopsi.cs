using ESharp;
using ESharp.Platform;

namespace BoopsiSample;

/// <summary>
/// BOOPSI objects of the ROM classes (issue #40, part 1): make them, set and read their attributes, a C# lambda as a system hook,
/// a window with a framed button, a slider (propgclass) and a string gadget with an edit hook, and clean disposal (no system object
/// and no closure left). Without arguments every check runs by itself and the window closes again, so host and Amiga print the
/// same lines. With the argument WAIT the window stays open after SHOT and handles events until its close gadget: on the Amiga
/// they come from scripted input (esharp run --input), on the host the sample queues a click, a slider move and Return itself.
/// </summary>
public static unsafe class Program
{
    static int _failed;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
        if (!ok) _failed++;
    }

    static void Say(string s)
    {
        Dos.PutStr("event: ");
        Dos.PutStr(s);
        Dos.PutStr("\n");
        Dos.FlushOutput();
    }

    [EntryPoint]
    public static void Main()
    {
        bool wait = Args.Has("WAIT");
        Objects();
        Hooks();
        Window(wait);
        Check(BoopsiObject.Live == 0, "every system object was disposed");
        Check(Heap.LiveClosures() == 0, "no closure is left");
        Dos.PutStr(_failed == 0 ? "boopsi: all ok\n" : "boopsi: some checks failed\n");
    }

    // Objects outside a window: create, set, get, DoMethod, dispose.
    static void Objects()
    {
        BoopsiObject prop = new BoopsiObject("propgclass");
        prop.Init(Ga.Width, 100);
        prop.Init(Ga.Height, 10);
        prop.Init(Pga.Freedom, Pga.FreeHoriz);
        prop.Init(Pga.Total, 100);
        prop.Init(Pga.Visible, 10);
        prop.Init(Pga.Top, 5);
        Check(prop.Create() && prop.Address != null, "a propgclass object is created");
        Check(BoopsiObject.Live == 1, "one system object is live");
        // propgclass of Kickstart 3.1 answers OM_GET for PGA_Top only (not PGA_Total or PGA_Visible), so the checks read that one.
        Check(prop.Get(Pga.Top) == 5, "the starting position reads back");
        prop.Set(Pga.Top, 30);
        Check(prop.Get(Pga.Top) == 30, "a set position reads back");
        nuint slot = 0;
        nuint handled = prop.DoMethod(Om.Get, Pga.Top.Tag, (nuint)(&slot));
        Check(handled != 0 && slot == 30, "OM_GET through DoMethod");
        nuint unknown = 0;
        Check(!prop.TryGetRaw(0x80FF0001u, out unknown), "an attribute the class does not know is refused");
        Check(!prop.Init(Pga.Top, 1), "no starting attributes after Create");

        BoopsiObject text = new BoopsiObject("strgclass");
        text.Init(Ga.Width, 120);
        text.Init(Ga.Height, 10);
        text.Init(Stringa.MaxChars, 32);
        text.Init(Stringa.TextVal, $"Ami{"ga"}");     // a temporary string: the object keeps its own copy
        Check(text.Create(), "a strgclass object is created");
        Check(text.Get(Stringa.TextVal) == "Amiga", "the string gadget holds its starting text");
        text.Set(Stringa.TextVal, "E# on BOOPSI");
        Check(text.Get(Stringa.TextVal) == "E# on BOOPSI", "a new text reads back");

        BoopsiObject missing = new BoopsiObject("no-such-class");
        Check(!missing.Create() && missing.Address == null, "an unknown class gives no object");

        prop.Dispose();
        Heap.Free(ref prop);
        text.Dispose();
        Heap.Free(ref text);
        missing.Dispose();
        Heap.Free(ref missing);
        Check(BoopsiObject.Live == 0 && prop == null, "the objects are disposed and freed");
    }

    // A capturing lambda as a struct Hook, called the way the system calls hooks (CallHookPkt on the Amiga).
    static void Hooks()
    {
        int calls = 0;
        int seen = 0;
        Hook hook = new Hook((obj, msg) =>
        {
            calls++;
            seen = *(int*)msg + (obj == null ? 0 : 1000);
            return (nuint)(calls * 10);
        });
        Check(hook.Address != null, "the hook has a system address");
        int message = 42;
        int target = 0;
        nuint r1 = hook.Call(&target, &message);
        Check(r1 == 10 && calls == 1, "the hook runs the lambda and returns its result");
        Check(seen == 1042, "the object and message arrive in the lambda");
        message = 7;
        nuint r2 = hook.Call(null, &message);
        Check(r2 == 20 && calls == 2 && seen == 7, "a second call updates the captured state");

        // An object owns a hook it keeps: disposing the object disposes the hook (and frees its closure).
        BoopsiObject owner = new BoopsiObject("strgclass");
        owner.Init(Ga.Width, 80);
        owner.Init(Ga.Height, 10);
        Hook kept = new Hook((obj, msg) => (nuint)calls);
        owner.Init(Stringa.EditHook, kept);
        Check(owner.Create() && owner.Keep(kept), "a string gadget with an edit hook it keeps");
        owner.Dispose();
        Heap.Free(ref owner);

        hook.Dispose();
        Heap.Free(ref hook);
        Check(hook == null && Heap.LiveClosures() == 0, "the hooks and their closures are freed");
    }

    // A window with ROM gadgets: a framed button, a slider reporting its moves, a string gadget with an edit hook.
    static void Window(bool wait)
    {
        BoopsiWindow win = new BoopsiWindow { Title = "E# BOOPSI", Width = 300, Height = 92 };
        if (!win.Open()) { Dos.PutStr("cannot open the window (no Workbench screen)\n"); _failed++; return; }
        Check(true, "the window opens");
        int x = win.InnerLeft;
        int y = win.InnerTop;

        // frbuttonclass sizes the frame around its label itself (the frame's IM_FRAMEBOX), so neither gets a size here.
        BoopsiObject frame = new BoopsiObject("frameiclass");
        frame.Init(Ia.FrameType, Ia.FrameButton);
        Check(frame.Create(), "a frameiclass image is created");
        BoopsiObject button = new BoopsiObject("frbuttonclass");
        button.Init(Ga.Left, x + 10);
        button.Init(Ga.Top, y + 8);
        button.Init(Ga.Image, frame);
        button.Init(Ga.Text, "Hello");
        button.Init(Ga.RelVerify, true);
        button.Init(Ga.Id, 1);
        Check(win.Add(button) && button.Keep(frame), "a framed button is added (it keeps its frame)");

        BoopsiObject slider = new BoopsiObject("propgclass");
        slider.Init(Ga.Left, x + 10);
        slider.Init(Ga.Top, y + 34);
        slider.Init(Ga.Width, 200);
        slider.Init(Ga.Height, 12);
        slider.Init(Pga.Freedom, Pga.FreeHoriz);
        slider.Init(Pga.Total, 100);
        slider.Init(Pga.Visible, 10);
        slider.Init(Pga.NewLook, true);
        slider.Init(Ga.RelVerify, true);
        slider.Init(Ga.Id, 2);
        slider.Init(Ica.Target, Ica.Idcmp);
        Check(win.Add(slider), "a slider is added");
        slider.Set(Pga.Top, 45);
        Check(slider.Get(Pga.Top) == 45, "the slider's position set in the window reads back");

        // A frame image fills its inside, so it is drawn before the gadget that sits in it.
        BoopsiObject ridge = new BoopsiObject("frameiclass");
        ridge.Init(Ia.Left, x + 10);
        ridge.Init(Ia.Top, y + 58);
        ridge.Init(Ia.Width, 192);
        ridge.Init(Ia.Height, 18);
        ridge.Init(Ia.FrameType, Ia.FrameRidge);
        Check(win.AddImage(ridge), "a ridge frame for the string gadget is drawn");
        int keys = 0;
        BoopsiObject entry = new BoopsiObject("strgclass");
        Hook editHook = new Hook((obj, msg) =>
        {
            if (*(uint*)msg == Stringa.Key) keys++;
            return 0;   // not handled here: the gadget's own editing stands
        });
        entry.Init(Ga.Left, x + 16);
        entry.Init(Ga.Top, y + 62);
        entry.Init(Ga.Width, 180);
        entry.Init(Ga.Height, 10);
        entry.Init(Stringa.MaxChars, 32);
        entry.Init(Stringa.TextVal, "Amiga");
        entry.Init(Stringa.EditHook, editHook);
        entry.Init(Ga.RelVerify, true);
        entry.Init(Ga.Id, 3);
        Check(win.Add(entry) && entry.Keep(editHook), "a string gadget with an edit hook is added");
        Check(BoopsiObject.Live == 5, "five system objects are live");

        int clicks = 0;
        int lastTop = -1;
        int updates = 0;
        int accepted = 0;
        button.Notify += e => { clicks++; Say("button clicked"); };
        slider.Notify += e =>
        {
            if (e.Kind == BoopsiEventKind.Update && e.Attr == Pga.Top.Tag) { updates++; lastTop = e.Value; Say($"slider at {e.Value}"); }
            else Say("slider released");
        };
        entry.Notify += e => { accepted++; Say($"text accepted: {entry.Get(Stringa.TextVal)}"); };

        if (wait)
        {
#if !AMIGA
            win.HostRelease(button, 0);
            win.HostUpdate(slider, Pga.Top, 70);
            win.HostRelease(entry, 0);
            win.HostClose();
#endif
            Dos.PutStr("SHOT\n");                                     // esharp run --input sends its clicks after this
            Dos.FlushOutput();
            win.Run();
            Dos.PutStr($"events: {clicks} click(s), {updates} slider update(s), last position {lastTop}, {accepted} Return(s), {keys} key(s) in the edit hook\n");
            Dos.PutStr($"slider: {slider.Get(Pga.Top)}, text: {entry.Get(Stringa.TextVal)}\n");
            Check(clicks >= 1, "a click on the button arrived");
            Check(updates >= 1 && lastTop == slider.Get(Pga.Top), "the slider's moves arrived and match its position");
        }

        win.Close();
        Heap.Free(ref win);
        Check(win == null && BoopsiObject.Live == 0, "the window disposed its gadgets and images");
    }
}
