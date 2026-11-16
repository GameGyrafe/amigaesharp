# Design: display modes, scaling, window mode, colours and gradients

Status: **design only, for the maintainer's approval.** Nothing in this document is implemented or verified yet. Numbers are
marked **measured** (with where they come from) or **estimate**. When parts of this are built, `docs/STATUS.md` records
what was actually run.

## 1. Why

Games written with E# today open one fixed-size screen (typically 320 x 256 lores) and draw in screen pixels. That has four
problems:

1. On RTG systems, AmigaOS 4, MorphOS and AROS the game looks small (OS4 centres it in a 640 x 480 or larger mode) or
   blocky when pixels are doubled (`Video.BlitMaskedScaled`). It should draw sharper art at the higher resolution.
2. AGA can show 256 colours, and the copper can change a colour on every line. Games use 32 or 64 colours and flat
   backgrounds.
3. A slow game (cards, puzzles, board games) should be able to run in a window on the Workbench, not only on its own screen.
4. Display choice is hard-wired. An A4000 with a 31 kHz monitor, or an A1200 with a flicker fixer, cannot pick a better
   mode; a stock A1200 on a 15 kHz TV or 1084 must not get a mode its monitor cannot show.

For a slow game the limit is the monitor, not drawing speed: double buffering already hides the redraw. So the design aims
at picking a display the player's monitor can show, drawing art made for that resolution, and keeping 320 x 256 as the
safe fallback.

## 2. Today (what exists)

| Piece | What it does | Limits |
|---|---|---|
| `Video.Open(w, h, depth)` | A custom screen plus a borderless backdrop window. Mode from the size on m68k: >400 wide is hires, >800 super-hires, >300 lines interlaced. Depth 6-8 needs AGA, else an 8-bit RTG screen if a card has one. OS4: `BestModeIDA` and the window centred in the nearest mode. | No monitor choice (always the default monitor, so PAL or NTSC). No DblPAL, Productivity, Multiscan. Coordinates are screen pixels. |
| `Video.OpenRtg` | 8-bit CLUT RTG screen via `cybergraphics.library` `BestCModeIDTagList`. | 8-bit only. Planar blits converted by the driver (not measured). |
| Drawing | `FillRect`, `Blit`, `BlitRegion`, `BlitMasked` (blitter, planar art in chip RAM), `BlitMaskedScaled` (pixel replication through `FillRect` runs), `Font` (5x7, `BltTemplate`), `Fonts` (disk fonts). | All in device pixels. No scale or origin. |
| Colours | `SetColor` (12-bit), `SetColor24` / `SetPalette24` (24-bit when depth > 5). | Every image has its own palette; no shared palette across images; no pen reservation. |
| Double buffer | `EnableDoubleBuffer` (Swap: two screen bitmaps via `AllocScreenBuffer` / `ChangeScreenBuffer`) and `EnableBackBuffer` (Copy: a friend bitmap blitted to the window at `Flip`). | Screen only. |
| Input | `Input.Poll` reads the backdrop window's IDCMP (buttons, moves, raw keys). Coordinates are window pixels. | No close, resize or refresh events. |
| `Asl` | File requester only. | No screen-mode requester. |
| `GtWindow` | GadTools window on the Workbench. | Not a drawing surface. |
| `esharp assets` | PNG / ILBM to planar data, per-image palette (OCS 12-bit or AGA 24-bit), mask, dithering. Data is `[ChipRam]` arrays in the executable. | One scale. Everything loads into chip RAM at start. |
| sdl | 8-bit chunky framebuffer and palette in C, shown in a window with integer scale (`ESHARP_SCALE`). | Fixed logical size. |

