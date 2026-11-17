using ESharp;
using ESharp.Platform;

namespace MuiSample;

/// <summary>
/// MUI on the BOOPSI core (issue #40, part 2): an application with one window (a text, two buttons, a slider, a string, a cycle
/// gadget, a check mark, a gauge and a list), notifications that run C# lambdas, and the whole tree disposed at once. Without
/// arguments every check runs by itself (attributes are set by the program, which drives the notifications), the window stays up
/// for two seconds and the program quits, so host and Amiga print the same lines. With WAIT it handles input until the close gadget
/// or Quit. Without MUI installed (a plain AmigaOS 3.1) it prints one skip line and ends.
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
        if (!Mui.Open())
        {
            Dos.PutStr("skip: MUI not installed\n");
            return;
        }
        Lone();
        Window(Args.Has("WAIT"));
        Check(Mui.Live == 0 && BoopsiObject.Live == 0, "every MUI object was disposed");
        Check(Heap.LiveClosures() == 0, "no closure is left");
        Mui.Close();
        Dos.PutStr(_failed == 0 ? "mui: all ok\n" : "mui: some checks failed\n");
    }

    // An object outside any tree: made and disposed by itself.
    static void Lone()
    {
        MuiText text = new MuiText("alone");
        Check(text.Create() && Mui.Live == 1, "a Text object is made on its own");
        Check(text.Text == "alone", "its text reads back");
        text.Text = "changed";
        Check(text.Text == "changed", "a new text reads back");
        MuiObject missing = new MuiObject("NoSuchClass.mui");
        Check(!missing.Create(), "an unknown class gives no object");
        text.Dispose();
        Heap.Free(ref text);
        missing.Dispose();
        Heap.Free(ref missing);
        // A negative trigger value: on 64-bit targets it must be widened with its sign, as the value MUI compares it with is.
        MuiSlider level = new MuiSlider(-10, 10, 0);
        int below = 0;
        level.WhenEquals(Muia.NumericValue, -3, () => below++);
        Check(level.Create(), "a slider from -10 to 10 is made on its own");
        level.Value = -3;
        level.Value = 3;
        Check(below == 1 && level.Value == 3, "a notification on a negative value fires for that value only");
        level.Dispose();
        Heap.Free(ref level);
        Check(Mui.Live == 0, "the lone objects are disposed");
    }

    static void Window(bool wait)
    {
        MuiApplication app = new MuiApplication("E# MUI", "ESHARPMUI");
        app.Init(Muia.ApplicationVersion, "$VER: mui 1.0 (17.11.2026)");
        app.Init(Muia.ApplicationDescription, "E# MUI sample");
        MuiWindow win = new MuiWindow("E# MUI");
        MuiGroup root = new MuiGroup();
        MuiText status = new MuiText("Press Hello or move the slider") { Frame = MuiFrame.Text };
        MuiGroup buttons = new MuiGroup { Horizontal = true };
        MuiButton hello = new MuiButton("_Hello");
        MuiButton quit = new MuiButton("_Quit");
        MuiGroup form = new MuiGroup { Columns = 2, Frame = MuiFrame.Group, Title = "Settings" };
        MuiSlider slider = new MuiSlider(0, 100, 10);
        MuiString name = new MuiString("Amiga", 32);
        string[] modes = { "One", "Two", "Three" };
        MuiCycle mode = new MuiCycle(modes);
        MuiCheckMark loud = new MuiCheckMark(false, "Loud");
        MuiGauge gauge = new MuiGauge(100) { InfoText = "%ld %%" };
        MuiListview list = new MuiListview();

        bool built = buttons.Add(hello) && buttons.Add(MuiRectangle.Gap(0)) && buttons.Add(quit);
        built = built && form.Add(new MuiLabel("Volume")) && form.Add(slider) && form.Add(new MuiLabel("Name")) && form.Add(name);
        built = built && form.Add(new MuiLabel("Mode")) && form.Add(mode) && form.Add(new MuiLabel("Loud")) && form.Add(loud);
        built = built && root.Add(status) && root.Add(buttons) && root.Add(form) && root.Add(gauge) && root.Add(list);
        Check(built, "the window's tree is put together");
        // One owner per object, no cycles, no window inside a group.
        Check(!form.Add(hello) && !form.Add(root) && !root.Add(root) && !root.Add(win) && hello.HasOwner, "an object has one parent (no second, no cycle)");
        win.Content = root;
        Check(win.Content == root && root.HasOwner, "the group is the window's content");
        Check(app.Add(win), "the window is added to the application");
        Check(!app.Add(win), "a window is added once");

        Check(app.Create(), "the application and every object in it are made");
        Check(app.IsCreated && hello.IsCreated && list.IsCreated && Mui.Live == 20, "twenty MUI objects are live");
        Check(win.Open() && win.IsOpen, "the window opens");
        Check(slider.Value == 10 && name.Contents == "Amiga" && mode.Active == 0 && !loud.Selected, "the starting values read back");

        int presses = 0;
        int sliderCalls = 0;
        int lastValue = -1;
        int lastMode = -1;
        int toggles = 0;
        int picked = -1;
        int typed = 0;
        hello.OnPressed(() => { presses++; status.Text = $"Hello pressed {presses} time(s)"; Say("hello"); });
        quit.OnPressed(() => { Say("quit"); app.Quit(); });
        slider.OnValueChanged(v =>
        {
            sliderCalls++;
            lastValue = v;
            status.Text = $"Slider: {v}";
            gauge.Current = v;
        });
        mode.OnActiveChanged(m => { lastMode = m; status.Text = $"Mode {m + 1}"; });
        loud.OnToggled(on => { toggles++; status.Text = on ? "Loud" : "Quiet"; });
        name.OnAcknowledged(s => { typed++; status.Text = $"Hello, {s}!"; Say($"name: {s}"); });
        list.OnActiveChanged(i => { picked = i; });

        // Drive the notifications by setting attributes: MUI runs the handlers inside the Set.
        slider.Value = 42;
        Check(sliderCalls == 1 && lastValue == 42, "setting the slider runs its notification with the new value");
        Check(status.Text == "Slider: 42", "the handler updated the text");
        Check(gauge.Current == 42, "the handler moved the gauge");
        mode.Active = 2;
        Check(lastMode == 2 && mode.Active == 2 && status.Text == "Mode 3", "the cycle gadget's notification");
        loud.Selected = true;
        Check(toggles == 1 && loud.Selected && status.Text == "Loud", "the check mark's notification");
        name.Contents = "E# on MUI";
        Check(name.Contents == "E# on MUI", "the string's text reads back");
        Check(list.Add("Amiga 1200") && list.Add("AmigaOS 4") && list.Add("AROS") && list.Count == 3, "three lines are added to the list");
        list.Active = 1;
        Check(picked == 1 && list.Active == 1 && list.Entry(1) == "AmigaOS 4", "the list's notification and its entry");

        // A dispose inside a handler is refused (the object stays usable); Quit is the way out of a handler.
        MuiText later = new MuiText("added later");
        bool refused = false;
        gauge.WhenChanged(Muia.GaugeCurrent, c => { if (c == 99) { later.Dispose(); refused = later.IsCreated; } });
        Check(root.Add(later) && later.IsCreated && Mui.Live == 21, "an object is added to the open window");
        gauge.Current = 99;
        Check(refused && Mui.Live == 21, "disposing inside a handler is refused");
        later.Dispose();
        Check(!later.IsCreated && Mui.Live == 20, "an object taken out of the open window is disposed");
        Heap.Free(ref later);

        Check(app.Step(), "input is handled without sleeping");

        if (wait)
        {
#if !AMIGA
            hello.HostPress();
            name.HostType("Amiga 1200");
            slider.Value = 70;
            quit.HostPress();
#endif
            Dos.PutStr("SHOT\n");                       // esharp run --input sends its clicks after this
            Dos.FlushOutput();
            app.Run();
            Dos.PutStr($"events: {presses} Hello press(es), {sliderCalls} slider change(s), last {lastValue}, {typed} Return(s), list entry {picked}\n");
            Check(presses >= 1, "a press of Hello arrived");
        }
        else
        {
            Dos.PutStr("SHOT\n");
            Dos.FlushOutput();
            for (int i = 0; i < 50 && app.Step(); i++) Dos.Delay(2);   // the window stays up for two seconds (for a screenshot)
        }

        win.Close();
        Check(!win.IsOpen, "the window closes");
        app.Dispose();
        Heap.Free(ref app);
        Check(app == null && Mui.Live == 0, "disposing the application disposes the whole tree");
    }
}
