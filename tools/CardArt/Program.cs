using ESharp.Assets;

// --hd <out.png> <Font.cs>: the 64x80 high resolution sheet (see HdArt.cs).
if (args.Length == 3 && args[0] == "--hd") { HdArt.Run(args[1], args[2]); return; }

// Draws the sprite sheet used by samples/cards (original artwork, MIT like the rest of the repository).
// Usage: dotnet run --project tools/CardArt -- samples/cards/cards.png
//
// Sheet: 14 columns x 5 rows of 32x40 cells. Rows 0..3 are suits (spades, hearts, diamonds, clubs), columns 0..12
// are ranks A..K. Row 4: card back, up arrow, down arrow, star, cross.
const int CellW = 32, CellH = 40, Cols = 14, Rows = 5;
var w = CellW * Cols;
var h = CellH * Rows;
var rgba = new byte[w * h * 4]; // all transparent

void Put(int x, int y, (byte r, byte g, byte b) c)
{
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    var o = (y * w + x) * 4;
    rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = 255;
}

var white = ((byte)255, (byte)255, (byte)255);
var black = ((byte)0, (byte)0, (byte)0);
var red = ((byte)221, (byte)17, (byte)17);
var grey = ((byte)136, (byte)136, (byte)136);
var blue = ((byte)34, (byte)51, (byte)204);
var lightBlue = ((byte)102, (byte)136, (byte)255);
var gold = ((byte)255, (byte)204, (byte)0);
var green = ((byte)34, (byte)170, (byte)51);

// Rounded card body: white fill, grey border, corners cut away (stay transparent).
void Body(int cx, int cy, (byte, byte, byte) fill)
{
    for (var y = 0; y < CellH; y++)
        for (var x = 0; x < CellW; x++)
        {
            var edgeX = Math.Min(x, CellW - 1 - x);
            var edgeY = Math.Min(y, CellH - 1 - y);
            if (edgeX < 2 && edgeY < 2 && edgeX + edgeY < 2) continue; // 3-pixel cut corner
            var border = edgeX == 0 || edgeY == 0 || (edgeX + edgeY == 2 && edgeX < 2 && edgeY < 2);
            Put(cx + x, cy + y, border ? grey : fill);
        }
}

var glyphs = new Dictionary<char, string[]>
{
    ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
    ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
    ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
    ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
    ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
    ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
    ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
    ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
    ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
    ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
    ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
    ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
    ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
    ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
};
var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

void Text(int x, int y, string s, int scale, (byte, byte, byte) c)
{
    foreach (var ch in s)
    {
        var g = glyphs[ch];
        for (var gy = 0; gy < 7; gy++)
            for (var gx = 0; gx < 5; gx++)
                if (g[gy][gx] == '#')
                    for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                            Put(x + gx * scale + sx, y + gy * scale + sy, c);
        x += 6 * scale;
    }
}

// 9x9 suit shapes.
var suits = new[]
{
    new[] { "....#....", "...###...", "..#####..", ".#######.", "#########", "#########", ".##.#.##.", "....#....", "...###..." }, // spade
    new[] { ".##...##.", "####.####", "#########", "#########", "#########", ".#######.", "..#####..", "...###...", "....#...." }, // heart
    new[] { "....#....", "...###...", "..#####..", ".#######.", "#########", ".#######.", "..#####..", "...###...", "....#...." }, // diamond
    new[] { "...###...", "..#####..", "...###...", ".##.#.##.", "#########", "#########", ".##.#.##.", "....#....", "...###..." }, // club
};

void Shape(int x, int y, string[] shape, int scale, (byte, byte, byte) c)
{
    for (var gy = 0; gy < shape.Length; gy++)
        for (var gx = 0; gx < shape[gy].Length; gx++)
            if (shape[gy][gx] == '#')
                for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        Put(x + gx * scale + sx, y + gy * scale + sy, c);
}

for (var suit = 0; suit < 4; suit++)
{
    var ink = suit is 1 or 2 ? red : black;
    for (var rank = 0; rank < 13; rank++)
    {
        var cx = rank * CellW;
        var cy = suit * CellH;
        Body(cx, cy, white);
        Text(cx + 3, cy + 4, ranks[rank], 1, ink);
        Shape(cx + 7, cy + 17, suits[suit], 2, ink);                    // big suit, centre
        Shape(cx + CellW - 12, cy + 4, suits[suit], 1, ink);            // small suit, top right
    }
}

// Row 4.
{
    var cy = 4 * CellH;
    Body(0, cy, lightBlue);
    for (var y = 3; y < CellH - 3; y++)
        for (var x = 3; x < CellW - 3; x++)
            if (((x + y) & 3) == 0 || ((x - y) & 3) == 0) Put(x, cy + y, blue);

    // Arrows are 13x13 shapes on a green button-sized cell (cells 2 and 3 of row 4).
    string[] up = { "......#......", ".....###.....", "....#####....", "...#######...", "..#########..", ".###########.", "..#########..", ".....###.....", ".....###.....", ".....###.....", ".....###.....", ".....###.....", ".....###....." };
    var down = up.Reverse().ToArray();
    Shape(CellW + 9, cy + 13, up, 1, white);
    Shape(2 * CellW + 9, cy + 13, down, 1, white);

    string[] star = { "......#......", "......#......", ".....###.....", ".....###.....", "#############", ".###########.", "..#########..", "...#######...", "...#######...", "..####.####..", "..###...###..", ".##.......##.", ".#.........#." };
    Shape(3 * CellW + 9, cy + 13, star, 1, gold);

    string[] cross = { "##.........##", "###.......###", ".###.....###.", "..###...###..", "...#######...", "....#####....", ".....###.....", "....#####....", "...#######...", "..###...###..", ".###.....###.", "###.......###", "##.........##" };
    Shape(4 * CellW + 9, cy + 13, cross, 1, red);
    _ = green;
}

File.WriteAllBytes(args[0], Png.Write(new RgbaImage(w, h, rgba)));
Console.WriteLine($"{args[0]}: {w}x{h}");