Measured so far (from `docs/STATUS.md` and a game port's own notes, emulated A1200 / Kickstart 3.1 in FS-UAE, chip RAM only):

- 50 whole-screen frames at 320 x 256 x 3 planes: 2010-2012 ms with Swap, 3010-3015 ms with Copy (samples/dbuffer).
- A full redraw of a card table at 320 x 256 x 6 planes: about 340-360 ms, dominated by many small drawing calls, not by
  the copy at `Flip`.

## 3. The model: logical canvas, device pixels, scale

The game draws in **logical coordinates**. The display has **device pixels**. Between them is an integer **scale** per axis
and an **origin**:

```
device_x = origin_x + logical_x * scale_x
device_y = origin_y + logical_y * scale_y
```

Rules:

- Scales are integers (1, 2, 3, 4). Pixel art does not survive fractional scaling, and integer scales keep the blitter path.
- `scale_x` and `scale_y` may differ. A PAL hires screen (640 x 256) has pixels about half as wide as they are tall, so it
  is `scale_x = 2, scale_y = 1`. The pixel aspect comes from the mode's DisplayInfo resolution (ticks per pixel), compared
  with lores PAL. RTG, OS4, MorphOS, AROS and sdl modes have square pixels.
- The game states a **minimum logical size** (for example 320 x 256). The display may be larger in logical units (a
  1024 x 768 screen at scale 2 is 512 x 384 logical). The game then either uses the room (a flexible layout) or asks for its
  layout to be centred (`origin` moves; the border is filled with a background pen).
- **Art scale** is what the game ships art for (1x, 2x). The display scale is chosen so that the logical size is at least
  the minimum and the scale is the highest one the game has art for, then larger if the device is much bigger (art is then
  replicated from the nearest lower set, once, at load time).

### What changes in drawing calls

Every `Video` drawing call takes logical coordinates. `Video` multiplies, adds the origin, and clips. With scale 1 and origin
0 (every program today) nothing changes: `Video.Open` keeps its current meaning and is a scale-1 display.

Blits take an **image set** instead of raw planar spans when the program wants scaling: the set has one variant per art scale,
and `Video` picks the variant for the current scale. A source rectangle (a sprite in a sheet) is given in logical units and
multiplied too. The raw-span calls stay for programs that do their own thing.

Text: `Font` and the bitmap fonts get the scale as well. A game should ship a font per art scale (a 2x font drawn for 2x, not a
doubled 5x7), see section 9.

### Input

Mouse positions arrive in window pixels. `Input` converts them back:

```
logical_x = (window_x - border_left - origin_x) / scale_x     (floor; outside the canvas gives -1)
```

`Input.MouseX/Y`, `PressX/Y` become logical. New `Input.DeviceX/Y` give the raw position. On scale 1 / origin 0 they are
equal, so existing programs see no change. Tests: a round trip for every scale and origin, and a click on the border.

### How the game learns the scale

`Display.ScaleX`, `ScaleY`, `Width`, `Height` (logical), `DeviceWidth`, `DeviceHeight`, `ArtScale` (the image variant in use),
and `Display.Changed`, which is set after a resize or a mode change and tells the game to lay out and redraw everything.

## 4. Choosing the mode

### Default: follow the Workbench

1. `LockPubScreen(NULL)`, read the Workbench mode with `GetVPModeID`, unlock.
2. The mode's monitor part (mode ID AND the monitor mask) names the family: PAL, NTSC, DblPAL, DblNTSC, Multiscan,
   Productivity, Euro72, Super72, or an RTG board (`IsCyberModeID` true, or the DisplayInfo property flag for foreign modes).
   A player whose Workbench is on a 15 kHz family has a 15 kHz monitor; one on a 31 kHz family has a VGA-class monitor; one
   on an RTG board uses the card.
3. Ask `BestModeIDA` for the wanted device size and depth **on that monitor** (BIDTAG monitor ID, nominal width and height,
   depth, and must-not-have flags for HAM, EHB, dual playfield and, by default, interlace).
4. Check the result with `GetDisplayInfoData`: DTAG_DIMS (nominal size, maximum depth), DTAG_DISP (property flags,
   resolution for the pixel aspect), DTAG_NAME (the mode's name, for messages and the settings screen).
5. If anything fails, use 320 x 256 (PAL lores; NTSC 320 x 200 as the system gives) at the highest depth the chipset allows.

The wanted device size is `minimum logical size x art scale`, so a game with 2x art and a 320 x 256 minimum wants 640 x 512.

### Overrides

- **MODE option** on the command line (and later the icon's tool types): `MODE=0x50031004` (a mode ID) or
  `MODE="DBLPAL:HighRes No Flicker"` (a mode name from the display database, matched case-insensitively by walking
  `NextDisplayInfo` and reading each DTAG_NAME), `MODE=WINDOW`, `MODE=SAFE` (320 x 256). `DEPTH=n` caps the depth.
- **In-game "Screen mode" setting**: opens the asl screen-mode requester (asl request type for screen modes, v38) with the
  current mode as the initial one, minimum width and height from the game's minimum logical size, depth fixed by the game,
  and property flags that hide HAM, EHB and dual playfield. The result is a mode ID, size and depth. A filter hook is not
  needed (E# has no hooks yet). The requester appears on the game's screen; double buffering is turned off around it, as
  `Asl.PickFile` does now.
- **Remembered choice**: the chosen mode ID and window/screen choice are saved in `ENVARC:<program>/display` and copied to
  `ENV:` (plain text, one line). The next start uses it if the mode still exists in the display database, else the default.

### Defaults per machine class

| Machine / Workbench | Default display | Scale | Depth | Notes |
|---|---|---|---|---|
| Stock A1200, Workbench on PAL or NTSC (15 kHz TV / 1084) | PAL lores 320 x 256 (NTSC 320 x 200) | 1 x 1 | 8 | Never interlaced by default: flicker. 640 x 256 hires (2 x 1) and 640 x 512 laced (2 x 2) only by MODE or the requester. |
| A1200 or A4000 with a 31 kHz monitor (Workbench on DblPAL, DblNTSC, Productivity, Multiscan, Euro72) | That monitor at 640 x 512 or 640 x 480 | 2 x 2 | 8 if DTAG_DIMS max depth allows, else the maximum | No flicker at 31 kHz. A 640 x 480 mode with a 256-line minimum gives logical 320 x 240: either the game accepts 240 or it falls back to scale 1 centred (open question Q3). |
| AGA super-hires (1280 wide) | Only by choice | 4 x 2 or 4 x 4 | max allowed | Chip RAM: see section 5. |
| RTG card, Workbench on the card (Picasso96, CyberGraphX; A4000 Zorro, accelerated A1200) | Card mode nearest to 640 x 512, 8-bit CLUT first | 2 x 2 | 8 | The planar blit path works unchanged on CLUT. 16/24-bit and the chunky canvas are phase 6. |
| AmigaOS 4 | Nearest mode >= wanted size (the QEMU sm501 has 640 x 480, 800 x 600, 1024 x 768, ...) | 2 x 2 | 8 (CLUT) | 640 x 480 has the same 240-line question; 800 x 600 at 2x is logical 400 x 300. |
| MorphOS | As RTG | 2 x 2 | 8 | Not built to run yet. |
| AROS (hosted or native) | As RTG; hosted AROS screens are X11 windows | 2 x 2 | 8 | |
| sdl | A desktop window of minimum logical size x scale, resizable | from the window size | 8 (chunky) | `ESHARP_SCALE` stays as an override. |
| Host (dotnet) | A fake display; tests set device size, aspect and family | any | any | |

Hires and the 15 kHz monitor: a 640 x 256 hires mode is fine on a TV or 1084 and gives finer horizontal detail (text) without
flicker. It needs 2 x 1 art or replicated 1x art. It is offered, not the default.

## 5. 256 colours on AGA: memory and speed

Screen memory (chip RAM) per bitmap, `width x height x depth / 8`:

| Mode | 6 planes | 8 planes | two buffers at 8 planes |
|---|---|---|---|
| 320 x 256 lores | 60 KB | 80 KB | 160 KB |
| 640 x 256 hires | 120 KB | 160 KB | 320 KB |
| 640 x 480 Productivity | 225 KB | 300 KB | 600 KB |
| 640 x 512 laced or DblPAL | 240 KB | 320 KB | 640 KB |
| 1280 x 512 super-hires laced | 480 KB | 640 KB | 1280 KB |

A stock A1200 has 2 MB of chip RAM, shared with the Workbench screen, the OS and every chip-RAM asset. Two 640 x 512 x 8
buffers plus 2x art (section 9) is about 0.8-1 MB (**estimate**). Super-hires at 8 planes with double buffering is not
practical on 2 MB and is offered without double buffering or not at all (open question Q5).

Speed (**estimates**, to be measured in phase 1):

- Blitter work is proportional to the number of planes: 8 planes is a third more than 6 for the same area.
- The redraw measured above (340-360 ms at 6 planes) is mostly per-call overhead, so 8 planes should add well under a third.
  **Estimate**: 380-450 ms on a stock A1200.
- 640 x 512 is four times the area. Large fills and the Copy-mode `Flip` grow about four times; small calls do not.
  **Estimate**: 0.8-1.5 s for a full table redraw on a stock A1200, hidden by double buffering. Acceptable for a slow game,
  not for an action game.
- Display DMA takes chip bus cycles from the blitter and the CPU. More planes, hires and 31 kHz modes take more. On a stock
  A1200 everything (code, data, screen) is in chip RAM, so the CPU slows down too; fast RAM (most A1200 accelerators, every
  A4000) removes most of that for the CPU. Not measured; phase 1 measures fill and blit rates per mode on FS-UAE with
  cycle-exact settings (FS-UAE numbers are not real hardware numbers and are labelled so).
- graphics.library knows which depths a mode can fetch. We read the maximum depth from DTAG_DIMS and never hard-code it.

## 6. Gradients

### One idea on every target: a gradient pen

A game names one pen as a **gradient pen** and gives a top and bottom colour (or a short list of stops) over a range of
logical rows. Anything drawn in that pen shows the gradient for its row. The game clears its background with an ordinary
`FillRect(..., pen)`. No game code changes between targets.

How each target does it:

| Target | Method | Quality |
|---|---|---|
| AGA screen (own screen) | **Copper**: a user copper list on the screen's view port changes the pen's colour at the start of each line (or each group of lines). | 24-bit if the spike works (below), else 12-bit. |
| ECS/other native screen | Not supported by the minimum system; same code as AGA in 12-bit. | |
| sdl, host, chunky canvas (phase 6) | **Per-row palette**: when the 8-bit frame is turned into RGB to present, the gradient pen's colour is looked up per row. | Full 24-bit. |
| RTG 8-bit CLUT screen, palette window on the Workbench | **Bands**: Video reserves N extra pens (default 16) with shades, and a fill in the gradient pen is drawn as bands, with an ordered 2-colour pattern between neighbouring bands (RastPort area pattern with A and B pens in JAM2, which `RectFill` honours). | Good, uses pens. |
| RTG 16/24-bit screen or window (phase 6) | Per-row palette in the chunky canvas present. | Full. |
| Anything else / out of pens | **Flat**: the middle colour. | |

`Display.GradientKind` says which one is in use (tests check it per target).

### Copper on AGA: the details

- Build a `UCopList` with `UCopperListInit`, then per step `CWait(line, 0)`, `CMove(colour register, value)`, `CBump`, end with
  a wait past the last line. Attach it to the screen's view port (`UCopIns`) and remake the display (`RethinkDisplay`, or
  `MakeScreen` plus `RethinkDisplay`). `CloseScreen` frees it (`FreeVPortCopLists` for an early remove).
- **12-bit**: a colour register write with the LOCT bit clear sets the high nibbles and copies them into the low nibbles.
  One move per line, no other register touched, but only 16 levels per channel, so a dark, subtle gradient shows bands.
  The pen must be in colour bank 0 (pens 0-31) unless the bank is set too.
- **24-bit**: per change, set BPLCON3 (bank and LOCT clear), write the high nibbles, set BPLCON3 with LOCT, write the low
  nibbles, restore BPLCON3. BPLCON3 also holds bits the system sets (sprite resolution, border blank, and so on) and is
  write-only, so we must write the system's own value back. **Risk**: we have to learn that value (from the screen's mode,
  or by reading it out of the view port's built copper list). The spike decides; 12-bit is the fallback.
- Rows are in device lines: a copper wait is per display line, so on a scale-2 display a step every second line is enough.
- Lines are counted from the top of the view port; a dragged-down screen moves with it (user copper lists are relative to
  the view port). The screen's own colour of the pen is the colour above the first wait.
- Double buffering: `ChangeScreenBuffer` changes the bitplane pointers; the user copper list should stay. **Not verified**;
  the spike checks it with Swap and Copy.
- Interlace and the copper: a laced view port needs the list to work on both fields; graphics.library builds both lists
  from the user list (not verified).
- Cost: a few hundred copper moves per frame, no CPU time after setup.

### Background restore

With the gradient pen idea the background is redrawn by filling in the pen, as today. Games that want a picture background
(a texture, not a gradient) need a background bitmap and `Video.RestoreBackground(x, y, w, h)`; that is a separate, later
feature and not part of this design.

## 7. Window mode on the Workbench

A game can ask for a window instead of a screen (`DisplayKind.Window`, `MODE=WINDOW`, the settings screen).

### Opening

`OpenWindowTagList` on the default public screen with a title, drag bar, depth gadget, close gadget, and (if the game allows)
a size gadget; inner size = minimum logical size x scale; minimum inner size = the minimum logical size at scale 1; IDCMP:
mouse buttons, mouse move, raw keys, close window, new size, refresh window, active / inactive window. Simple refresh: the
game's back buffer is the true picture, so a refresh is one blit from it (`BeginRefresh` / `EndRefresh` around it).

The scale in a window: the Workbench's pixel aspect (a PAL hires Workbench is 2 x 1) times the largest integer that fits the
inner size. A 640 x 256 PAL hires Workbench gets 2 x 1; a 1024 x 768 RTG Workbench gets 2 x 2 (a 640 x 512 window).

### Colours

| Workbench | How |
|---|---|
| Palette screen (native, any depth; RTG CLUT) | The game's palette is a request, not a promise. For each colour the program uses, `ObtainBestPenA` on the Workbench's colour map (precision "image"; fail if bad, then fall back). Pens are shared; `ReleasePen` for each at close. Drawing calls keep logical pen numbers and `Video` maps them through a pen table; art is remapped once, at load, into a copy with the Workbench's depth (CPU, fast RAM to chip RAM). A Workbench with few colours (4, 8, 16) gives a poor picture: the game then offers its own screen (open question Q6). The asset pipeline can prepare a reduced "window palette" (section 9) so the request is small. |
| RTG 16/24/32-bit (most RTG Workbenches, OS4, MorphOS, AROS) | No pen limits. Phase 5 still uses pens (`ObtainBestPen` returns exact colours on a true-colour screen) and the planar path; phase 6 adds the chunky canvas: the game draws into an 8-bit canvas in fast RAM, and the canvas is presented with `WriteLUTPixelArray` (cybergraphics v41: 8-bit source plus a colour table, any destination depth), which also does the per-row gradient. |
| sdl | A desktop window is always the "window mode". |

The copper gradient is not available in a window (the view port is the Workbench's): a window uses bands or the per-row
palette.

### Double buffering in a window

`ChangeScreenBuffer` is screen-only. A window uses the existing Copy mode: a friend bitmap of the Workbench screen's bitmap
(`AllocBitMap` with the screen bitmap as friend), `Flip` blits it to the window's rast port (clipped by the layer, so a
partly covered window is fine). The same bitmap answers refresh messages. Memory on a palette Workbench is in chip RAM.

### Resize, close, activation

- **New size**: work out the new scale and origin, allocate a new back buffer, set `Display.Changed`. The game lays out and
  redraws. The art set may change (2x to 1x) if the scale changes.
- **Close gadget**: `Input.CloseRequested` becomes true. The game decides (ask, save, quit).
- **Inactive window**: `Input.Active` false. A slow game can keep going; an action game may pause.
- Window position and size are remembered with the mode choice.

### Switching between window and screen

`Display.Reopen(request)` closes and opens. Art in chip RAM stays if the art scale and depth stay; else the set is reloaded.

## 8. API sketch (E# subset)

Plain static classes, structs and enums, as in `ESharp.Platform` today. Names are proposals.

```csharp
namespace ESharp.Platform;

public enum DisplayKind { Screen = 0, Window = 1 }
public enum DisplayFamily { Unknown, Native15kHz, Native31kHz, Rtg, Os4, MorphOS, Aros, Sdl, Host }
public enum GradientKind { None, Copper, RowPalette, Bands, Flat }

public struct DisplayRequest
{
    public int MinWidth, MinHeight;   // logical size the game needs, e.g. 320 x 256
    public int MaxArtScale;           // highest scale the game ships art for (1 or 2)
    public int Depth;                 // bitplanes wanted (8 for 256 colours); capped by the mode
    public DisplayKind Kind;
    public uint ModeId;               // 0: follow the Workbench (section 4)
    public bool AllowLace;            // default false
    public bool Resizable;            // window only
    public bool CenterFixedLayout;    // true: logical size is exactly MinWidth x MinHeight, centred
    public string Title;              // window title / screen title
}

public static class Display
{
    public static bool Open(in DisplayRequest request);        // false: nothing fits (try Video.Open(320, 256, 5))
    public static bool Reopen(in DisplayRequest request);      // mode or kind change
    public static void Close();

    public static int Width { get; }        // logical
    public static int Height { get; }
    public static int ScaleX { get; }
    public static int ScaleY { get; }
    public static int ArtScale { get; }     // which image variant is drawn
    public static int DeviceWidth { get; }
    public static int DeviceHeight { get; }
    public static DisplayKind Kind { get; }
    public static DisplayFamily Family { get; }
    public static uint ModeId { get; }
    public static string ModeName { get; }   // from the display database, "" if none
    public static bool Changed { get; }      // resized / reopened since the last ClearChanged
    public static void ClearChanged();

    public static bool PickMode(ref DisplayRequest request);   // asl screen-mode requester; false: cancelled
    public static bool ParseMode(string text, out uint modeId);
    public static bool LoadChoice(string program, ref DisplayRequest request);   // ENV:/ENVARC:
    public static bool SaveChoice(string program, in DisplayRequest request);
}

public static class Palette
{
    public static void Set(ReadOnlySpan<uint> rgb);            // logical pens 0..n-1, 0x00RRGGBB
    public static int Pen(int logicalPen);                      // device pen (identity on own screens)
    public static bool SetGradient(int pen, uint topRgb, uint bottomRgb, int topRow, int bottomRow);
    public static bool SetGradientStops(int pen, ReadOnlySpan<uint> rgb, ReadOnlySpan<int> rows);
    public static void ClearGradient(int pen);
    public static GradientKind Gradient { get; }
}

// Generated by esharp assets for an image with several scales (section 9).
public struct ImageSet
{
    public int Width, Height;          // in logical pixels (the 1x size)
    public int Depth;
    public int Scales;                 // bit n set: a variant for scale n+1 exists
}

public static class Video   // additions
{
    public static void Draw(in ImageSet image, int sx, int sy, int w, int h, int x, int y);        // logical units, masked if it has a mask
    public static bool LoadImage(ref ImageSet image, string file);                                 // "load": "file" sets (section 9)
    public static void FreeImage(ref ImageSet image);
}

public static class Input   // additions
{
    public static int DeviceX { get; }
    public static int DeviceY { get; }
    public static bool CloseRequested { get; }
    public static bool Active { get; }
}
```

Use in a game:

```csharp
DisplayRequest r = new DisplayRequest();
r.MinWidth = 320; r.MinHeight = 256; r.MaxArtScale = 2; r.Depth = 8; r.Title = "My game";
Display.LoadChoice("MyGame", ref r);
if (Args.Has("WINDOW")) r.Kind = DisplayKind.Window;
if (!Display.Open(r)) { r.ModeId = 0; r.MaxArtScale = 1; r.Depth = 5; if (!Display.Open(r)) return 20; }
Palette.Set(Art.GamePalette);
Palette.SetGradient(Pens.Table, 0x1C5A36, 0x0A2616, 0, Display.Height);
Video.EnableDoubleBuffer();
while (!Input.CloseRequested)
{
    Input.Poll();
    if (Display.Changed) { Layout(Display.Width, Display.Height); RedrawAll(); Display.ClearChanged(); }
    ...
    Video.Flip();
}
```

`Video.Open(w, h, depth)` stays. It becomes `Display.Open` with `ModeId` from the old size rules, scale 1, so every existing
sample behaves as now (checked by the existing sample runs).

On the host, the fake takes `Display.HostWorkbench(width, height, aspectX, aspectY, family, depth)` and records what was
opened, so the mode logic is unit-tested without an emulator. `Video.HostRowPalette(row)` returns the per-row colour of a
gradient pen.

## 9. Art pipeline

`assets.json` grows, and stays backward compatible (an existing manifest gives byte-identical output; the drift tests guard
that).

```json
{
  "namespace": "MyGame",
  "class": "Art",
  "palette": { "name": "Game", "colors": 256, "fixed": "palette.png", "reserve": [ { "first": 240, "count": 16 } ],
               "window": 32 },
  "images": [
    { "name": "Sheet", "palette": "Game", "mask": true, "load": "file",
      "scales": { "1": "sheet.png", "2": "sheet@2x.png" } },
    { "name": "Logo", "file": "logo.png", "colors": 16 }
  ],
  "fonts": [
    { "name": "Ui", "palette": "Game", "first": 32, "scales": { "1": { "file": "ui8.png", "cell": [ 6, 8 ] },
                                                                "2": { "file": "ui16.png", "cell": [ 12, 16 ] } } }
  ]
}
```

- **Shared palette** (`"palette"`): one palette for many images, so a 256-colour screen shows all art at once. Either fixed
  from a file (a strip of colours) or built by median cut over all images that use it. `reserve` keeps pens free for the
  program (table colours, gradient pens, bands). Output: `uint[] GamePalette`.
- **Window palette** (`"window": 32`): the generator also reduces the shared palette to N colours and writes a map from
  each full pen to the nearest reduced pen (`byte[] GameWindowMap`), so window mode on a palette Workbench asks for only N
  pens and remaps art through the map.
- **Scales** (`"scales"`): one file per art scale, drawn by an artist (or by a tool) at that size. The 2x file must be twice
  the 1x size; the generator checks it. A missing scale is filled at run time by replication from the next lower one.
- **Loading** (`"load": "file"`): instead of `[ChipRam]` arrays in the executable, each variant is written to a data file
  next to the program (`<name>.<scale>x.esi`: a small header plus planar planes and mask) and loaded into chip RAM by
  `Video.LoadImage` when the display needs that scale. A 1x-only stock A1200 never loads the 2x set. Default stays
  `"executable"`.
- **Chunky output** (phase 6): `"chunky": true` also writes one byte per pixel for the chunky canvas (fast RAM).
- **Fonts**: a PNG grid of glyphs per scale, turned into a one-plane template strip for `BltTemplate` (as `Font` uses now) plus
  widths. Generic: the program picks its own glyph set.
- The report lists chip RAM per variant and the total for each scale, and fails over a budget (as today).

Example sizes (**estimate**, for planning): a sprite sheet of 336 x 92 at 5 planes is about 19 KB; at 2x and 8 planes
(672 x 184) about 121 KB plus a 15 KB mask.

## 10. Test plan

Unit tests (`dotnet test`, no emulator):

- Mode choice: the host fake with Workbench families (PAL lores, PAL hires, DblPAL, Productivity, RTG 1024 x 768, OS4
  640 x 480 only) and checks of the picked size, scale, origin, art scale and family.
- The Amiga `Display` code built with the host gcc against a shim (as `DoubleBufferTests` does): tag lists given to
  `BestModeIDA`, DisplayInfo reads at the measured offsets, the monitor mask, the copper list (number and order of
  `CWait` / `CMove`, register values for 12-bit and 24-bit), window IDCMP flags, `ObtainBestPenA` / `ReleasePen` pairs.
- Input mapping round trips for scales 1-4, mixed aspects, origins, borders.
- Assets: shared palette, scales (size check), window map, file output read back, unchanged output for old manifests.

Emulator and VM runs (each prints `ok` / `FAIL` lines; screenshots looked at, not just taken):

| Run | What it shows |
|---|---|
| FS-UAE A1200, Kickstart 3.1 (40.069), `--model A1200` | PAL lores 8 planes, copper gradient (12/24-bit), hires 2 x 1, laced 2 x 2 (chosen), window on the Workbench screen (palette pens), the screen-mode requester, timings per mode. Workbench families other than PAL need the monitor drivers from a Workbench disk (the hwpack's `--wb` setup); without them only PAL/NTSC exist. |
| FS-UAE A4000/040, the A4000 Kickstart 3.1 ROM (40.068) whose path the user provides | 31 kHz families (DblPAL, Productivity) with fast RAM; the default 2 x 2 path; timings. Needs `--model A4000/040` (or similar) added to `esharp run`, which today offers A1200, A500, A500+ and A600. |
| FS-UAE with an emulated RTG board | Only if the user provides an RTG driver install; otherwise not verified. |
| AmigaOS 4.1 VM (`--target os4`) | Full screen at 800 x 600 (2 x 2), window on the OS4 Workbench (true colour), resize, the requester. |
| Hosted AROS (`--target aros`) | Window on Wanderer, RTG screen, gradient bands / row palette. |
| sdl (`esharp-sdl`, headless and Xvfb) | Scales, resizable window, row-palette gradient, screenshots. |
| Real hardware (`tools/hwpack`) | Add the new sample; results come from people with machines. Not verified until then. |

Every result goes into `docs/STATUS.md` with what was and was not run.

## 11. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| 24-bit copper needs the system's BPLCON3 value | 12-bit gradient only, visible banding on dark colours | Spike first; 12-bit plus a light ordered dither in the art above the background |
| Chip RAM on a stock A1200 at 640 x 512 x 8 with double buffering and 2x art | Out of memory | Art from files, Copy rather than Swap where it saves a bitmap, report the budget, fall back to 1x |
| Display DMA slows the CPU and blitter in hires / 31 kHz / 8-plane modes on chip-only machines | Slow redraws | Measured per mode in phase 1; slow games accept it; double buffering hides it |
| 31 kHz families do not exist in FS-UAE without monitor drivers from a Workbench disk | Cannot test DblPAL / Productivity in the default setup | Test with the user's Workbench disk (hwpack `--wb`); unit tests with the shim |
| Pen sharing on a Workbench with few colours | Ugly window | Window palette; offer full screen; document the minimum |
| Planar blits through RTG drivers are slow (never measured) | Slow on RTG | Phase 6 chunky canvas |
| Fractional fits (640 x 480 for a 256-line game) | Small picture or cut layout | Flexible layouts; let games accept 240 lines; choose 800 x 600 when it exists |
| More API surface in `Video`/`Display` for every target | Work on six targets | Host and sdl first, then m68k, then the rest; ES0102-style errors for calls a target cannot do |
| User copper list and `ChangeScreenBuffer` / interlace interplay | Gradient lost or wrong | Checked in the spike |

## 12. Open questions for the maintainer

1. **Default kind**: full screen by default with window mode as an option, or a window by default when the Workbench is on
   RTG / OS4 (like Solitaire on Windows)?
2. **Scale 2 on a stock 15 kHz A1200**: never by default (proposed), or offer 640 x 256 hires with 2 x 1 art when the
   Workbench is hires?
3. **240-line modes** (640 x 480 at 2x): should games be asked to support a 320 x 240 minimum, or fall back to 1x centred?
4. **24-bit copper** worth the BPLCON3 risk, or is 12-bit plus art dithering enough for the first release?
5. **Super-hires 8 planes**: offer it in the requester (no double buffering), or hide it?
6. **Window on a palette Workbench with fewer than 32 free pens**: refuse window mode, or open it with a reduced palette?
7. **Art at 2x**: is the expectation hand-drawn 2x art per game (proposed), with replication only as a fallback?
8. **Saving the choice**: `ENVARC:` text file (proposed), or the icon's tool types (needs icon.library bindings)?
9. **Chunky canvas (phase 6)**: in scope for v1, or a v2 item?
10. **FS-UAE A4000/040 support** in `esharp run` (a `--model` value): add it in phase 1?

## 13. Work order

Sizes: S about a day, M a few days, L a week or more (agent working time, including the target runs).

| Phase | Work | Size | Depends on |
|---|---|---|---|
| 0a | Spike: copper gradient on the A1200 (12-bit, 24-bit, with Swap and Copy), screenshots | S | |
| 0b | Spike: list the display database (`NextDisplayInfo`, names, max depth, aspect, monitor) on A1200 (with and without Workbench monitors), OS4, AROS; `--model` for an A4000/040 in `esharp run` | S | |
| 0c | Spike: a plain window on the Workbench with `ObtainBestPenA`, Copy double buffering, refresh, close | S | |
| 1 | `Display` core: request, Workbench family, `BestModeIDA`, DisplayInfo, scale and origin in every `Video` call, input mapping, host fake, sdl scale; `Video.Open` as a scale-1 wrapper; measurements of fill and blit rates per mode | M | 0b |
| 2 | Assets: shared palette, reserves, scales, `"load": "file"`, `ImageSet` and `Video.Draw`, replication fallback | M | 1 |
| 3 | Gradient pen: copper, row palette (sdl, host), bands, flat; `Palette` | M | 0a, 1 |
| 4 | Mode choice: MODE option, `ParseMode`, the asl screen-mode requester, `LoadChoice` / `SaveChoice` | S-M | 1 |
| 5 | Window mode: Workbench window, pen table and art remap, window palette, resize, close, activation, Copy double buffer, `Reopen` | L | 0c, 1, 2 |
| 6 | Chunky canvas for RTG 16/24-bit, OS4, MorphOS, AROS: C renderer shared with sdl, `WriteLUTPixelArray` present, row-palette gradient | L | 1, 3 |
| 7 | Fonts per scale (asset fonts, `Font` scale-aware) | S-M | 2 |
| 8 | Test matrix runs (A1200, A4000/040, OS4, AROS, sdl), hwpack entry, `docs/STATUS.md` | M | each phase |

A game can use phases 1-4 without 5 and 6: a full-screen 256-colour game with a gradient and a mode choice.
