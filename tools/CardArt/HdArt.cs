using ESharp.Assets;

// The high-resolution art set (the "@2x" sheet): the same cards, drawn at 64x80 with vector shapes and anti-aliasing instead of
// 32x40 pixel by pixel, plus a smoothed copy of the built-in 5x7 font for the 640x400 screen. Everything is drawn here, in this file
// (original artwork, MIT like the rest of the repository).
//
// Anti-aliased edges are baked against the colour the sprite is drawn over (the table, or the button for the arrows), because the
// blitter mask is one bit: a pixel is either opaque (already blended with its background) or transparent.
// Sheet layout (896 x 496):  y 0..399   cards, 14 x 5 cells of 64x80 (as the 32x40 sheet)
//                            y 400      FontInk: 64 glyphs (ASCII 32..95) of 12x16, ink on the table
//                            y 416      FontGold: the same in gold on the table
//                            y 432..495 FontBig: 2 rows of 32 glyphs of 24x32, ink on a button (the 2x text)
internal static class HdArt
{
    public const int CellW = 64, CellH = 80, Cols = 14, Rows = 5;
    const int SS = 4;                                   // sub-samples per pixel in each direction

    record struct Rgb(float R, float G, float B);
    static Rgb C(int r, int g, int b) => new(r, g, b);

    static readonly Rgb Table = C(0, 119, 34), Button = C(34, 170, 51);
    static readonly Rgb White = C(255, 255, 255), Black = C(16, 16, 16), Red = C(210, 16, 24), Grey = C(120, 126, 120);
    static readonly Rgb Blue = C(34, 51, 204), LightBlue = C(120, 150, 255), Gold = C(255, 204, 0), Ink = C(255, 255, 255);

    // ------------------------------------------------------------ painter: a super-sampled cell of coverage and colour

    sealed class Painter
    {
        public readonly int W, H;
        readonly float[] r, g, b, a;                    // per sub-sample, colour not premultiplied
        public bool Rot;                                // draw rotated by 180 degrees (the bottom-right index and the lower pips)
        public Painter(int w, int h)
        {
            W = w; H = h;
            var n = w * SS * h * SS;
            r = new float[n]; g = new float[n]; b = new float[n]; a = new float[n];
        }

        public void Fill(Func<double, double, bool> inside, Rgb c, double x0, double y0, double x1, double y1)
        {
            if (Rot) { var nx0 = W - x1; var nx1 = W - x0; var ny0 = H - y1; var ny1 = H - y0; x0 = nx0; x1 = nx1; y0 = ny0; y1 = ny1; }
            var sx0 = Math.Max(0, (int)Math.Floor(x0 * SS)); var sx1 = Math.Min(W * SS - 1, (int)Math.Ceiling(x1 * SS));
            var sy0 = Math.Max(0, (int)Math.Floor(y0 * SS)); var sy1 = Math.Min(H * SS - 1, (int)Math.Ceiling(y1 * SS));
            for (var sy = sy0; sy <= sy1; sy++)
                for (var sx = sx0; sx <= sx1; sx++)
                {
                    var x = (sx + 0.5) / SS; var y = (sy + 0.5) / SS;
                    if (Rot) { x = W - x; y = H - y; }
                    if (!inside(x, y)) continue;
                    var i = sy * W * SS + sx;
                    r[i] = c.R; g[i] = c.G; b[i] = c.B; a[i] = 1;
                }
        }

        /// <summary>Per pixel colour baked over <paramref name="bg"/>; alpha 0 where nothing was drawn at all.</summary>
        public void Resolve(Rgb bg, byte[] rgba, int stride, int ox, int oy)
        {
            for (var py = 0; py < H; py++)
                for (var px = 0; px < W; px++)
                {
                    float sr = 0, sg = 0, sb = 0, sa = 0;
                    for (var j = 0; j < SS; j++)
                        for (var k = 0; k < SS; k++)
                        {
                            var i = (py * SS + j) * W * SS + px * SS + k;
                            sr += r[i] * a[i]; sg += g[i] * a[i]; sb += b[i] * a[i]; sa += a[i];
                        }
                    var o = ((oy + py) * stride + ox + px) * 4;
                    if (sa == 0) { rgba[o + 3] = 0; continue; }
                    var cov = sa / (SS * SS);
                    var fr = sr / sa * cov + bg.R * (1 - cov);
                    var fg = sg / sa * cov + bg.G * (1 - cov);
                    var fb = sb / sa * cov + bg.B * (1 - cov);
                    rgba[o] = (byte)Math.Round(fr); rgba[o + 1] = (byte)Math.Round(fg); rgba[o + 2] = (byte)Math.Round(fb); rgba[o + 3] = 255;
                }
        }
    }

