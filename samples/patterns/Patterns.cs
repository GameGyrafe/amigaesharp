using System;
using ESharp;
using ESharp.Platform;

namespace PatternsSample;

/// <summary>A card: a struct with a Deconstruct, so positional patterns can take it apart.</summary>
public struct Card
{
    public int Rank;
    public int Suit;

    public Card(int rank, int suit) { Rank = rank; Suit = suit; }

    public void Deconstruct(out int rank, out int suit) { rank = Rank; suit = Suit; }

    public bool IsFace { get { return Rank > 10 && Rank < 14; } }
}

public class Piece
{
    public int Col;
    public int Row;
    public virtual int Moves() { return 0; }
}

public class Rook : Piece
{
    public bool Moved;
    public Rook(int col, int row) { Col = col; Row = row; }
    public void Deconstruct(out int col, out int row) { col = Col; row = Row; }
    public override int Moves() { return 14; }
}

public class Pawn : Piece
{
    public Pawn(int col, int row) { Col = col; Row = row; }
    public override int Moves() { return Row == 1 ? 2 : 1; }
}

public class Board
{
    public Piece Corner;
    public int Reads;
    public int Size { get { Reads++; return 8; } }
}

public static class Program
{
    static int s_calls;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static Card Draw()
    {
        s_calls++;
        return new Card(12, 3);
    }

    static string Name(Card c) => c switch
    {
        (1, _) => "ace",
        (13, 0) => "king of clubs",
        (> 10 and < 14, var s) when s == 3 => "spade face",
        { IsFace: true } => "face",
        (var r, _) when r >= 2 && r <= 10 => "pip",
        _ => "?",
    };

    static int Score(Piece p) => p switch
    {
        Rook(0 or 7, 0 or 7) { Moved: false } => 10,
        Rook(var c, _) => c,
        Pawn { Row: 6 } => 50,
        Pawn => 1,
        null => -1,
        _ => 0,
    };

    static int Hand(Span<int> cards) => cards switch
    {
        [] => 0,
        [var one] => one,
        [var a, var b] when a == b => 100 + a,
        [var first, .., var last] => first * 10 + last,
    };

    static int Inner(ReadOnlySpan<int> xs)
    {
        if (xs is [_, .. var mid, _] && mid.Length > 0)
        {
            int sum = 0;
            for (int i = 0; i < mid.Length; i++) sum += mid[i];
            return sum;
        }
        return -1;
    }

    static string Direction(int dx, int dy)
    {
        switch (dx, dy)
        {
            case (0, 0):
                return "still";
            case (0, < 0):
                return "up";
            case (0, > 0):
                return "down";
            case (var x, 0) when x < 0:
                return "left";
            case (_, 0):
                return "right";
            default:
                return "diagonal";
        }
    }

    static int Kind(Piece p)
    {
        int k = 0;
        switch (p)
        {
            case Rook(var c, var r) when c == r:
                k = 1;
                break;
            case Rook:
                k = 2;
                break;
            case Pawn { Row: > 3 } pawn:
                k = 30 + pawn.Row;
                break;
            case null:
                k = -1;
                break;
            default:
                k = 9;
                break;
        }
        return k;
    }

    [EntryPoint]
    public static void Main()
    {
        var pair = (3, "hearts");
        Check(pair is (3, "hearts") && pair is (_, not "spades") && !(pair is (4, _)), "tuple positional pattern");
        Check(pair is (var n, var s) && n == 3 && s == "hearts", "tuple pattern binds");

        Check(new Card(5, 2) is (5, var suit) && suit == 2, "struct Deconstruct");
        Check(Name(new Card(1, 0)) == "ace" && Name(new Card(13, 0)) == "king of clubs" && Name(new Card(12, 3)) == "spade face"
            && Name(new Card(11, 1)) == "face" && Name(new Card(7, 1)) == "pip", "switch expression over a struct");

        s_calls = 0;
        bool hit = Draw() is (12, _) and { IsFace: true };
        Check(hit && s_calls == 1, "subject evaluated once");

        Check(new Card(9, 1) is var (r9, s1) && r9 == 9 && s1 == 1, "var (a, b)");

        Check(Score(new Rook(0, 7)) == 10 && Score(new Rook(3, 0)) == 3 && Score(new Pawn(2, 6)) == 50
            && Score(new Pawn(2, 2)) == 1 && Score(null) == -1, "class positional and property patterns");
        Rook moved = new Rook(7, 7);
        moved.Moved = true;
        Check(Score(moved) == 7, "property pattern on a field");

        Board b = new Board();
        Check(b is { Size: > 4 and < 10 } && b.Reads == 1, "getter read once");
        Check(!(b is { Corner.Col: 0 }), "extended property pattern stops at null");
        b.Corner = new Rook(0, 0);
        Check(b is { Corner.Col: 0, Corner.Row: 0 } && b is { Corner: Rook(0, 0) }, "extended and nested patterns");
        Board none = null;
        Check(!(none is { }) && b is { } && !(none is Board), "{ } and type tests never match null");

        int[] deck = { 2, 3, 4, 5 };
        Check(deck is [2, 3, 4, 5] && deck is [2, ..] && deck is [.., 5] && deck is [_, 3, ..] && !(deck is [2, 3]), "list patterns on an array");
        Check(deck is [var lo, .., var hi] && lo == 2 && hi == 5, "first and last of an array");

        int[] one = { 6 };
        int[] twins = { 4, 4 };
        Span<int> all = deck;
        Check(Hand(deck) == 25 && Hand(one) == 6 && Hand(twins) == 104 && Hand(all.Slice(0, 0)) == 0, "list switch expression over spans");
        Check(Inner(deck) == 7 && Inner(one) == -1, "slice of a span");

        Check("ace" is ['a', ..] && "ace" is [.., 'e'] && !("ace" is [_, _]), "list patterns on a string");

        Vec<int> v = new Vec<int>(4, false);
        v.Add(10);
        v.Add(20);
        v.Add(30);
        Check(v is [10, _, 30] && v is [.., > 25] && !(v is [10]), "list patterns on a Vec");
        Check(v is [var head, ..] && head == 10, "Vec binding");
        v.Destroy();
        Check(v is [], "empty Vec");

        Check(Direction(0, 0) == "still" && Direction(0, -1) == "up" && Direction(0, 2) == "down"
            && Direction(-3, 0) == "left" && Direction(3, 0) == "right" && Direction(1, 1) == "diagonal", "switch statement on a tuple");
        Check(Kind(new Rook(2, 2)) == 1 && Kind(new Rook(1, 2)) == 2 && Kind(new Pawn(0, 5)) == 35
            && Kind(new Pawn(0, 1)) == 9 && Kind(null) == -1, "switch statement with class patterns");

        int count = 3;
        string size = "";
        switch (count)
        {
            case < 0: size = "negative"; break;
            case 0 or 1: size = "small"; break;
            case > 1 and < 10: size = "medium"; break;
            default: size = "large"; break;
        }
        Check(size == "medium", "relational case labels");
    }
}
