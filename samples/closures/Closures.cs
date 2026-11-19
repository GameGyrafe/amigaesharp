using System;
using ESharp;
using ESharp.Platform;

namespace ClosuresSample;

// Heap closures and events (issue #41): capturing lambdas that are stored and outlive the method that made them.
// There is no garbage collector: a stored closure is freed by its owner with Heap.Free, an event owns its handlers
// (-= drops one, Heap.Free(ref E) inside the class drops them all). Heap.LiveClosures() counts the closure references
// not yet freed, so the last check finds a leak on the Amiga (it is always 0 under dotnet). A frame that holds a delegate it
// owns (an [Owned] parameter, a setter's value) releases it when the frame goes (issue #85).

public class Button
{
    public event Action? Clicked;
    public event Action<int>? Moved;

    public void Click() { Clicked?.Invoke(); }
    public void Move(int dx) { Moved?.Invoke(dx); }
    public bool HasHandlers => Clicked != null;

    // The button owns its handlers: drop them before the button itself is freed.
    public void Dispose()
    {
        Heap.Free(ref Clicked);
        Heap.Free(ref Moved);
    }
}

public class Holder
{
    public Func<int>? Get;
    public Action? Run;
    Action? _tick;

    // [Owned]: this method keeps the delegate it is given, so a capturing lambda passed here is a heap closure.
    public void OnTick([Owned] Action a) { _tick = a; }
    public void Tick() { _tick!(); }

    public void Dispose()
    {
        Heap.Free(ref Get);
        Heap.Free(ref Run);
        Heap.Free(ref _tick);
    }
}

// A setter's value is owned by the setter: the stored lambda captures it, and the setter's frame releases it when that lambda goes.
public class Relay
{
    Action? _fire;
    public Action Target { set { _fire = () => { value(); value(); }; } }
    public void Fire() { _fire!(); }
    public void Dispose() { Heap.Free(ref _fire); }
}

public class Score
{
    public int Points;
    public int Bonus = 5;

    // A stored lambda that uses 'this': the frame keeps the object pointer.
    public void Wire(Button b) { b.Clicked += () => Points += Bonus; }
}

public static class Bus
{
    public static event Action<int>? Changed;
    public static void Set(int v) { Changed?.Invoke(v); }
    public static bool Quiet => Changed == null;
    public static void Clear() { Heap.Free(ref Changed); }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    // A returned closure: its own copy of 'n' lives on after MakeCounter returns.
    static Func<int> MakeCounter(int start)
    {
        int n = start;
        return () => n++;
    }

    static Func<int> MakeAdder(int k) => () => k * 3;

    // An [Owned] parameter captured by the returned closure: freeing that closure also releases the one it was given.
    static Func<int> Twice([Owned] Func<int> f) => () => f() + f();

    static void Repeat(int times, Action body) { for (int i = 0; i < times; i++) body(); }

    static int log;
    static Action? selfRemoving;
    static Button shared = null!;

    static void Note(int digit) { log = log * 10 + digit; }

    [EntryPoint]
    public static void Main()
    {
        int before = Heap.LiveClosures();

        Button b = new Button();
        int count = 0;
        b.Clicked += () => count++;
        b.Click();
        b.Click();
        Check(count == 2, "an event handler that captures a local");

        count = 10;
        b.Click();
        Check(count == 11, "the handler and the method share the captured local");

        Action h = () => count += 100;
        b.Clicked += h;
        b.Click();
        Check(count == 112, "two handlers run");
        b.Clicked -= h;
        b.Click();
        Check(count == 113, "-= removes a handler kept in a variable");
        Heap.Free(ref h);
        Check(h == null, "Heap.Free sets the delegate to null");

        int moved = 0;
        b.Moved += dx => moved += dx;
        b.Moved += dx => moved += dx * 10;
        b.Move(3);
        Check(moved == 33, "an event with an argument calls every handler");

        Button idle = new Button();
        idle.Click();
        Check(!idle.HasHandlers, "raising an event with no handlers does nothing");
        Heap.Free(ref idle);

        Func<int> c1 = MakeCounter(5);
        Func<int> c2 = MakeCounter(50);
        c1();
        c1();
        Check(c1() == 7 && c2() == 50, "returned closures keep their own variables");
        Heap.Free(ref c1);
        Heap.Free(ref c2);

        Func<int> add = MakeAdder(4);
        Check(add() == 12, "an expression-bodied method returns a closure over its parameter");
        Heap.Free(ref add);

        Holder holder = new Holder();
        int x = 1;
        holder.Get = () => x * 2;
        x = 5;
        Check(holder.Get!() == 10, "a closure stored in a field sees later changes");
        holder.Run = () => x = 42;
        holder.Run!();
        Check(x == 42, "a stored closure writes the method's variable");
        int ticks = 0;
        holder.OnTick(() => ticks++);
        holder.Tick();
        holder.Tick();
        Check(ticks == 2, "an [Owned] parameter keeps the closure");
        Repeat(3, () => ticks++);
        Check(ticks == 5, "a borrowed lambda shares a variable with a stored one");
        holder.Dispose();
        Heap.Free(ref holder);

        Func<int>[] fs = new Func<int>[3];
        for (int i = 0; i < 3; i++)
        {
            int j = i * 10;
            fs[i] = () => j + 1;
        }
        Check(fs[0]() == 1 && fs[1]() == 11 && fs[2]() == 21, "each loop iteration gets its own captured variable");
        for (int i = 0; i < 3; i++) Heap.Free(ref fs[i]);

        int t = 0;
        Func<int> twice = Twice(() => ++t);
        Check(twice() == 3, "a stored lambda over an [Owned] parameter");
        Heap.Free(ref twice);
        Relay relay = new Relay();
        relay.Target = () => t += 10;
        relay.Fire();
        Check(t == 22, "a stored lambda over a setter's value");
        relay.Dispose();
        Heap.Free(ref relay);

        Score s = new Score();
        s.Wire(b);
        b.Click();
        Check(s.Points == 5 && count == 114, "a stored lambda that uses this");

        log = 0;
        shared = new Button();
        shared.Clicked += () => Note(1);
        selfRemoving = () => { Note(2); shared.Clicked -= selfRemoving; };
        shared.Clicked += selfRemoving;
        shared.Clicked += () => { Note(3); shared.Clicked += () => Note(9); };
        shared.Click();
        Check(log == 123, "a raise uses a snapshot: removal and addition wait for the next raise");
        log = 0;
        shared.Click();
        Check(log == 139, "the removed handler is gone and the added one runs");
        Heap.Free(ref selfRemoving);
        shared.Dispose();
        Heap.Free(ref shared);

        int last = 0;
        Bus.Changed += v => last = v;
        Bus.Set(7);
        Check(last == 7 && !Bus.Quiet, "a static event");
        Bus.Clear();
        Bus.Set(8);
        Check(last == 7 && Bus.Quiet, "Heap.Free on an event drops every handler");

        b.Dispose();
        Heap.Free(ref b);
        Heap.Free(ref s);
        Check(Heap.LiveClosures() == before, "every stored closure was freed");
    }
}
