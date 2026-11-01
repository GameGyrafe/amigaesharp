using System;
using ESharp;
using ESharp.Platform;

namespace PropertiesSample;

public struct Player
{
    public int Score { get; set; }
    public int Id { get; }
    int _lives;

    public Player(int id)
    {
        Id = id;
        Score = 0;
        _lives = 3;
    }

    // Accessors with bodies.
    public int Lives
    {
        get { return _lives; }
        set { _lives = value < 0 ? 0 : value > 9 ? 9 : value; }
    }

    // Expression-bodied, read-only.
    public bool Alive => _lives > 0;
    public int Double => Score * 2;

    public void Hit() { Lives--; }
    public void Bonus() { Lives += 2; Score += 100; }
    public int Total() { return this.Score + Lives * 10; }
}

public struct Box<T> where T : unmanaged
{
    public T Item { get; set; }
    public int Reads { get; set; }
    public T Peek => Item;
}

public static class Config
{
    public static int Width { get; set; }
    static int _height = 200;
    public static int Height { get { return _height; } set { _height = value; } }
    public static int Area => Width * Height;
    public static Fix Gravity { get; set; }
}

public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static int Score(Player p) { return p.Score; }

    [EntryPoint]
    public static unsafe void Main()
    {
        Player p = new Player(7);
        p.Score = 40;
        Check(p.Score == 40 && p.Id == 7, "auto properties, get-only set in the constructor");
        Check(p.Lives == 3 && p.Alive, "property getter with a body, expression-bodied getter");
        p.Lives = 20;
        Check(p.Lives == 9, "setter with a body clamps");
        p.Lives = -4;
        Check(p.Lives == 0 && !p.Alive, "setter clamps low, Alive follows");
        p.Lives = 5;
        p.Hit();
        Check(p.Lives == 4, "-- on a property inside a method");
        p.Bonus();
        Check(p.Lives == 6 && p.Score == 140, "compound assignment on properties");
        p.Score++;
        ++p.Score;
        p.Score -= 1;
        Check(p.Score == 141, "++ and -= on an auto-property");
        p.Lives++;
        ++p.Lives;
        Check(p.Lives == 8, "++ on a property with a setter");
        Check(p.Double == 282, "computed property");
        Check(p.Total() == 141 + 80, "this.Property and unqualified property in a method");
        Check(Score(p) == 141, "property of a by-value parameter");

        Player* ptr = &p;
        ptr->Lives = 1;
        ptr->Score += 9;
        Check(ptr->Lives == 1 && p.Score == 150 && ptr->Alive, "properties through a pointer");

        Player[] team = new Player[2];
        team[1].Score = 5;
        team[1].Lives += 4;
        Check(team[1].Score == 5 && team[1].Lives == 4 && team[0].Score == 0, "properties on array elements");

        Box<int> b = default;
        b.Item = 12;
        b.Reads++;
        Check(b.Peek == 12 && b.Reads == 1, "properties in a generic struct");
        Box<Fix> bf = default;
        bf.Item = Fix.FromInt(3);
        bf.Item += Fix.FromInt(2);
        Check(Fix.ToInt(bf.Peek) == 5, "compound assignment with a user-defined operator");

        Config.Width = 320;
        Check(Config.Area == 320 * 200, "static properties");
        Config.Height += 56;
        Check(Config.Height == 256 && Config.Area == 320 * 256, "static property with accessors, compound");
        Config.Gravity = Fix.FromInt(2);
        Check(Fix.ToInt(Config.Gravity * Fix.FromInt(3)) == 6, "static property of a struct type");
    }
}
