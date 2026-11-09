using ESharp;
using ESharp.Platform;

// E# types named like the system's own C structs (struct Window, struct Node, struct List, ...), and two pairs of
// same-named types in different namespaces. Every E# type gets its own prefixed, namespaced C name, so none of these
// clash with the AmigaOS / AROS headers (intuition and exec are both included here) or with each other.
// Prints "ok NAME" or "FAIL NAME".

namespace NamesSample
{
    public interface IDraw
    {
        int Draw();
    }

    /// <summary>exec's struct Node: here a base class with a virtual member.</summary>
    public class Node
    {
        public Node? Next;
        public int Pri;
        public Node(int pri) { Pri = pri; }
        public virtual int Kind() { return 1; }
    }

    /// <summary>exec's struct Task and struct Message: classes derived from our Node.</summary>
    public class Task : Node
    {
        public Task(int pri) : base(pri) { }
        public override int Kind() { return 2; }
    }

    public class Message : Node
    {
        public int Length;
        public Message(int length) : base(0) { Length = length; }
        public override int Kind() { return 3; }
    }

    /// <summary>exec's struct List: a list of our Nodes.</summary>
    public class List
    {
        public Node? Head;
        public int Count;
        public void Add(Node n) { n.Next = Head; Head = n; Count++; }
        public int SumKinds()
        {
            int sum = 0;
            for (Node? n = Head; n != null; n = n.Next) sum += n.Kind();
            return sum;
        }
    }

    /// <summary>intuition's struct Window, Screen, Gadget, Menu and Image.</summary>
    public class Screen
    {
        public int Width;
        public Screen(int width) { Width = width; }
    }

    public class Window
    {
        public Screen Parent;
        public int Left;
        public Window(Screen parent, int left) { Parent = parent; Left = left; }
        public int Right() { return Parent.Width - Left; }
    }

    public class Gadget : IDraw
    {
        public int Id;
        public Gadget(int id) { Id = id; }
        public int Draw() { return Id * 10; }
    }

    public class Image : IDraw
    {
        public int Depth;
        public Image(int depth) { Depth = depth; }
        public int Draw() { return Depth; }
    }

    public class Menu
    {
        public int Items;
        public static int Made;
        public Menu(int items) { Items = items; Made++; }
    }

    /// <summary>graphics' struct Rectangle as an E# struct, intuition's struct Border as an enum.</summary>
    public struct Rectangle
    {
        public int MinX, MinY, MaxX, MaxY;
        public int Width() { return MaxX - MinX + 1; }
    }

    public enum Border { None, Thin, Thick }

    /// <summary>utility's struct Hook as a delegate type.</summary>
    public delegate int Hook(int x);

    /// <summary>A generic struct used with both of the same-named Point types below.</summary>
    public struct Box<T>
    {
        public T Item;
        public Box(T item) { Item = item; }
    }

    public static class Program
    {
        static void Check(bool ok, string name)
        {
            Dos.PutStr(ok ? "ok " : "FAIL ");
            Dos.PutStr(name);
            Dos.PutStr("\n");
        }

        static int Paint(IDraw d) { return d.Draw(); }

        static int Twice(int x) { return x * 2; }

        [EntryPoint]
        public static void Main()
        {
            List list = new List();
            list.Add(new Node(5));
            list.Add(new Task(1));
            list.Add(new Message(64));
            Check(list.Count == 3 && list.SumKinds() == 6, "Node, Task and Message in a List");
            Node? first = list.Head;
            Check(first is Message m && m.Length == 64, "a Message is a Node");

            Screen screen = new Screen(640);
            Window window = new Window(screen, 40);
            Check(window.Right() == 600, "a Window on a Screen");
            Check(Paint(new Gadget(3)) == 30 && Paint(new Image(5)) == 5, "Gadget and Image through an interface");
            Menu menu = new Menu(4);
            Check(menu.Items == 4 && Menu.Made == 1, "Menu with a static field");

            Rectangle r = new Rectangle();
            r.MinX = 10; r.MaxX = 19;
            Border b = Border.Thick;
            Check(r.Width() == 10 && b == Border.Thick && (int)b == 2, "Rectangle struct and Border enum");
            Hook hook = Twice;
            Check(hook(21) == 42, "Hook delegate");

            Left.Item left = new Left.Item();
            Right.Item right = new Right.Item();
            Check(left.Side() == 1 && right.Side() == 2, "two classes called Item in different namespaces");
            Left.Point lp = new Left.Point(); lp.X = 7;
            Right.Point rp = new Right.Point(); rp.X = 8; rp.Y = 9;
            Check(lp.X + rp.X + rp.Y == 24, "two structs called Point in different namespaces");
            Box<Left.Point> lb = new Box<Left.Point>(lp);
            Box<Right.Point> rb = new Box<Right.Point>(rp);
            Check(lb.Item.X == 7 && rb.Item.Y == 9, "a generic struct over both Points");
            Check(Left.Tools.Count() + Right.Tools.Count() == 30, "static classes called Tools in different namespaces");

#if AMIGA
            Check(IntuitionTime.Micros() < 1000000, "intuition.library CurrentTime");
#else
            Check(true, "intuition.library CurrentTime");
#endif
        }
    }

#if AMIGA
    /// <summary>One intuition call, so the generated C includes proto/intuition.h (struct Window, Screen, Gadget, ...).</summary>
    public static unsafe class IntuitionTime
    {
        [AmigaLib("intuition", -84)] public static extern void CurrentTime(uint* seconds, uint* micros);

        public static uint Micros()
        {
            uint seconds = 0, micros = 0;
            CurrentTime(&seconds, &micros);
            return micros;
        }
    }
#endif
}

namespace NamesSample.Left
{
    public class Item { public int Side() { return 1; } }
    public struct Point { public int X; }
    public static class Tools { public static int Count() { return 10; } }
}

namespace NamesSample.Right
{
    public class Item { public int Side() { return 2; } }
    public struct Point { public int X; public int Y; }
    public static class Tools { public static int Count() { return 20; } }
}
