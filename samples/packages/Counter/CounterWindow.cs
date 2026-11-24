using ESharp;
using ESharp.Platform;

namespace Counter;

/// <summary>
/// A window class of a package, derived from the platform's [Extensible] <see cref="GtWindow"/> and itself [Extensible]: a "+1" and a
/// "Reset" button and a count. A game derives from it and overrides <see cref="OnPlus"/>. The close gadget is accepted only after
/// <see cref="Closes"/> has been counted.
/// </summary>
[Extensible]
public class CounterWindow : GtWindow
{
    public GtButton Plus;
    public GtButton Reset;
    public int Count;
    public int Closes;

    public CounterWindow(string title) : base()
    {
        Title = title;
        Width = 200;
        Plus = new GtButton { Label = "+1" };
        Reset = new GtButton { Label = "Reset" };
        Add(Plus);
        Add(Reset);
    }

    protected override void OnClicked(GtGadget gadget)
    {
        if (gadget == Plus) { Count++; OnPlus(); }
        else if (gadget == Reset) Count = 0;
    }

    /// <summary>Called after each "+1" (the count is already raised). Does nothing unless overridden.</summary>
    protected virtual void OnPlus() { }

    protected override bool OnCloseRequest() { Closes++; return true; }
}
