using ESharp;
using ESharp.Platform;

namespace MuiClassSample;

/// <summary>
/// A MUI custom class written in C# (issues #93, #114): a bar graph derived from <see cref="MuiArea"/> (Area.mui) whose size
/// (MUIM_AskMinMax) and drawing (MUIM_Draw) are C#, with an attribute of its own (<see cref="Level"/>, OM_SET/OM_GET), a method of its
/// own (<see cref="Reset"/>) and mouse input (MUIM_HandleEvent): a click or a drag on the graph sets the level from the mouse's
/// position, left 0 to right 100. Setting the attribute redraws the graph.
/// </summary>
public sealed unsafe class BarGraph : MuiArea
{
    /// <summary>The level, 0 to 100 (TAG_USER and a number of this program's).</summary>
    public static IntAttr Level => new IntAttr(0x8E5A0001);

    /// <summary>A method of its own: { Reset, new level }; returns the old level.</summary>
    public const uint Reset = 0x8E5A0101;

    private int _level;
    private bool _dragging;
    public int Setups, Cleanups, SizeAsks, Draws, Sets, Clicks, Moves;

    public BarGraph(int level)
    {
        Frame = MuiFrame.Text;
        Init(Level, level);
        WantEvents(MuiEvent.MouseButtons);
    }

    // The left button down on the graph sets the level and starts a drag; moves while it is held set it again; up ends the drag.
    // Mouse moves are asked for only during the drag (every move in the window would be a call otherwise). Events elsewhere in the
    // window are left to the other handlers (false).
    protected override bool OnEvent(MuiEvent e)
    {
        if (e.Class == MuiEvent.MouseButtons)
        {
            if (e.Code == MuiEvent.SelectDown && e.Inside)
            {
                _dragging = true;
                WantEvents(MuiEvent.MouseButtons | MuiEvent.MouseMove);
                Clicks++;
                LevelAt(e);
                return true;
            }
            if (e.Code == MuiEvent.SelectUp && _dragging)
            {
                _dragging = false;
                WantEvents(MuiEvent.MouseButtons);
                return true;
            }
            return false;
        }
        if (e.Class == MuiEvent.MouseMove && _dragging)
        {
            Moves++;
            LevelAt(e);
            return true;
        }
        return false;
    }

    private void LevelAt(MuiEvent e)
    {
        int x = e.MouseX < 0 ? 0 : e.MouseX >= e.Width ? e.Width - 1 : e.MouseX;
        int v = e.Width > 1 ? x * 100 / (e.Width - 1) : 0;
        if (v != _level) Set(Level, v);   // through OM_SET, so the notifications (the slider) follow
    }

    protected override bool OnSetup()
    {
        Setups++;
        return true;
    }

    protected override void OnCleanup() { Cleanups++; _dragging = false; }

    protected override void OnAskMinMax(ref MuiMinMax size)
    {
        SizeAsks++;
        size.MinWidth = 96;
        size.MinHeight = 40;
        size.DefWidth = 192;
        size.DefHeight = 64;
        size.MaxWidth = MuiMinMax.Unlimited;
        size.MaxHeight = MuiMinMax.Unlimited;
    }

    protected override void OnDraw(MuiRender r)
    {
        Draws++;
        // Eight bars rising to the level, a base line and the level as text.
        int bars = 8;
        int w = r.Width / bars;
        for (int i = 0; i < bars; i++)
        {
            int h = (r.Height - 2) * _level * (i + 1) / (bars * 100);
            r.Fill(i % 2 == 0 ? MuiPen.Fill : MuiPen.Shadow, i * w + 1, r.Height - 1 - h, w - 2, h);
        }
        r.Line(MuiPen.Text, 0, r.Height - 1, r.Width - 1, r.Height - 1);
        r.Text(MuiPen.Text, 2, 1, $"{_level}%");
    }

    protected override void OnSet(uint attr, nuint value)
    {
        if (attr != Level.Tag) return;
        int v = (int)(nint)value;
        _level = v < 0 ? 0 : v > 100 ? 100 : v;
        Sets++;
        Redraw();
    }

    protected override bool OnGet(uint attr, out nuint value)
    {
        value = (nuint)_level;
        return attr == Level.Tag;
    }

    protected override bool OnMethod(uint methodId, nuint* msg, out nuint result)
    {
        result = 0;
        if (methodId != Reset) return false;
        result = (nuint)_level;
        Set(Level, (int)(nint)msg[1]);
        return true;
    }
}

