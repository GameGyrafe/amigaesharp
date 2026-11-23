using ESharp;

namespace Tween;

/// <summary>
/// A point that moves along a tween, one frame per <see cref="Step"/>. Marked <see cref="ExtensibleAttribute"/>: a game may
/// derive its own movers from it (across the package boundary) and override <see cref="Shape"/> (the curve, linear here) and
/// <see cref="OnArrived"/> (called once at the end of each run). A class of a package without [Extensible] cannot be a base
/// class of the game's classes (ES0117).
/// </summary>
[Extensible]
public class Mover
{
    public Tween<Point> Path;
    public Point At;
    public int Arrivals;

    public Mover(Point from, Point to, int frames)
    {
        Path = new Tween<Point>(from, to, frames, Curve.Linear);
        At = from;
    }

    /// <summary>The eased progress for the linear progress <paramref name="t"/> (0..Ease.One): the curve of this mover.</summary>
    protected virtual int Shape(int t) => Ease.Linear(t);

    /// <summary>Called once each time the mover reaches the end of its path.</summary>
    protected virtual void OnArrived() { }

    /// <summary>One frame along the path; false (and no move) once at its end.</summary>
    public bool Step()
    {
        if (!Path.Step()) return false;
        int t = Shape(Path.Frame * Ease.One / Path.Frames);
        At = new Point(Lerp.Int(Path.From.X, Path.To.X, t), Lerp.Int(Path.From.Y, Path.To.Y, t));
        if (Path.Done())
        {
            Arrivals++;
            OnArrived();
        }
        return true;
    }

    /// <summary>Steps to the end of the path; the number of frames taken.</summary>
    public int Finish()
    {
        int frames = 0;
        while (Step()) frames++;
        return frames;
    }
}