    // ------------------------------------------------------------ shapes

    static bool InRRect(double x, double y, double x0, double y0, double x1, double y1, double rad)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        var cx = Math.Clamp(x, x0 + rad, x1 - rad); var cy = Math.Clamp(y, y0 + rad, y1 - rad);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad;
    }

    static void RRect(Painter p, double x0, double y0, double x1, double y1, double rad, Rgb c)
        => p.Fill((x, y) => InRRect(x, y, x0, y0, x1, y1, rad), c, x0, y0, x1, y1);

    static void Circle(Painter p, double cx, double cy, double rad, Rgb c)
        => p.Fill((x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad, c, cx - rad, cy - rad, cx + rad, cy + rad);

    static bool InPolygon(double x, double y, (double X, double Y)[] pts)
    {
        var inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X) inside = !inside;
        return inside;
    }

    static void Polygon(Painter p, (double X, double Y)[] pts, Rgb c)
        => p.Fill((x, y) => InPolygon(x, y, pts), c, pts.Min(q => q.X), pts.Min(q => q.Y), pts.Max(q => q.X), pts.Max(q => q.Y));

    static double SegDist(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax; var dy = by - ay; var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        var qx = ax + t * dx - px; var qy = ay + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>A thick polyline (round ends).</summary>
    static void Stroke(Painter p, IReadOnlyList<(double X, double Y)> pts, double width, Rgb c)
    {
        var h = width / 2;
        p.Fill((x, y) =>
        {
            for (var i = 0; i + 1 < pts.Count; i++)
                if (SegDist(x, y, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y) <= h) return true;
            return false;
        }, c, pts.Min(q => q.X) - h, pts.Min(q => q.Y) - h, pts.Max(q => q.X) + h, pts.Max(q => q.Y) + h);
    }

    // ------------------------------------------------------------ suits (centre cx,cy; half width w)

    static void Heart(Painter p, double cx, double cy, double w, bool flip, Rgb c)
    {
        var s = w / 1.15;
        p.Fill((x, y) =>
        {
            var u = (x - cx) / s; var v = -(y - cy) / s + 0.1;
            if (flip) v = -v + 0.2;
            var t = u * u + v * v - 1;
            return t * t * t - u * u * v * v * v <= 0;
        }, c, cx - w - 1, cy - w * 1.2, cx + w + 1, cy + w * 1.2);
    }

    static void Diamond(Painter p, double cx, double cy, double w, Rgb c)
        => Polygon(p, new[] { (cx, cy - w * 1.3), (cx + w * 0.85, cy), (cx, cy + w * 1.3), (cx - w * 0.85, cy) }, c);

    static void Spade(Painter p, double cx, double cy, double w, bool flip, Rgb c)
    {
        var s = w / 1.15;
        p.Fill((x, y) =>
        {
            var u = (x - cx) / s; var v = (y - cy) / s - 0.05;       // the heart upside down
            if (flip) v = -v;
            var t = u * u + v * v - 1;
            if (t * t * t - u * u * v * v * v <= 0) return true;
            // stem: a slim triangle under the body
            var sy = flip ? -(y - cy) / s : (y - cy) / s;
            return sy > 0.55 && sy < 1.45 && Math.Abs(u) < 0.55 * (1.45 - sy);
        }, c, cx - w - 1, cy - w * 1.4, cx + w + 1, cy + w * 1.4);
    }

    static void Club(Painter p, double cx, double cy, double w, bool flip, Rgb c)
    {
        var d = flip ? -1 : 1;
        var r = w * 0.52;
        Circle(p, cx, cy - d * w * 0.5, r, c);
        Circle(p, cx - w * 0.58, cy + d * w * 0.32, r, c);
        Circle(p, cx + w * 0.58, cy + d * w * 0.32, r, c);
        var ty = cy + d * w * 0.15; var by = cy + d * w * 1.2;
        Polygon(p, new[] { (cx - w * 0.12, ty), (cx + w * 0.12, ty), (cx + w * 0.4, by), (cx - w * 0.4, by) }, c);
    }

    static void Suit(Painter p, int suit, double cx, double cy, double w, bool flip)
    {
        var ink = suit is 1 or 2 ? Red : Black;
        switch (suit)
        {
            case 0: Spade(p, cx, cy, w, flip, ink); break;
            case 1: Heart(p, cx, cy, w, flip, ink); break;
            case 2: Diamond(p, cx, cy, w, ink); break;
            default: Club(p, cx, cy, w * 0.86, flip, ink); break;
        }
    }

    // ------------------------------------------------------------ stroke glyphs for the ranks, in a 0..1 box

    static List<(double X, double Y)> Arc(double cx, double cy, double rx, double ry, double a0, double a1)
    {
        var pts = new List<(double, double)>();
        var n = Math.Max(8, (int)(Math.Abs(a1 - a0) / 8));
        for (var i = 0; i <= n; i++)
        {
            var a = (a0 + (a1 - a0) * i / n) * Math.PI / 180;
            pts.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return pts;
    }

    static List<(double X, double Y)> Line(params (double, double)[] pts) => pts.ToList();

    static List<List<(double X, double Y)>> Glyph(char ch)
    {
        var l = new List<List<(double X, double Y)>>();
        void Add(List<(double X, double Y)> s) => l.Add(s);
        switch (ch)
        {
            case 'A': Add(Line((0.05, 1), (0.5, 0), (0.95, 1))); Add(Line((0.22, 0.66), (0.78, 0.66))); break;
            case '1': Add(Line((0.2, 0.22), (0.52, 0), (0.52, 1))); Add(Line((0.2, 1), (0.84, 1))); break;
            case '2': { var s = Arc(0.5, 0.27, 0.4, 0.27, 180, 400); s.Add((0.08, 1)); s.Add((0.94, 1)); Add(s); break; }
            case '3': Add(Arc(0.48, 0.25, 0.36, 0.23, 200, 450)); Add(Arc(0.48, 0.73, 0.42, 0.27, 270, 520)); break;
            case '4': Add(Line((0.72, 1), (0.72, 0), (0.04, 0.7), (0.96, 0.7))); break;
            case '5': { var s = Line((0.86, 0), (0.22, 0), (0.17, 0.44)); s.AddRange(Arc(0.48, 0.7, 0.42, 0.3, 235, 510)); Add(s); break; }
            case '6': { Add(Arc(0.5, 0.69, 0.4, 0.31, 0, 360)); Add(Line((0.86, 0.04), (0.55, 0), (0.25, 0.16), (0.1, 0.5), (0.1, 0.69))); break; }
            case '7': Add(Line((0.06, 0), (0.94, 0), (0.38, 1))); break;
            case '8': Add(Arc(0.5, 0.25, 0.31, 0.23, 0, 360)); Add(Arc(0.5, 0.73, 0.4, 0.27, 0, 360)); break;
            case '9': Add(Arc(0.5, 0.31, 0.4, 0.31, 0, 360)); Add(Line((0.9, 0.31), (0.9, 0.5), (0.75, 0.84), (0.45, 1), (0.14, 0.96))); break;
            case '0': Add(Arc(0.5, 0.5, 0.4, 0.5, 0, 360)); break;
            case 'J': { var s = Line((0.8, 0), (0.8, 0.68)); s.AddRange(Arc(0.45, 0.68, 0.35, 0.32, 0, 180)); s.Add((0.1, 0.52)); Add(s); break; }
            case 'Q': Add(Arc(0.5, 0.5, 0.42, 0.5, 0, 360)); Add(Line((0.58, 0.68), (0.96, 1))); break;
            case 'K': Add(Line((0.12, 0), (0.12, 1))); Add(Line((0.9, 0), (0.12, 0.58))); Add(Line((0.36, 0.4), (0.92, 1))); break;
        }
        return l;
    }

    /// <summary>Draws a rank glyph into the box (x, y, w, h) with stroke <paramref name="stroke"/>.</summary>
    static void GlyphAt(Painter p, char ch, double x, double y, double w, double h, double stroke, Rgb c)
    {
        foreach (var s in Glyph(ch))
            Stroke(p, s.Select(q => (x + q.X * w, y + q.Y * h)).ToList(), stroke, c);
    }

    static void Rank(Painter p, string rank, double x, double y, double h, double stroke, Rgb c)
    {
        var w = h * 0.62;
        for (var i = 0; i < rank.Length; i++)
            GlyphAt(p, rank[i], x + i * (w + h * 0.12), y, w, h, stroke, c);
    }

    // ------------------------------------------------------------ cards

    static readonly string[] RankNames = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    // Pip positions in the 64 x 80 cell for ranks 2..10 (x column 0..2, y in pixels).
    static (double X, double Y)[] Pips(int rank)
    {
        double L = 25, Cx = 32, R = 39, T = 19, B = 61, M = 40;
        return rank switch
        {
            1 => new[] { (Cx, M) },
            2 => new[] { (Cx, T), (Cx, B) },
            3 => new[] { (Cx, T), (Cx, M), (Cx, B) },
            4 => new[] { (L, T), (R, T), (L, B), (R, B) },
            5 => new[] { (L, T), (R, T), (Cx, M), (L, B), (R, B) },
            6 => new[] { (L, T), (R, T), (L, M), (R, M), (L, B), (R, B) },
            7 => new[] { (L, T), (R, T), (Cx, 29.5), (L, M), (R, M), (L, B), (R, B) },
            8 => new[] { (L, T), (R, T), (Cx, 29.5), (L, M), (R, M), (Cx, 50.5), (L, B), (R, B) },
            9 => new[] { (L, T), (R, T), (L, 33), (R, 33), (Cx, M), (L, 47), (R, 47), (L, B), (R, B) },
            _ => new[] { (L, T), (R, T), (Cx, 29.5), (L, 33), (R, 33), (L, 47), (R, 47), (Cx, 50.5), (L, B), (R, B) },
        };
    }

    static void Body(Painter p, Rgb fill)
    {
        RRect(p, 0, 0, CellW, CellH, 6, Grey);
        RRect(p, 1.1, 1.1, CellW - 1.1, CellH - 1.1, 5, fill);
    }

    static void Face(Painter p, int suit, int rank)
    {
        var ink = suit is 1 or 2 ? Red : Black;
        Body(p, White);
        // Corner index, top left and (rotated) bottom right.
        for (var pass = 0; pass < 2; pass++)
        {
            p.Rot = pass == 1;
            Rank(p, RankNames[rank], rank == 9 ? 3 : 6, 6, rank == 9 ? 12 : 14, 2.0, ink);
            Suit(p, suit, 9.5, 27, 4.2, false);
        }
        p.Rot = false;
        var pc = rank + 1;
        if (pc is >= 11)
        {
            // Court cards: a frame, a big letter and the suit above and below it.
            RRect(p, 15, 14, 49, 66, 4, ink);
            RRect(p, 16.4, 15.4, 47.6, 64.6, 3, White);
            GlyphAt(p, RankNames[rank][0], 22, 26, 20, 28, 4.2, ink);
            Suit(p, suit, 32, 21, 4.4, false);
            Suit(p, suit, 32, 59, 4.4, true);
            return;
        }
        var pips = Pips(pc);
        foreach (var (x, y) in pips)
        {
            var size = pc == 1 ? 14.0 : 5.0;
            Suit(p, suit, x, y, size, y > 40.5);
        }
    }

    static void Back(Painter p)
    {
        Body(p, White);
        RRect(p, 4, 4, CellW - 4, CellH - 4, 3, Blue);
        RRect(p, 5.2, 5.2, CellW - 5.2, CellH - 5.2, 2.2, LightBlue);
        for (var k = -CellH; k < CellW + CellH; k += 8)
        {
            var kk = k;
            p.Fill((x, y) => x > 5.2 && x < CellW - 5.2 && y > 5.2 && y < CellH - 5.2 && (Math.Abs(((x - y - kk) % 8 + 8) % 8) < 1.1 || Math.Abs(((x + y - kk) % 8 + 8) % 8) < 1.1), Blue, 4, 4, CellW - 4, CellH - 4);
        }
    }

    static void Arrow(Painter p, bool up)
    {
        var pts = new (double X, double Y)[] { (32, 20), (49, 40), (38, 40), (38, 59), (26, 59), (26, 40), (15, 40) };
        if (!up) pts = pts.Select(q => (q.X, 80 - q.Y)).ToArray();
        Polygon(p, pts, White);
    }

    static void Star(Painter p)
    {
        var pts = new List<(double, double)>();
        for (var i = 0; i < 10; i++)
        {
            var a = (-90 + i * 36) * Math.PI / 180; var rad = i % 2 == 0 ? 20 : 8.5;
            pts.Add((32 + rad * Math.Cos(a), 41 + rad * Math.Sin(a)));
        }
        Polygon(p, pts.ToArray(), Gold);
    }

    static void Cross(Painter p)
    {
        Stroke(p, Line((18, 27), (46, 55)), 7, Red);
        Stroke(p, Line((46, 27), (18, 55)), 7, Red);
    }

    // ------------------------------------------------------------ font: the 5x7 glyphs, smoothed

    static byte[] Glyphs5x7 = Array.Empty<byte>();

    static void LoadGlyphs(string fontSource)
    {
        var text = File.ReadAllText(fontSource);
        var start = text.IndexOf("Glyphs =", StringComparison.Ordinal);
        var open = text.IndexOf('{', start);
        var close = text.IndexOf("};", open, StringComparison.Ordinal);
        Glyphs5x7 = text.Substring(open + 1, close - open - 1)
            .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Convert.ToByte(t, 16)).ToArray();
    }

    static bool Src(int code, int x, int y)
        => x >= 0 && x < 5 && y >= 0 && y < 7 && (Glyphs5x7[(code - 32) * 7 + y] & (0x10 >> x)) != 0;

    /// <summary>One glyph, each source pixel k x k pixels, edges smoothed (blur then threshold) and anti-aliased.</summary>
    static void FontGlyph(byte[] rgba, int stride, int ox, int oy, int cellW, int cellH, int code, int k, Rgb ink, Rgb bg)
    {
        const int O = 8;                                 // sub-samples per output pixel
        var w = cellW * O; var h = cellH * O;
        var density = new float[w * h];
        var pad = k / 2;                                 // margin inside the cell
        var scale = (double)k * O;                       // sub-samples per source pixel
        for (var sy = 0; sy < h; sy++)
            for (var sx = 0; sx < w; sx++)
            {
                var fx = (sx - pad * O) / scale - 0.5; var fy = (sy - pad * O) / scale - 0.5;
                // smooth field: bilinear blend of the four source pixels around the point, then thresholded below
                var x0 = (int)Math.Floor(fx); var y0 = (int)Math.Floor(fy);
                var tx = fx - x0; var ty = fy - y0;
                double v(int xx, int yy) => Src(code, xx, yy) ? 1 : 0;
                var d = v(x0, y0) * (1 - tx) * (1 - ty) + v(x0 + 1, y0) * tx * (1 - ty) + v(x0, y0 + 1) * (1 - tx) * ty + v(x0 + 1, y0 + 1) * tx * ty;
                density[sy * w + sx] = (float)d;
            }
        for (var py = 0; py < cellH; py++)
            for (var px = 0; px < cellW; px++)
            {
                var on = 0;
                for (var j = 0; j < O; j++)
                    for (var i = 0; i < O; i++)
                        if (density[(py * O + j) * w + px * O + i] >= 0.5f) on++;
                var o = ((oy + py) * stride + ox + px) * 4;
                if (on == 0) { rgba[o + 3] = 0; continue; }
                var cov = on / (float)(O * O);
                rgba[o] = (byte)Math.Round(ink.R * cov + bg.R * (1 - cov));
                rgba[o + 1] = (byte)Math.Round(ink.G * cov + bg.G * (1 - cov));
                rgba[o + 2] = (byte)Math.Round(ink.B * cov + bg.B * (1 - cov));
                rgba[o + 3] = 255;
            }
    }

    // ------------------------------------------------------------ entry point

    public static void Run(string outPng, string fontSource)
    {
        LoadGlyphs(fontSource);
        var w = CellW * Cols;
        var h = CellH * Rows + 16 + 16 + 64;
        var rgba = new byte[w * h * 4];

        for (var suit = 0; suit < 4; suit++)
            for (var rank = 0; rank < 13; rank++)
            {
                var p = new Painter(CellW, CellH);
                Face(p, suit, rank);
                p.Resolve(Table, rgba, w, rank * CellW, suit * CellH);
            }

        var row = 4 * CellH;
        var back = new Painter(CellW, CellH); Back(back); back.Resolve(Table, rgba, w, 0, row);
        var up = new Painter(CellW, CellH); Arrow(up, true); up.Resolve(Button, rgba, w, CellW, row);
        var down = new Painter(CellW, CellH); Arrow(down, false); down.Resolve(Button, rgba, w, 2 * CellW, row);
        var star = new Painter(CellW, CellH); Star(star); star.Resolve(Table, rgba, w, 3 * CellW, row);
        var cross = new Painter(CellW, CellH); Cross(cross); cross.Resolve(Table, rgba, w, 4 * CellW, row);

        var fy = CellH * Rows;
        for (var i = 0; i < 64; i++)
        {
            FontGlyph(rgba, w, i * 12, fy, 12, 16, 32 + i, 2, Ink, Table);
            FontGlyph(rgba, w, i * 12, fy + 16, 12, 16, 32 + i, 2, Gold, Table);
            FontGlyph(rgba, w, (i % 32) * 24, fy + 32 + (i / 32) * 32, 24, 32, 32 + i, 4, Ink, Button);
        }

        File.WriteAllBytes(outPng, Png.Write(new RgbaImage(w, h, rgba)));
        Console.WriteLine($"{outPng}: {w}x{h}");
    }
}
