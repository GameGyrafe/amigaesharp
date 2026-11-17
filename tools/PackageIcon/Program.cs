using ESharp.Assets;

// Draws the NuGet package icon: a tilted, shaded red and white checkered sphere with a soft drop shadow
// (original artwork, procedural, MIT like the rest of the repository; no existing image was used).
// Usage: dotnet run --project tools/PackageIcon -- src/icon.png
// Output is deterministic: 256x256, transparent background, 4x4 supersampling.
if (args.Length != 1) { Console.Error.WriteLine("usage: PackageIcon <out.png>"); return 2; }

const int Size = 256, Ss = 4, Bands = 8;
const double Cx = 108, Cy = 106, R = 98;              // ball
const double ShX = 134, ShY = 130, ShR = 96, ShSoft = 16; // shadow: offset down-right, soft edge
const double Tilt = 20 * Math.PI / 180, Spin = 12 * Math.PI / 180;
var (lx, ly, lz) = Norm(-0.55, -0.65, 0.52);          // light from upper left, towards the viewer

static (double, double, double) Norm(double x, double y, double z)
{
    var l = Math.Sqrt(x * x + y * y + z * z);
    return (x / l, y / l, z / l);
}

var rgba = new byte[Size * Size * 4];
for (var py = 0; py < Size; py++)
    for (var px = 0; px < Size; px++)
    {
        double sr = 0, sg = 0, sb = 0, sa = 0; // premultiplied sums
        for (var j = 0; j < Ss; j++)
            for (var i = 0; i < Ss; i++)
            {
                var x = px + (i + 0.5) / Ss;
                var y = py + (j + 0.5) / Ss;
                double r = 0, g = 0, b = 0, a = 0;

                // shadow: grey, alpha falls off smoothly at the edge
                var sd = Math.Sqrt((x - ShX) * (x - ShX) + (y - ShY) * (y - ShY));
                var t = Math.Clamp((ShR + ShSoft / 2 - sd) / ShSoft, 0, 1);
                if (t > 0) { a = 0.38 * t * t * (3 - 2 * t); r = g = b = 70; }

                var dx = (x - Cx) / R;
                var dy = (y - Cy) / R;
                var d2 = dx * dx + dy * dy;
                if (d2 <= 1)
                {
                    var dz = Math.Sqrt(1 - d2);
                    // into the ball's frame: undo the tilt (rotation about the view axis), then the spin about the ball's axis
                    var ct = Math.Cos(-Tilt); var st = Math.Sin(-Tilt);
                    var ax = dx * ct - dy * st;
                    var ay = dx * st + dy * ct;
                    var cs = Math.Cos(Spin); var ss = Math.Sin(Spin);
                    var bx = ax * cs + dz * ss;
                    var bz = -ax * ss + dz * cs;
                    var lon = Math.Atan2(bx, bz);               // -pi..pi
                    var lat = Math.Asin(Math.Clamp(ay, -1, 1)); // -pi/2..pi/2
                    var u = (int)Math.Floor((lon + Math.PI) / (2 * Math.PI) * Bands);
                    var v = (int)Math.Floor((lat + Math.PI / 2) / Math.PI * Bands);
                    var red = ((u + v) & 1) == 0;
                    var (cr, cg, cb) = red ? (216.0, 32.0, 32.0) : (250.0, 250.0, 250.0);

                    var diff = Math.Max(0, dx * lx + dy * ly + dz * lz);
                    var shade = 0.34 + 0.78 * diff;
                    // reflected highlight
                    var (hx, hy, hz) = Norm(lx, ly, lz + 1);
                    var spec = Math.Pow(Math.Max(0, dx * hx + dy * hy + dz * hz), 40) * 0.35;
                    // cool, darker rim on the shaded side
                    var rim = Math.Pow(1 - dz, 3) * 0.10;
                    r = Math.Min(255, cr * shade + 255 * spec - 255 * rim * 0.5);
                    g = Math.Min(255, cg * shade + 255 * spec - 255 * rim * 0.5);
                    b = Math.Min(255, cb * shade + 255 * spec + 40 * rim * (1 - diff));
                    a = 1;
                }
                sr += r * a; sg += g * a; sb += b * a; sa += a;
            }
        var n = Ss * Ss;
        var o = (py * Size + px) * 4;
        var alpha = sa / n;
        if (alpha > 0)
        {
            rgba[o] = (byte)Math.Clamp(Math.Round(sr / sa), 0, 255);
            rgba[o + 1] = (byte)Math.Clamp(Math.Round(sg / sa), 0, 255);
            rgba[o + 2] = (byte)Math.Clamp(Math.Round(sb / sa), 0, 255);
            rgba[o + 3] = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);
        }
    }

File.WriteAllBytes(args[0], Png.Write(new RgbaImage(Size, Size, rgba)));
Console.WriteLine($"wrote {args[0]}");
return 0;
