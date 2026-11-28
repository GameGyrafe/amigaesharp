using System;
using System.Linq;
using ESharp;
using ESharp.Platform;

namespace ClosuresSample;

// Heap closures and events (issue #41): capturing lambdas that are stored and outlive the method that made them.
// There is no garbage collector: a stored closure is freed by its owner with Heap.Free, an event owns its handlers
// (-= drops one, Heap.Free(ref E) inside the class drops them all). Heap.LiveClosures() counts the closure references
// not yet freed, so the last check finds a leak on the Amiga (it is always 0 under dotnet). A frame that holds a delegate it
// owns (an [Owned] parameter, a setter's value) releases it when the frame goes (issue #85). A value the caller keeps
// (a field, or a local it uses again or frees) gives an [Owned] parameter or a setter a reference of its own (issue #110).

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

// += and -= on a delegate that is not an event (issue #164): a multicast delegate, as in dotnet. The variable owns its closures
// (Heap.Free(ref d) releases them), -= drops the entry it removes, and a value the caller keeps gets its own reference.
public class Chain
{
    public Action? Steps;
    public Action? Auto { get; set; }
    public static Func<int>? Last;
}

// A static field of reference type in a struct is not part of the value (issue #164): ES0010 allows it.
public struct Lookup
{
    public static int[] Primes = { 2, 3, 5, 7 };
    public static Action? OnUse;
    public int Index;
    public int Get() { OnUse?.Invoke(); return Primes[Index]; }
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
    static void Hit() { log = log * 10 + 9; }

    // Issue #175: delegates with += whose list is used while code of the program changes them.
    static Action? Hooks;
    static Action<int>? Sink;
    static Func<int, bool>? Odd;
    static Action MakeNote(int d) => () => Note(d);
    static void RunGrowing(Action a) { Hooks += MakeNote(3); a(); }
    static int GrowSink() { Sink += v => Note(v + 1); return 5; }
    static Action MoreHooks() { Hooks += MakeNote(6); return MakeNote(7); }

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
        // A value the caller keeps using (and frees itself) gives the setter a reference of its own (issue #110).
        int u = 0;
        Action bump = () => u++;
        Relay relay2 = new Relay();
        relay2.Target = bump;
        bump();
        Heap.Free(ref bump);
        relay2.Fire();
        Check(u == 3, "a shared value given to a setter keeps its own reference");
        relay2.Dispose();
        Heap.Free(ref relay2);

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

        // += and -= on delegates that are not events.
        log = 0;
        Action m = () => Note(1);
        m += () => Note(2);
        m += () => Note(3);
        m();
        Check(log == 123, "+= on a local delegate calls in order");
        Action dup = () => Note(4);
        m += dup;
        m += dup;
        m -= dup;
        log = 0;
        m();
        Check(log == 1234, "-= removes the last match of a kept variable");
        m -= dup;
        log = 0;
        m();
        Check(log == 123, "-= removes the other one");
        Heap.Free(ref dup);
        Heap.Free(ref m);
        Check(m == null, "Heap.Free on a multicast delegate");

        Action one = Program.Hit;
        one += Program.Hit;
        log = 0;
        one();
        Check(log == 99, "method groups are added too");
        one -= Program.Hit;
        one -= Program.Hit;
        Check(one == null, "removing the last entry leaves null");

        Chain ch = new Chain();
        int total = 0;
        ch.Steps += () => total += 1;
        ch.Steps += () => total += 10;
        ch.Auto += () => total += 100;
        ch.Auto += () => total += 1000;
        ch.Steps!();
        ch.Auto!();
        Check(total == 1111, "a field and an auto-property");
        Heap.Free(ref ch.Steps);
        Action? auto = ch.Auto;
        ch.Auto = null;
        Heap.Free(ref auto);
        Chain.Last += () => 1;
        Chain.Last += () => 2;
        Check(Chain.Last!() == 2, "a Func gives the last result");
        Heap.Free(ref Chain.Last);
        Heap.Free(ref ch);

        Lookup l = new Lookup();
        l.Index = 2;
        int used = 0;
        Lookup.OnUse += () => used++;
        Lookup.OnUse += () => used += 10;
        Check(l.Get() == 5 && used == 11, "a static array and a static delegate in a struct");
        Heap.Free(ref Lookup.OnUse);

        // Issue #175: the list of a delegate with += / -= is held while code of the program runs next to it: given to a borrowed
        // parameter, called with an argument that runs code, the target of a += whose operand runs code, and given to an inlined
        // LINQ operator. As in dotnet, that code changing the variable does not change the list already taken.
        Hooks = MakeNote(1);
        Hooks += MakeNote(2);
        log = 0;
        RunGrowing(Hooks!);
        Check(log == 12, "a delegate given to a borrowed parameter runs the list it was given while the callee adds to it");
        log = 0;
        Hooks!();
        Check(log == 123, "the entry the callee added runs next time");
        Sink = v => Note(v);
        log = 0;
        Sink!(GrowSink());
        Check(log == 5, "an argument that adds to the delegate runs after the delegate is loaded");
        log = 0;
        Sink!(1);
        Check(log == 12, "the added entry runs next time");
        Heap.Free(ref Hooks);
        Hooks = MakeNote(1);
        Hooks += MoreHooks();
        log = 0;
        Hooks!();
        Check(log == 17, "a += whose operand adds to the same delegate adds to the value it loaded, as in dotnet");
        int[] nums = { 1, 2, 3, 4, 5 };
        Odd = v => v > 100;
        Odd += v => v % 2 == 1;
        Check(nums.Count(Odd!) == 3 && Odd is { }, "a multicast predicate given to an inlined LINQ operator");
        Heap.Free(ref Hooks);
        Heap.Free(ref Sink);
        Heap.Free(ref Odd);

        b.Dispose();
        Heap.Free(ref b);
        Heap.Free(ref s);
        Check(Heap.LiveClosures() == before, "every stored closure was freed");
    }
}