/// <summary>
/// A custom group (Group.mui): the children are laid out by MUI as usual; the class draws a stripe under them whose length is its own
/// attribute <see cref="Mark"/>, and counts its setups and draws.
/// </summary>
public sealed unsafe class Panel : MuiCustomGroup
{
    public static IntAttr Mark => new IntAttr(0x8E5A0002);

    private int _mark;
    public int Setups, Cleanups, Draws;

    public Panel()
    {
        Horizontal = true;
        Frame = MuiFrame.Group;
        Title = "Custom group";
    }

    protected override bool OnSetup()
    {
        Setups++;
        return true;
    }

    protected override void OnCleanup() { Cleanups++; }

    protected override void OnDraw(MuiRender r)
    {
        Draws++;
        r.Fill(MuiPen.Shadow, 0, r.Height + 1, r.Width, 2);
        r.Fill(MuiPen.Fill, 0, r.Height + 1, r.Width * _mark / 100, 2);
    }

    protected override void OnSet(uint attr, nuint value)
    {
        if (attr != Mark.Tag) return;
        _mark = (int)(nint)value;
        Redraw();
    }

    protected override bool OnGet(uint attr, out nuint value)
    {
        value = (nuint)_mark;
        return attr == Mark.Tag;
    }
}

/// <summary>
/// Two bar graphs (one Area class for both) in a custom group, a slider whose notification sets the first graph's level and the group's
/// mark, and checks of the class's life: made once with the first object, objects made and disposed through the dispatcher (OM_NEW,
/// OM_DISPOSE), setup, size and draw called, the own attribute's round trip and notification, the own method, and the classes deleted
/// after the application. Without arguments the window stays up for two seconds; with WAIT it runs until closed (or Quit): click or drag on
/// a graph to set its level (the first graph's slider follows). WAIT CLICKS (scripted runs with --input) also checks that a click reached a
/// graph.
/// </summary>
public static unsafe class Program
{
    static int _failed;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
        Dos.FlushOutput();
        if (!ok) _failed++;
    }

    [EntryPoint]
    public static void Main()
    {
        if (!Mui.Open())
        {
            Dos.PutStr("skip: MUI not installed\n");
            return;
        }
        int made = Mui.CustomClassesMade;
        Window(Args.Has("WAIT"), made);
        Lone(made);
        Check(Mui.Live == 0 && BoopsiObject.Live == 0 && Mui.CustomObjects == 0, "every MUI object was disposed");
        Check(Heap.LiveClosures() == 0, "no closure is left");
        Mui.Close();
        Dos.PutStr(_failed == 0 ? "muiclass: all ok\n" : "muiclass: some checks failed\n");
    }

    static void Window(bool wait, int made)
    {
        MuiApplication app = new MuiApplication("E# MUI class", "ESHARPMUICLASS");
        app.Init(Muia.ApplicationVersion, "$VER: muiclass 1.0 (19.11.2026)");
        MuiWindow win = new MuiWindow("E# MUI custom class");
        MuiGroup root = new MuiGroup();
        MuiText status = new MuiText("Move the slider") { Frame = MuiFrame.Text };
        Panel panel = new Panel();
        BarGraph bar = new BarGraph(30);
        BarGraph other = new BarGraph(80);
        MuiSlider slider = new MuiSlider(0, 100, 30);
        MuiButton quit = new MuiButton("_Quit");
        bool built = panel.Add(bar) && panel.Add(other);
        built = built && root.Add(status) && root.Add(panel) && root.Add(slider) && root.Add(quit);
        win.Content = root;
        Check(built && app.Add(win), "the window's tree is put together");
        Check(Mui.CustomClasses == 0, "no custom class before the application is made");

        Check(app.Create(), "the application with two bar graphs in a custom group is made");
        Check(Mui.CustomClasses == 2 && Mui.CustomClassesMade - made == 2, "one Area class for both graphs and one Group class");
        Check(Mui.CustomObjects == 3, "the dispatcher saw three objects made (OM_NEW)");
        Check(bar.Get(BarGraph.Level) == 30 && other.Get(BarGraph.Level) == 80, "the starting levels went through OnSet and read back through OnGet");

        Check(win.Open(), "the window opens");
        Check(bar.Setups == 1 && other.Setups == 1 && panel.Setups == 1, "MUIM_Setup reached each object once");
        Check(bar.SizeAsks >= 1 && other.SizeAsks >= 1, "MUIM_AskMinMax asked the graphs for their size");
        Check(bar.Draws >= 1 && other.Draws >= 1 && panel.Draws >= 1, "MUIM_Draw drew the graphs and the group");

        int seen = -1;
        int notes = 0;
        // The graph and the slider follow each other: each sets the other only when it differs, so the notifications stop.
        bar.WhenChanged(BarGraph.Level, v =>
        {
            seen = v;
            notes++;
            if (slider.Value != v) slider.Value = v;
        });
        slider.OnValueChanged(v =>
        {
            if (bar.Get(BarGraph.Level) != v) bar.Set(BarGraph.Level, v);
            panel.Set(Panel.Mark, v);
            status.Text = $"Level {v}";
        });
        quit.OnPressed(() => app.Quit());

        int before = bar.Draws;
        int sets = bar.Sets;
        bar.Set(BarGraph.Level, 55);
        Check(bar.Get(BarGraph.Level) == 55 && bar.Sets == sets + 1, "the own attribute's round trip (OM_SET, OM_GET)");
        Check(bar.Draws > before, "setting it redrew the graph");
        Check(seen == 55 && notes == 1, "a notification on the own attribute fired");
        Check(slider.Value == 55 && panel.Get(Panel.Mark) == 55, "and the slider followed the graph (and the group's mark the slider)");
        before = bar.Draws;
        int panelDraws = panel.Draws;
        slider.Value = 70;
        Check(bar.Get(BarGraph.Level) == 70 && seen == 70 && bar.Draws > before, "the slider's notification set the level and redrew");
        Check(panel.Get(Panel.Mark) == 70 && panel.Draws > panelDraws, "and set the group's own attribute, which redrew it");
        Check(other.DoMethod(BarGraph.Reset, 20, 0) == 80 && other.Get(BarGraph.Level) == 20, "the own method ran (old level back, new level set)");
        nuint unknown = 0;
        Check(bar.Get(Muia.Disabled) == false && !other.TryGetRaw(Muia.NumericValue.Tag, out unknown), "other attributes go to the superclass");

        if (wait)
        {
#if !AMIGA
            // Host: what a user would do: a click at the middle of the first graph, a drag to its right edge, the button up (MUI would
            // send these to each graph's handler: the second one is not under the mouse and leaves them), then Quit.
            int h = 32;
            bool ate = bar.HostEvent(MuiEvent.MouseButtons, MuiEvent.SelectDown, 0, 96, h);
            ate = ate && !other.HostEvent(MuiEvent.MouseButtons, MuiEvent.SelectDown, 0, 96 + 200, h);
            ate = ate && bar.HostEvent(MuiEvent.MouseMove, 0, 0, 191, h) && !other.HostEvent(MuiEvent.MouseMove, 0, 0, 191 + 200, h);
            ate = ate && bar.HostEvent(MuiEvent.MouseButtons, MuiEvent.SelectUp, 0, 191, h);
            Check(ate, "host: the graph under the mouse ate the click and the drag, the other one did not");
            quit.HostPress();
#endif
            Dos.PutStr("SHOT\n");
            Dos.FlushOutput();
            app.Run();
            int level = bar.Get(BarGraph.Level);
            Dos.PutStr($"events: level {level}, slider {slider.Value}, {bar.Clicks} click(s), {bar.Moves} move(s); other graph {other.Get(BarGraph.Level)}, {other.Clicks} click(s)\n");
            Check(slider.Value == level, "the slider follows the graph's level");
            if (Args.Has("CLICKS")) Check(bar.Clicks + other.Clicks > 0, "a click reached a graph (MUIM_HandleEvent)");   // scripted runs
        }
        else
        {
            Dos.PutStr("SHOT\n");
            Dos.FlushOutput();
            for (int i = 0; i < 50 && app.Step(); i++) Dos.Delay(2);
        }

        win.Close();
        Check(bar.Cleanups == 1 && other.Cleanups == 1 && panel.Cleanups == 1, "MUIM_Cleanup reached each object when the window closed");
        app.Dispose();
        Heap.Free(ref app);
        Check(Mui.Live == 0 && Mui.CustomObjects == 0, "disposing the application disposed the custom objects (OM_DISPOSE)");
        Check(Mui.CustomClasses == 0, "both classes were deleted after their last object");
    }

    // A graph on its own (never in a window): attributes work, nothing is drawn, and its class comes and goes with it.
    static void Lone(int made)
    {
        BarGraph g = new BarGraph(5);
        Check(g.Create() && Mui.CustomClasses == 1 && Mui.CustomClassesMade - made == 3, "a lone graph makes the Area class again");
        g.Set(BarGraph.Level, 150);
        Check(g.Get(BarGraph.Level) == 100 && g.Draws == 0 && g.Setups == 0, "its level is kept in range and nothing is drawn while it is not shown");
        g.Dispose();
        Heap.Free(ref g);
        Check(Mui.CustomClasses == 0 && Mui.CustomObjects == 0, "the class is deleted with it");
    }
}
