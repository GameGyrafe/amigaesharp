using ESharp;
using ESharp.Platform;

namespace InheritSample;

public interface IDraw
{
    int Draw(int scale);
}

/// <summary>An abstract base class: shared fields, a constructor, abstract and virtual members.</summary>
public abstract class Shape
{
    public int Id;
    public int Created;

    public Shape(int id) { Id = id; Created = 1; }

    public abstract int Area();
    public virtual int Sides { get { return 0; } }
    public virtual int Weight(int factor) { return Area() * factor; }
    public int Describe() { return Area() * 10 + Id; }       // a non-virtual method that calls virtual ones
    public virtual int Kind() { return 1; }
}

public class Rect : Shape, IDraw
{
    public int W;
    public int H;

    public Rect(int id, int w, int h) : base(id) { W = w; H = h; Created += 10; }
    public Rect(int id, int side) : this(id, side, side) { Created += 100; }

    public override int Area() { return W * H; }
    public override int Sides { get { return 4; } }
    public override int Kind() { return base.Kind() + 10; }
    public virtual int Draw(int scale) { return Area() * scale; }
}

public class Square : Rect
{
    public Square(int id, int side) : base(id, side, side) { Created += 1000; }

    public override int Kind() { return base.Kind() + 100; }
    public override int Draw(int scale) { return base.Draw(scale) + 1; }   // an interface call must reach this override
    public int Perimeter() { return 4 * W; }                  // uses a field two levels up
}

public class Circle : Shape
{
    public int R;
    public Circle(int id, int r) : base(id) { R = r; }
    public override int Area() { return 3 * R * R; }
    public override int Weight(int factor) { return 7 + factor; }
}

/// <summary>A derived class with no constructor of its own, over a base with a parameterless constructor.</summary>
public class Counter
{
    public int Count;
    public Counter() { Count = 5; }
    public virtual int Next() { Count++; return Count; }
}

public class DoubleCounter : Counter
{
    public override int Next() { Count += 2; return Count; }
}

/// <summary>A class that is not in a hierarchy at all must keep working exactly as before.</summary>
public class Plain
{
    public int V;
    public Plain(int v) { V = v; }
    public int Twice() { return V * 2; }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static int AreaOf(Shape s) { return s.Area(); }

    static int Drawn(IDraw d, int scale) { return d.Draw(scale); }

    static string KindName(Shape s)
    {
        if (s is Square) return "square";
        if (s is Rect) return "rect";
        if (s is Circle) return "circle";
        return "other";
    }

    static bool Same(string a, string b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    [EntryPoint]
    public static void Main()
    {
        Rect r = new Rect(1, 3, 4);
        Check(r.Area() == 12 && r.W == 3 && r.Id == 1, "derived fields and inherited fields");
        Check(r.Created == 11, "base constructor runs first, then the derived body");
        Rect sq2 = new Rect(2, 5);
        Check(sq2.W == 5 && sq2.H == 5 && sq2.Created == 111, "this(...) chains to a constructor that calls base(...)");
        Square sq = new Square(3, 6);
        Check(sq.Area() == 36 && sq.Perimeter() == 24 && sq.Id == 3, "two levels of inheritance");
        Check(sq.Created == 1011, "constructors run base to derived");

        Shape s = r;
        Check(s.Area() == 12 && AreaOf(sq) == 36, "virtual call through the base type");
        Shape c = new Circle(4, 2);
        Check(c.Area() == 12 && AreaOf(c) == 12, "another override");
        Check(r.Describe() == 121 && c.Describe() == 124, "a base method calls virtual methods of the derived object");
        Check(s.Sides == 4 && c.Sides == 0 && sq.Sides == 4, "virtual property");
        Check(s.Weight(2) == 24 && c.Weight(2) == 9, "virtual with an argument, inherited and overridden");
        Check(s.Kind() == 11 && ((Shape)sq).Kind() == 111 && c.Kind() == 1, "base.Method() calls");

        Check(Same(KindName(sq), "square") && Same(KindName(r), "rect") && Same(KindName(c), "circle"), "is: tests along the chain");
        Shape nobody = null;
        Check(!(nobody is Rect) && nobody is null, "is on null");
        Rect asRect = s as Rect;
        Rect notRect = c as Rect;
        Check(asRect != null && asRect.W == 3 && notRect == null, "as");
        if (s is Rect found && found.H == 4) Check(true, "is with a declaration");
        else Check(false, "is with a declaration");
        if (c is not Rect) Check(true, "is not");
        else Check(false, "is not");
        Square down = (Square)(Shape)sq;
        Check(down.Perimeter() == 24, "explicit downcast");

        IDraw d = r;
        Check(d.Draw(2) == 24 && Drawn(r, 3) == 36, "interface on a derived class");
        Check(Drawn(sq, 2) == 73, "interface implemented by a base class, used on a derived one");

        Counter cn = new DoubleCounter();
        Check(cn.Count == 5 && cn.Next() == 7 && cn.Next() == 9, "implicit derived constructor runs the base constructor");
        Counter base1 = new Counter();
        Check(base1.Next() == 6, "a base object still uses its own method");

        Plain pl = new Plain(21);
        Check(pl.Twice() == 42, "a class outside any hierarchy");

        Shape alias = sq;
        sq.W = 1;
        Check(alias.Area() == 6, "a base reference shares the object");

        Heap.Free(ref s);
        Check(s == null, "Heap.Free through a base variable");
        Heap.Free(ref c);
        Heap.Free(ref cn);
        Heap.Free(ref base1);
        Heap.Free(ref pl);
        Heap.Free(ref sq2);
        Heap.Free(ref sq);
    }
}
