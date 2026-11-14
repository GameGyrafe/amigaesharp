using System;

namespace ESharp.Platform;

/// <summary>How <see cref="Video"/> draws: straight to the screen, or into a back buffer that <see cref="Video.Flip"/> shows.</summary>
public enum VideoBuffering
{
    /// <summary>No back buffer: drawing shows as it happens.</summary>
    Single = 0,
    /// <summary>Two screen bitmaps that change places at each Flip (the back buffer then holds the frame before the one shown).</summary>
    Swap = 1,
    /// <summary>An offscreen bitmap copied to the screen at each Flip (the back buffer keeps the frame just shown).</summary>
    Copy = 2,
}

/// <summary>
/// A single full-screen indexed-colour display. On the Amiga it opens a quiet custom Intuition screen
/// with a borderless backdrop window (system friendly: no hardware takeover) and draws with the
/// graphics.library blitter calls. On SDL it is a chunky framebuffer shown in a window. On the host it is a software framebuffer so game logic can be
/// tested under dotnet. Colours are 4 bits per channel (OCS); pixels are palette indices.
/// </summary>
public static unsafe class Video
{
#if AMIGA
    private static void* _screen;
    private static void* _window;
    private static void* _rastPort;
    private static void* _viewPort;
    private static int _width;
    private static int _height;
    private static int _depth;
    private static void* _windowRp;    // the window's own (layered, clipped) rast port; _rastPort is where drawing goes
    private static int _left;          // the window's place on the screen (non-zero when a larger mode centres it, AmigaOS 4)
    private static int _top;
    private static int _dx;            // added to every drawing coordinate: the window's place when drawing goes straight to a screen bitmap
    private static int _dy;

    /// <summary>
    /// True on a machine with the AGA chipset (A1200, A4000: 8 bitplanes, 256 colours out of 16.7 million). Needs
    /// Kickstart 3.0 or newer; older systems report false.
    /// </summary>
    public static bool HasAga()
    {
        void* gfx = Exec.OpenLibrary("graphics.library", 39);
        if (gfx == null) return false;
        Exec.CloseLibrary(gfx);
        // The AGA modes are off after boot until a program asks for the best chip set; asking is harmless on other chips.
        uint rev = Graphics.SetChipRev(0xFFFFFFFFu);
        return (rev & 12) == 12;                      // GFXF_AA_ALICE | GFXF_AA_LISA
    }

    /// <summary>
    /// Opens a screen of <paramref name="width"/> x <paramref name="height"/> with <paramref name="depth"/> bitplanes (1..5 on OCS/ECS,
    /// up to 8 on AGA: check <see cref="HasAga"/> first or fall back when this returns false). False on failure.
    /// <para>
    /// The display mode follows the size on the classic chipset: wider than 400 pixels is hires (640 x 256 is a hires PAL screen with
    /// square-ish pixels, not a lores screen that scrolls), wider than 800 super-hires, and taller than 300 lines interlaced. Without AGA
    /// (hosted AROS, a graphics card system) a depth above 5 opens an 8-bit RTG screen instead when there is one (see <see cref="OpenRtg"/>).
    /// </para>
    /// </summary>
    public static bool Open(int width, int height, int depth)
    {
        if (_screen != null) return false;
        // Kickstart 2.x opens a screen with more bitplanes than the chips have and then misbehaves: refuse it here.
        if (depth > 5 && !HasAga()) return depth <= 8 && HasRtg(width, height) && OpenRtg(width, height, depth);
        return OpenMode(width, height, depth, NativeMode(width, height));
    }

    /// <summary>The classic display mode for a size (graphics/modeid.h keys on the default monitor), or 0 to let the system choose.</summary>
    private static uint NativeMode(int width, int height)
    {
#if AMIGAOS3
        uint mode = 0;
        if (width > 800) mode |= 0x8020;          // SUPER_KEY (super-hires, 1280 wide)
        else if (width > 400) mode |= 0x8000;     // HIRES_KEY
        if (height > 300) mode |= 0x0004;         // LORESLACE_KEY / HIRESLACE_KEY: interlaced
        return mode == 0 ? 0u : mode;
#else
        return 0;
#endif
    }

    /// <summary>True when the open screen has hires (or finer) pixels: a native hires mode, or any screen 640 or more pixels wide on RTG and other systems.</summary>
    public static bool IsHires()
    {
        if (_screen == null) return false;
#if AMIGAOS3
        if (!IsRtg()) return (Graphics.GetVPModeID(_viewPort) & 0x8000) != 0;
#endif
        return _width >= 640;
    }

    /// <summary>
    /// True if a retargetable graphics (RTG) card driver is installed (Picasso96, CyberGraphX, or the AmigaOS 4 / MorphOS
    /// graphics system) and it has an 8-bit mode of at least <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    public static bool HasRtg(int width, int height)
    {
        return RtgMode(width, height) != CyberGfx.InvalidId;
    }

    private static uint RtgMode(int width, int height)
    {
        void* cyber = Exec.OpenLibrary("cybergraphics.library", 40);
        if (cyber == null) return CyberGfx.InvalidId;
        CyberGfx.SetBase(cyber);
        if (!CyberGfx.Ready())
        {
            Exec.CloseLibrary(cyber);   // no interface (AmigaOS 4): RTG is unavailable
            return CyberGfx.InvalidId;
        }
        uint id = CyberGfx.InvalidId;
        TagList t = new TagList();
        t.Add(CyberGfx.CYBRBIDTG_Depth, 8);
        t.Add(CyberGfx.CYBRBIDTG_NominalWidth, width);
        t.Add(CyberGfx.CYBRBIDTG_NominalHeight, height);
        nuint* p = t.End();
        if (p != null) id = CyberGfx.BestCModeIDTagList(p);
        CyberGfx.SetBase(null);
        Exec.CloseLibrary(cyber);
        return id;
    }

    /// <summary>
    /// Opens the screen on an RTG card instead of the native chipset. The screen is always 8 bits per pixel (256 palette entries), so
    /// <paramref name="depth"/> is the number of colours the program uses (1..8) and the same planar assets work, although each blit is
    /// converted by the driver, so it is slower than on the chipset. False if there is no RTG mode that large (see <see cref="HasRtg"/>).
    /// </summary>
    public static bool OpenRtg(int width, int height, int depth)
    {
        if (_screen != null) return false;
        uint id = RtgMode(width, height);
        if (id == CyberGfx.InvalidId) return false;
        return OpenMode(width, height, 8, id);
    }

    /// <summary>True if the open screen is on an RTG card (not the native chipset).</summary>
    public static bool IsRtg()
    {
        if (_screen == null) return false;
        void* cyber = Exec.OpenLibrary("cybergraphics.library", 40);
        if (cyber == null) return false;
        CyberGfx.SetBase(cyber);
        if (!CyberGfx.Ready())
        {
            Exec.CloseLibrary(cyber);
            return false;
        }
        bool yes = CyberGfx.IsCyberModeID(Graphics.GetVPModeID(_viewPort)) != 0;
        CyberGfx.SetBase(null);
        Exec.CloseLibrary(cyber);
        return yes;
    }

#if AMIGAOS4
    /// <summary>
    /// AmigaOS 4: the display mode nearest to <paramref name="width"/> x <paramref name="height"/> (graphics BestModeIDA) and its
    /// nominal size, at least the requested one. Graphics cards often have no mode as small as 320 x 200 (the sm501 in QEMU starts at
    /// 640 x 480), so the screen is opened at the mode's size and the picture centred in it. 0 (system's choice) if there is none.
    /// </summary>
    private static uint NearestMode(int width, int height, int depth, out int modeWidth, out int modeHeight)
    {
        modeWidth = width;
        modeHeight = height;
        uint id = Layout.InvalidModeId;
        TagList t = new TagList();
        t.Add(Layout.BidNominalWidth, width);
        t.Add(Layout.BidNominalHeight, height);
        t.Add(Layout.BidDepth, depth);
        nuint* p = t.End();
        if (p != null) id = Graphics.BestModeIDA(p);
        if (id == Layout.InvalidModeId) return 0;
        byte* dims = Mem.AllocFast(Layout.DimInfoSize);
        if (dims == null) return id;
        void* handle = Graphics.FindDisplayInfo(id);
        if (handle != null && Graphics.GetDisplayInfoData(handle, dims, Layout.DimInfoSize, Layout.DtagDims, id) != 0)
        {
            byte* r = dims + Layout.DimNominal;
            int w = *(short*)(r + Layout.RectMaxX) - *(short*)(r + Layout.RectMinX) + 1;
            int h = *(short*)(r + Layout.RectMaxY) - *(short*)(r + Layout.RectMinY) + 1;
            if (w > modeWidth) modeWidth = w;
            if (h > modeHeight) modeHeight = h;
        }
        Mem.Free(dims);
        return id;
    }
#endif

    private static bool OpenMode(int width, int height, int depth, uint displayId)
    {
        int screenWidth = width, screenHeight = height;
#if AMIGAOS4
        if (displayId == 0) displayId = NearestMode(width, height, depth, out screenWidth, out screenHeight);
#endif
        TagList t = new TagList();
        t.Add(Intuition.SA_Width, screenWidth);
        t.Add(Intuition.SA_Height, screenHeight);
        t.Add(Intuition.SA_Depth, depth);
        t.Add(Intuition.SA_Quiet, true);
        t.Add(Intuition.SA_Type, Intuition.CustomScreen);
        if (displayId != 0) t.Add(Intuition.SA_DisplayID, displayId);   // native: let the system choose
        nuint* p = t.End();
        if (p != null) _screen = Intuition.OpenScreenTagList(null, p);
        if (_screen == null) return false;

        TagList w = new TagList();
        w.Add(Intuition.WA_CustomScreen, _screen);
        w.Add(Intuition.WA_Left, (screenWidth - width) / 2);      // centred when the mode is larger (AmigaOS 4); else 0
        w.Add(Intuition.WA_Top, (screenHeight - height) / 2);
        w.Add(Intuition.WA_Width, width);
        w.Add(Intuition.WA_Height, height);
        w.Add(Intuition.WA_Borderless, true);
        w.Add(Intuition.WA_Backdrop, true);
        w.Add(Intuition.WA_Activate, true);
        w.Add(Intuition.WA_RMBTrap, true);
        w.Add(Intuition.WA_NoCareRefresh, true);
        w.Add(Intuition.WA_IDCMP, Intuition.IDCMP_MOUSEBUTTONS | Intuition.IDCMP_MOUSEMOVE | Intuition.IDCMP_RAWKEY);
        w.Add(Intuition.WA_ReportMouse, true);
        nuint* wp = w.End();
        if (wp != null) _window = Intuition.OpenWindowTagList(null, wp);
        if (_window == null)
        {
            Intuition.CloseScreen(_screen);
            _screen = null;
            return false;
        }
        _rastPort = *(void**)((byte*)_window + Intuition.WindowRPort);
        _windowRp = _rastPort;
        _viewPort = (byte*)_screen + Intuition.ScreenViewPort;
        _left = (screenWidth - width) / 2;
        _top = (screenHeight - height) / 2;
        _dx = 0;
        _dy = 0;
        _width = width;
        _height = height;
        _depth = depth;
        return true;
    }

    public static bool IsOpen() { return _screen != null; }

    /// <summary>The backdrop window (struct Window *), used by <see cref="Input"/>.</summary>
    public static void* Window { get { return _window; } }

    public static void Close()
    {
        DisableDoubleBuffer();
        if (_window != null) Intuition.CloseWindow(_window);
        if (_screen != null) Intuition.CloseScreen(_screen);
        Keys.Close();
        _window = null;
        _screen = null;
        _rastPort = null;
        _windowRp = null;
        _viewPort = null;
    }

    /// <summary>Sets palette entry <paramref name="index"/> from 4-bit components (0..15).</summary>
    public static void SetColor(int index, int r, int g, int b)
    {
        Graphics.SetRGB4(_viewPort, index, r, g, b);
    }

    /// <summary>
    /// Sets palette entry <paramref name="index"/> from 8-bit components (0..255). With an AGA screen (more than 5 bitplanes) all 24
    /// bits are used; on other screens the colour is rounded to 4 bits per channel.
    /// </summary>
    public static void SetColor24(int index, int r, int g, int b)
    {
        if (_depth > 5)
            Graphics.SetRGB32(_viewPort, (uint)index, (uint)r * 0x01010101u, (uint)g * 0x01010101u, (uint)b * 0x01010101u);
        else
            Graphics.SetRGB4(_viewPort, index, r >> 4, g >> 4, b >> 4);
    }

    /// <summary>Loads palette entries from 0x00RRGGBB values (see <see cref="SetColor24"/>), starting at entry 0.</summary>
    public static void SetPalette24(ReadOnlySpan<uint> rgb)
    {
        for (int i = 0; i < rgb.Length; i++)
            SetColor24(i, (int)((rgb[i] >> 16) & 255), (int)((rgb[i] >> 8) & 255), (int)(rgb[i] & 255));
    }

    /// <summary>Loads palette entries from 0x0RGB words, starting at entry 0.</summary>
    public static void SetPalette(ReadOnlySpan<ushort> rgb4)
    {
        for (int i = 0; i < rgb4.Length; i++)
            Graphics.SetRGB4(_viewPort, i, (rgb4[i] >> 8) & 15, (rgb4[i] >> 4) & 15, rgb4[i] & 15);
    }

    /// <summary>Fills a rectangle with palette entry <paramref name="color"/> using the blitter.</summary>
    public static void FillRect(int x, int y, int width, int height, int color)
    {
        // Clipped here: a back buffer's rast port has no layer to clip it (the window's clips the same rectangle).
        if (x < 0) { width += x; x = 0; }
        if (y < 0) { height += y; y = 0; }
        if (x + width > _width) width = _width - x;
        if (y + height > _height) height = _height - y;
        if (width <= 0 || height <= 0 || _rastPort == null) return;
        Graphics.SetAPen(_rastPort, color);
        Graphics.RectFill(_rastPort, x + _dx, y + _dy, x + _dx + width - 1, y + _dy + height - 1);
    }

    /// <summary>The palette index at a pixel (of the back buffer when double buffered), or -1 outside the screen.</summary>
    public static int GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height || _rastPort == null) return -1;
        return Graphics.ReadPixel(_rastPort, x + _dx, y + _dy);
    }

    /// <summary>
    /// Draws the set bits of a one-plane template (<paramref name="bytesPerRow"/> bytes per row, word aligned, chip RAM) in
    /// <paramref name="color"/>, leaving the other pixels alone: one graphics call (BltTemplate), used by <see cref="Font"/>.
    /// </summary>
    internal static void Template(byte* bits, int bytesPerRow, int width, int height, int x, int y, int color)
    {
        if (x < 0 || y < 0) return;               // the caller (Font) draws such text with rectangles
        if (x + width > _width) width = _width - x;
        if (y + height > _height) height = _height - y;
        if (_rastPort == null || width <= 0 || height <= 0) return;
        Graphics.SetAPen(_rastPort, color);
        Graphics.SetDrMd(_rastPort, 0);           // JAM1: only the set bits
        Graphics.BltTemplate(bits, 0, bytesPerRow, _rastPort, x + _dx, y + _dy, width, height);
        Graphics.WaitBlit();                      // the blit may still be reading the strip: the CPU refills it next
        Graphics.SetDrMd(_rastPort, 1);           // back to JAM2, a new window's mode
    }

    /// <summary>
    /// Copies a planar image (plane after plane, each row padded to 16 pixels, as <c>esharp assets</c>
    /// writes it) to the screen with the blitter. The data must be in chip RAM.
    /// </summary>
    public static void Blit(ReadOnlySpan<byte> planar, int width, int height, int depth, int x, int y)
    {
        BlitRegion(planar, width, height, depth, 0, 0, width, height, x, y);
    }

    /// <summary>Copies the <paramref name="w"/> x <paramref name="h"/> rectangle at (<paramref name="sx"/>, <paramref name="sy"/>) of a planar image (a sprite sheet) to (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void BlitRegion(ReadOnlySpan<byte> planar, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, null, width, height, depth, sx, sy, w, h, x, y);
    }

    /// <summary>
    /// Like <see cref="BlitRegion"/> but only pixels whose bit is set in <paramref name="mask"/> (one plane in the
    /// layout of the image planes, as <c>esharp assets</c> writes with <c>"mask": true</c>) are drawn. Chip RAM.
    /// </summary>
    public static void BlitMasked(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, mask, width, height, depth, sx, sy, w, h, x, y);
    }

    private static void BltCommon(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        if (x < 0) { sx -= x; w += x; x = 0; }
        if (y < 0) { sy -= y; h += y; y = 0; }
        if (x + w > _width) w = _width - x;
        if (y + h > _height) h = _height - y;
        if (w <= 0 || h <= 0 || _rastPort == null) return;
        x += _dx;
        y += _dy;
        int bytesPerRow = ((width + 15) / 16) * 2;
        nuint[] bm = new nuint[10];   // struct BitMap (40 bytes, or 72 with 64-bit pointers), pointer-aligned
        fixed (nuint* bmw = bm)
        {
            byte* bb = (byte*)bmw;
            *(ushort*)(bb + 0) = (ushort)bytesPerRow;      // BytesPerRow
            *(ushort*)(bb + 2) = (ushort)height;           // Rows
            // Planes the image does not have stay NULL: the blitter reads them as zeros, so every plane of the
            // screen gets written (a shallower bitmap would leave the remaining planes untouched).
            bb[4] = 0;                                     // Flags
            bb[5] = (byte)_depth;                          // Depth
        }
        fixed (byte* data = planar)
        {
            fixed (nuint* bmw = bm)
            {
                byte* bb = (byte*)bmw;
                for (int p = 0; p < depth; p++)
                    *(byte**)(bb + 8 + p * sizeof(byte*)) = data + p * bytesPerRow * height;
            }
            fixed (nuint* b = bm)
            {
                if (mask.Length == 0)
                {
                    Graphics.BltBitMapRastPort(b, sx, sy, _rastPort, x, y, w, h, 0xC0);
                }
                else
                {
                    fixed (byte* m = mask)
                    {
                        Graphics.BltMaskBitMapRastPort(b, sx, sy, _rastPort, x, y, w, h, 0xE0, m);
                    }
                }
            }
        }
    }

    /// <summary>Waits for <paramref name="frames"/> video frames (50 per second on PAL, 60 on NTSC): each is a WaitTOF, so it is synced to the display.</summary>
    public static void WaitFrames(int frames)
    {
        Frame.Wait(frames);
    }

    /// <summary>Waits for the next vertical blank (graphics.library WaitTOF), so drawing after it syncs to the display.</summary>
    public static void WaitVBlank() { Frame.WaitVBlank(); }

    // ---- double buffering ----
    private static VideoBuffering _buffering;
    private static void* _sb0;            // struct ScreenBuffer of the screen's own bitmap (Swap)
    private static void* _sb1;            // and of the second bitmap
    private static int _backIndex;        // which of the two is the back buffer
    private static void* _safePort;       // DBufInfo replies: safe to draw into the old buffer
    private static void* _dispPort;       // and the new buffer is on display (safe to change again)
    private static int _safeMessages;     // per port: 0 not known yet, 1 the system sends them (wait for them), 2 it does not
    private static int _dispMessages;
    private static bool _changePending;   // a ChangeScreenBuffer whose "on display" message has not been taken
    private static void* _offBitMap;      // Copy: the offscreen bitmap
    private static byte* _backRp;         // a rast port (no layer) on the back buffer

    /// <summary>
    /// Turns on double buffering: from now on every drawing call (<see cref="FillRect"/>, the blits, <see cref="Font"/>,
    /// <see cref="Fonts"/>, <see cref="GetPixel"/>) goes to a back buffer that is not on display, and <see cref="Flip"/> shows it in one
    /// go, so a whole-screen redraw does not flicker. Uses two screen bitmaps (intuition AllocScreenBuffer/ChangeScreenBuffer, graphics
    /// v39) when the system gives them (<see cref="VideoBuffering.Swap"/>), else an offscreen bitmap copied with one blit per
    /// <see cref="Flip"/> (<see cref="VideoBuffering.Copy"/>). The back buffer starts as a copy of the screen. True if it is on
    /// (also when it already was). Intuition (the window, requesters such as <see cref="Asl.PickFile"/>) draws into the screen's own bitmap,
    /// which is not on display during <see cref="VideoBuffering.Swap"/>: <see cref="Asl.PickFile"/> turns double buffering off first.
    /// </summary>
    public static bool EnableDoubleBuffer()
    {
        if (_screen == null) return false;
        if (_buffering != VideoBuffering.Single) return true;
        if (StartSwap()) return true;
        return EnableBackBuffer();
    }

    /// <summary>
    /// Double buffering with an offscreen bitmap (<see cref="VideoBuffering.Copy"/>) even where screen buffers are available: each
    /// <see cref="Flip"/> waits for the vertical blank and copies the whole back buffer to the screen with one blit. The fallback of
    /// <see cref="EnableDoubleBuffer"/>; useful to compare the two. True if it is on.
    /// </summary>
    public static bool EnableBackBuffer()
    {
        if (_screen == null) return false;
        if (_buffering != VideoBuffering.Single) return _buffering == VideoBuffering.Copy;
        void* screenBitMap = *(void**)((byte*)_windowRp + Layout.RastPortBitMap);
        _offBitMap = Graphics.AllocBitMap((uint)_width, (uint)_height, (uint)_depth, Layout.BmfClear | Layout.BmfDisplayable, screenBitMap);
        if (_offBitMap == null) return false;
        if (!MakeBackRastPort(_offBitMap))
        {
            Graphics.FreeBitMap(_offBitMap);
            _offBitMap = null;
            return false;
        }
        // Start from what is on the screen, as the swap does.
        Graphics.BltBitMap(screenBitMap, _left, _top, _offBitMap, 0, 0, _width, _height, 0xC0, 0xFF, null);
        Graphics.WaitBlit();
        _buffering = VideoBuffering.Copy;
        _rastPort = _backRp;
        _dx = 0;
        _dy = 0;
        return true;
    }

    private static bool MakeBackRastPort(void* bitMap)
    {
        if (_backRp == null) _backRp = Mem.AllocFast((uint)Layout.RastPortSize);
        if (_backRp == null) return false;
        Graphics.InitRastPort((void*)_backRp);
        *(void**)(_backRp + Layout.RastPortBitMap) = bitMap;
        return true;
    }

    private static bool StartSwap()
    {
        _sb0 = Intuition.AllocScreenBuffer(_screen, null, Layout.SbScreenBitMap);
        if (_sb0 != null) _sb1 = Intuition.AllocScreenBuffer(_screen, null, Layout.SbCopyBitMap);
        if (_sb1 != null) _safePort = Exec.CreateMsgPort();
        if (_safePort != null) _dispPort = Exec.CreateMsgPort();
        if (_dispPort == null || !MakeBackRastPort(*(void**)((byte*)_sb1 + Layout.SbBitMap)))
        {
            FreeSwap();
            return false;
        }
        SetReplyPorts(_sb0);
        SetReplyPorts(_sb1);
        _safeMessages = 0;
        _dispMessages = 0;
        _changePending = false;
        _backIndex = 1;
        _buffering = VideoBuffering.Swap;
        _rastPort = _backRp;
        _dx = _left;
        _dy = _top;
        return true;
    }

    private static void SetReplyPorts(void* sb)
    {
        byte* dbi = *(byte**)((byte*)sb + Layout.SbDBufInfo);
        *(void**)(dbi + Layout.DbiSafeReplyPort) = _safePort;
        *(void**)(dbi + Layout.DbiDispReplyPort) = _dispPort;
    }

    private static void FreeSwap()
    {
        if (_sb1 != null) Intuition.FreeScreenBuffer(_screen, _sb1);
        if (_sb0 != null) Intuition.FreeScreenBuffer(_screen, _sb0);
        if (_dispPort != null) Exec.DeleteMsgPort(_dispPort);
        if (_safePort != null) Exec.DeleteMsgPort(_safePort);
        _sb0 = null;
        _sb1 = null;
        _safePort = null;
        _dispPort = null;
    }

    /// <summary>
    /// Waits for a DBufInfo message at <paramref name="port"/> and takes every one there. The first time it polls (a 1/50 second
    /// delay between tries, about a second in all), because a system that never sends them would hang a plain WaitPort; once one
    /// has come, later waits sleep in WaitPort, and if none came the swap is paced by <see cref="Frame.WaitVBlank"/> instead.
    /// </summary>
    private static void TakeMessage(bool safe)
    {
        void* port = safe ? _safePort : _dispPort;
        int state = safe ? _safeMessages : _dispMessages;
        if (state == 1) Exec.WaitPort(port);
        if (state != 0)
        {
            while (Exec.GetMsg(port) != null) { }
            return;
        }
        state = 2;
        for (int i = 0; i < 50; i++)
        {
            if (Exec.GetMsg(port) != null)
            {
                while (Exec.GetMsg(port) != null) { }
                state = 1;
                break;
            }
            Dos.Delay(1);
        }
        if (safe) _safeMessages = state;
        else _dispMessages = state;
    }

    private static void* BufferBitMap(int index) { return *(void**)((byte*)(index == 0 ? _sb0 : _sb1) + Layout.SbBitMap); }

    /// <summary>
    /// Shows the back buffer and waits for the display to take it (one frame, counted by <see cref="Frame.Count"/>); drawing then goes
    /// to the new back buffer. Its contents are an older frame: the one before the frame just shown with
    /// <see cref="VideoBuffering.Swap"/>, the frame just shown with <see cref="VideoBuffering.Copy"/>, so redraw the whole screen (or all
    /// that changed in the last two frames). Without double buffering this is <see cref="WaitVBlank"/>.
    /// </summary>
    public static void Flip()
    {
        if (_buffering == VideoBuffering.Copy)
        {
            Graphics.WaitBlit();
            Frame.WaitVBlank();
            Graphics.BltBitMapRastPort(_offBitMap, 0, 0, _windowRp, 0, 0, _width, _height, 0xC0);
            Graphics.WaitBlit();
            return;
        }
        if (_buffering != VideoBuffering.Swap)
        {
            Frame.WaitVBlank();
            return;
        }
        Graphics.WaitBlit();                           // the blitter has finished the frame
        if (_changePending) TakeMessage(false);    // the previous change is on display, so another may follow
        _changePending = false;
        void* sb = _backIndex == 0 ? _sb0 : _sb1;
        int tries = 0;
        while (Intuition.ChangeScreenBuffer(_screen, sb) == 0)
        {
            if (++tries > 50) { Frame.WaitVBlank(); return; }   // Intuition stayed busy (a menu?): keep drawing into the same buffer
            Graphics.WaitTOF();
        }
        _changePending = true;
        // The old front buffer is no longer read by the display once this arrives (at the vertical blank that shows the new one).
        TakeMessage(true);
        if (_safeMessages == 2) Frame.WaitVBlank();
        else Frame.CountFrame();
        _backIndex ^= 1;
        *(void**)(_backRp + Layout.RastPortBitMap) = BufferBitMap(_backIndex);
    }

    /// <summary>Single (drawing goes straight to the screen), Swap (two screen bitmaps) or Copy (an offscreen bitmap and one blit).</summary>
    public static VideoBuffering Buffering() { return _buffering; }

    /// <summary>True between <see cref="EnableDoubleBuffer"/> (or <see cref="EnableBackBuffer"/>) and <see cref="DisableDoubleBuffer"/>.</summary>
    public static bool IsDoubleBuffered() { return _buffering != VideoBuffering.Single; }

    /// <summary>
    /// Turns double buffering off: the frame on display stays, and drawing goes straight to the screen again. <see cref="Close"/>
    /// does this too. If the system will not switch back to the screen's own bitmap (500 tries), the buffers are leaked rather than
    /// freed while on display.
    /// </summary>
    public static void DisableDoubleBuffer()
    {
        if (_buffering == VideoBuffering.Copy)
        {
            Graphics.WaitBlit();
            Graphics.FreeBitMap(_offBitMap);
            _offBitMap = null;
        }
        else if (_buffering == VideoBuffering.Swap)
        {
            Graphics.WaitBlit();
            if (_changePending) TakeMessage(false);
            _changePending = false;
            if (_backIndex == 0)
            {
                // The second bitmap is on display: copy it into the screen's own one and show that again.
                Graphics.BltBitMap(BufferBitMap(1), _left, _top, BufferBitMap(0), _left, _top, _width, _height, 0xC0, 0xFF, null);
                Graphics.WaitBlit();
                int tries = 0;
                bool changed = Intuition.ChangeScreenBuffer(_screen, _sb0) != 0;
                while (!changed && ++tries < 500)
                {
                    Graphics.WaitTOF();
                    changed = Intuition.ChangeScreenBuffer(_screen, _sb0) != 0;
                }
                if (changed)
                {
                    TakeMessage(true);
                    TakeMessage(false);
                    if (_safeMessages == 2) Frame.WaitVBlank();
                }
                else
                {
                    // The second bitmap is still on display: freeing it would free what is being shown. Leak the buffers
                    // (and the reply ports they may still post to) instead; the screen's own bitmap is not freed either.
                    _sb0 = null;
                    _sb1 = null;
                    _safePort = null;
                    _dispPort = null;
                }
            }
            FreeSwap();
        }
        if (_backRp != null) Mem.Free(_backRp);
        _backRp = null;
        _buffering = VideoBuffering.Single;
        _rastPort = _windowRp;
        _dx = 0;
        _dy = 0;
    }

    /// <summary>The rast port drawing goes to (the window's, or the back buffer's when double buffered), for <see cref="Fonts"/>.</summary>
    internal static void* DrawRastPort { get { return _rastPort; } }

    /// <summary>True when <see cref="DrawRastPort"/> has no layer: text drawn through it must be kept inside the screen by the caller.</summary>
    internal static bool DrawUnclipped { get { return _buffering != VideoBuffering.Single; } }

    internal static int DrawOffsetX { get { return _dx; } }
    internal static int DrawOffsetY { get { return _dy; } }
    internal static int DrawWidth { get { return _width; } }
    internal static int DrawHeight { get { return _height; } }
#elif SDL
    // SDL: a chunky 8-bit framebuffer and palette in the C runtime, shown in a window (integer scaled) at every
    // Frame.WaitVBlank and Dos.Delay. Drawing works on the framebuffer like the blitter works on the Amiga screen.

    /// <summary>SDL shows 256 colours on any desktop, so the AGA depths (6 to 8 bitplanes) are always available.</summary>
    public static bool HasAga() { return true; }

    private static int _sdlWidth;

    /// <summary>Any size works on SDL (a 640 x 256 screen is shown at its own size, as hires is on an Amiga).</summary>
    public static bool Open(int width, int height, int depth)
    {
        if (!Crt.es_sdl_video_open(width, height, depth)) return false;
        _sdlWidth = width;
        return true;
    }

    /// <summary>True when the screen is 640 or more pixels wide (hires on an Amiga).</summary>
    public static bool IsHires() { return Crt.es_sdl_video_is_open() && _sdlWidth >= 640; }

    /// <summary>There is no RTG card on SDL: false. Use <see cref="Open"/>, which takes up to 8 bitplanes (256 colours).</summary>
    public static bool HasRtg(int width, int height) { return false; }

    public static bool OpenRtg(int width, int height, int depth) { return false; }

    public static bool IsRtg() { return false; }

    public static bool IsOpen() { return Crt.es_sdl_video_is_open(); }

    public static void Close()
    {
        _sdlBuffering = VideoBuffering.Single;
        Crt.es_sdl_video_close();
        Keys.Close();
    }

    public static void SetColor(int index, int r, int g, int b)
    {
        Crt.es_sdl_set_color(index, (r & 15) * 17, (g & 15) * 17, (b & 15) * 17);
    }

    /// <summary>As on the Amiga: all 24 bits on a screen of more than 5 bitplanes, otherwise rounded to 4 bits per channel.</summary>
    public static void SetColor24(int index, int r, int g, int b)
    {
        if (Crt.es_sdl_video_depth() > 5) Crt.es_sdl_set_color(index, r & 255, g & 255, b & 255);
        else SetColor(index, (r & 255) >> 4, (g & 255) >> 4, (b & 255) >> 4);
    }

    public static void SetPalette24(ReadOnlySpan<uint> rgb)
    {
        for (int i = 0; i < rgb.Length; i++)
            SetColor24(i, (int)((rgb[i] >> 16) & 255), (int)((rgb[i] >> 8) & 255), (int)(rgb[i] & 255));
    }

    public static void SetPalette(ReadOnlySpan<ushort> rgb4)
    {
        for (int i = 0; i < rgb4.Length; i++) SetColor(i, (rgb4[i] >> 8) & 15, (rgb4[i] >> 4) & 15, rgb4[i] & 15);
    }

    public static void FillRect(int x, int y, int width, int height, int color) { Crt.es_sdl_fill(x, y, width, height, color); }

    public static int GetPixel(int x, int y) { return Crt.es_sdl_get_pixel(x, y); }

    public static void Blit(ReadOnlySpan<byte> planar, int width, int height, int depth, int x, int y)
    {
        BlitRegion(planar, width, height, depth, 0, 0, width, height, x, y);
    }

    public static void BlitRegion(ReadOnlySpan<byte> planar, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, null, width, height, depth, sx, sy, w, h, x, y);
    }

    public static void BlitMasked(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, mask, width, height, depth, sx, sy, w, h, x, y);
    }

    private static void BltCommon(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        if (w <= 0 || h <= 0) return;
        fixed (byte* data = planar)
        {
            fixed (byte* m = mask)
            {
                Crt.es_sdl_blit(data, mask.Length == 0 ? null : m, width, height, depth, sx, sy, w, h, x, y, planar.Length, mask.Length);
            }
        }
    }

    public static void WaitFrames(int frames) { Frame.Wait(frames); }

    public static void WaitVBlank() { Frame.WaitVBlank(); }

    // Double buffering: a second framebuffer in the C runtime. The window shows the front one; drawing goes to the back one.
    private static VideoBuffering _sdlBuffering;

    /// <summary>Drawing goes to a back buffer that <see cref="Flip"/> shows; on SDL the two framebuffers swap (<see cref="VideoBuffering.Swap"/>).</summary>
    public static bool EnableDoubleBuffer()
    {
        if (_sdlBuffering != VideoBuffering.Single) return true;
        if (!Crt.es_sdl_dbuf_enable()) return false;
        _sdlBuffering = VideoBuffering.Swap;
        return true;
    }

    /// <summary>Double buffering with the back buffer copied to the front one at each <see cref="Flip"/> (<see cref="VideoBuffering.Copy"/>).</summary>
    public static bool EnableBackBuffer()
    {
        if (_sdlBuffering != VideoBuffering.Single) return _sdlBuffering == VideoBuffering.Copy;
        if (!Crt.es_sdl_dbuf_enable()) return false;
        _sdlBuffering = VideoBuffering.Copy;
        return true;
    }

    /// <summary>Shows the back buffer (the window is redrawn once, here) and waits for the next frame, as on the Amiga.</summary>
    public static void Flip()
    {
        if (_sdlBuffering != VideoBuffering.Single) Crt.es_sdl_flip(_sdlBuffering == VideoBuffering.Copy);
        Frame.WaitVBlank();
    }

    public static VideoBuffering Buffering() { return _sdlBuffering; }

    public static bool IsDoubleBuffered() { return _sdlBuffering != VideoBuffering.Single; }

    /// <summary>The frame on display stays and drawing goes straight to it again.</summary>
    public static void DisableDoubleBuffer()
    {
        if (_sdlBuffering == VideoBuffering.Single) return;
        Crt.es_sdl_dbuf_disable();
        _sdlBuffering = VideoBuffering.Single;
    }
#else
    private static byte[] _pixels = new byte[0];
    private static ushort[] _palette = new ushort[0];
    private static uint[] _palette24 = new uint[0];
    private static int _depthBits;

    /// <summary>Host: whether <see cref="HasAga"/> says yes. Tests set it to false to check the fallback path.</summary>
    public static bool HostAga = true;

    public static bool HasAga() { return HostAga; }

    /// <summary>Host palette as 0x00RRGGBB (what <see cref="SetColor24"/> stored; entries set with <see cref="SetColor"/> are expanded).</summary>
    public static uint[] HostPalette24 => _palette24;
    private static int _width;
    private static int _height;
    private static bool _open;

    /// <summary>Host framebuffer: one byte per pixel, row after row.</summary>
    public static byte[] HostPixels => _pixels;

    /// <summary>Host palette as 0x0RGB words.</summary>
    public static ushort[] HostPalette => _palette;

    public static int HostWidth => _width;
    public static int HostHeight => _height;

    public static bool Open(int width, int height, int depth)
    {
        if (_open || width <= 0 || height <= 0 || depth < 1 || depth > 8) return false;
        if (depth > 5 && !HostAga) return OpenRtg(width, height, depth);   // as on an Amiga without AGA: RTG if there is a card
        return OpenFrame(width, height, depth);
    }

    private static bool OpenFrame(int width, int height, int depth)
    {
        _hostBuffering = VideoBuffering.Single;
        _hostFlips = 0;
        _pixels = new byte[width * height];
        _palette = new ushort[1 << depth];
        _palette24 = new uint[1 << depth];
        _depthBits = depth;
        _width = width;
        _height = height;
        _open = true;
        return true;
    }

    /// <summary>Host: whether an RTG card is "installed" (<see cref="HasRtg"/>). Tests set it to false to check the fallback path.</summary>
    public static bool HostRtg = true;

    private static bool _rtgScreen;

    public static bool HasRtg(int width, int height) { return HostRtg && width <= 1920 && height <= 1200; }

    public static bool OpenRtg(int width, int height, int depth)
    {
        if (_open || depth < 1 || depth > 8 || !HasRtg(width, height)) return false;
        if (!OpenFrame(width, height, 8)) return false;
        _rtgScreen = true;
        return true;
    }

    public static bool IsRtg() { return _open && _rtgScreen; }

    public static bool IsOpen() { return _open; }

    /// <summary>True when the screen is 640 or more pixels wide (hires on an Amiga).</summary>
    public static bool IsHires() { return _open && _width >= 640; }

    public static void Close() { DisableDoubleBuffer(); _open = false; _rtgScreen = false; }

    public static void SetColor(int index, int r, int g, int b)
    {
        if (index >= 0 && index < _palette.Length)
        {
            _palette[index] = (ushort)(((r & 15) << 8) | ((g & 15) << 4) | (b & 15));
            _palette24[index] = (uint)((((r & 15) * 17) << 16) | (((g & 15) * 17) << 8) | ((b & 15) * 17));
        }
    }

    public static void SetColor24(int index, int r, int g, int b)
    {
        if (index < 0 || index >= _palette.Length) return;
        if (_depthBits > 5)
        {
            _palette24[index] = (uint)(((r & 255) << 16) | ((g & 255) << 8) | (b & 255));
            _palette[index] = (ushort)((((r & 255) >> 4) << 8) | (((g & 255) >> 4) << 4) | ((b & 255) >> 4));
        }
        else
        {
            SetColor(index, r >> 4, g >> 4, b >> 4);
        }
    }

    public static void SetPalette24(ReadOnlySpan<uint> rgb)
    {
        for (int i = 0; i < rgb.Length; i++)
            SetColor24(i, (int)((rgb[i] >> 16) & 255), (int)((rgb[i] >> 8) & 255), (int)(rgb[i] & 255));
    }

    public static void SetPalette(ReadOnlySpan<ushort> rgb4)
    {
        for (int i = 0; i < rgb4.Length && i < _palette.Length; i++) SetColor(i, (rgb4[i] >> 8) & 15, (rgb4[i] >> 4) & 15, rgb4[i] & 15);
    }

    public static void FillRect(int x, int y, int width, int height, int color)
    {
        for (int row = y; row < y + height; row++)
        {
            if (row < 0 || row >= _height) continue;
            for (int col = x; col < x + width; col++)
                if (col >= 0 && col < _width) _pixels[row * _width + col] = (byte)color;
        }
    }

    public static int GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return -1;
        return _pixels[y * _width + x];
    }

    public static void Blit(ReadOnlySpan<byte> planar, int width, int height, int depth, int x, int y)
    {
        BlitRegion(planar, width, height, depth, 0, 0, width, height, x, y);
    }

    public static void BlitRegion(ReadOnlySpan<byte> planar, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, ReadOnlySpan<byte>.Empty, width, height, depth, sx, sy, w, h, x, y);
    }

    public static void BlitMasked(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        BltCommon(planar, mask, width, height, depth, sx, sy, w, h, x, y);
    }

    private static void BltCommon(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y)
    {
        int bytesPerRow = ((width + 15) / 16) * 2;
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                int srcX = sx + col;
                int srcY = sy + row;
                if (srcX < 0 || srcY < 0 || srcX >= width || srcY >= height) continue;
                int bit = 0x80 >> (srcX & 7);
                if (mask.Length != 0 && (mask[srcY * bytesPerRow + (srcX >> 3)] & bit) == 0) continue;
                int value = 0;
                for (int p = 0; p < depth; p++)
                {
                    int offset = p * bytesPerRow * height + srcY * bytesPerRow + (srcX >> 3);
                    if ((planar[offset] & bit) != 0) value |= 1 << p;
                }
                int px = x + col;
                int py = y + row;
                if (px >= 0 && py >= 0 && px < _width && py < _height) _pixels[py * _width + px] = (byte)value;
            }
        }
    }

    public static void WaitFrames(int frames) { Frame.Wait(frames); }

    public static void WaitVBlank() { Frame.WaitVBlank(); }

    // Double buffering: _pixels is where drawing goes (the back buffer), _shown what the display shows.
    private static VideoBuffering _hostBuffering;
    private static byte[] _shown = new byte[0];
    private static int _hostFlips;

    /// <summary>Host: whether the system gives screen buffers. Tests set it to false to check the offscreen-bitmap fallback.</summary>
    public static bool HostScreenBuffers = true;

    /// <summary>Host: the pixels on display, one byte per pixel. Without double buffering it is <see cref="HostPixels"/>, which is where drawing goes.</summary>
    public static byte[] HostShown => _hostBuffering == VideoBuffering.Single ? _pixels : _shown;

    /// <summary>Host: how many times <see cref="Flip"/> showed a back buffer since the screen opened.</summary>
    public static int HostFlips => _hostFlips;

    /// <summary>
    /// Turns on double buffering: drawing goes to a back buffer (a copy of the screen at first) and <see cref="Flip"/> shows it.
    /// <see cref="VideoBuffering.Swap"/>, or <see cref="VideoBuffering.Copy"/> when <see cref="HostScreenBuffers"/> is false.
    /// </summary>
    public static bool EnableDoubleBuffer()
    {
        if (!_open) return false;
        if (_hostBuffering != VideoBuffering.Single) return true;
        if (!HostScreenBuffers) return EnableBackBuffer();
        StartBuffers(VideoBuffering.Swap);
        return true;
    }

    public static bool EnableBackBuffer()
    {
        if (!_open) return false;
        if (_hostBuffering != VideoBuffering.Single) return _hostBuffering == VideoBuffering.Copy;
        StartBuffers(VideoBuffering.Copy);
        return true;
    }

    private static void StartBuffers(VideoBuffering mode)
    {
        _shown = _pixels;
        _pixels = (byte[])_shown.Clone();
        _hostBuffering = mode;
    }

    /// <summary>Shows the back buffer (swapped or copied) and waits a frame (<see cref="Frame.WaitVBlank"/>).</summary>
    public static void Flip()
    {
        if (_hostBuffering == VideoBuffering.Swap)
        {
            byte[] t = _shown;
            _shown = _pixels;
            _pixels = t;
            _hostFlips++;
        }
        else if (_hostBuffering == VideoBuffering.Copy)
        {
            Array.Copy(_pixels, _shown, _pixels.Length);
            _hostFlips++;
        }
        Frame.WaitVBlank();
    }

    public static VideoBuffering Buffering() { return _hostBuffering; }

    public static bool IsDoubleBuffered() { return _hostBuffering != VideoBuffering.Single; }

    /// <summary>The frame on display stays and drawing goes straight to it again.</summary>
    public static void DisableDoubleBuffer()
    {
        if (_hostBuffering == VideoBuffering.Single) return;
        _pixels = _shown;
        _shown = new byte[0];
        _hostBuffering = VideoBuffering.Single;
    }
#endif

    /// <summary>
    /// <see cref="BlitMasked"/> with every pixel drawn as a <paramref name="scale"/> x <paramref name="scale"/> square, so the same pixel art can fill a larger
    /// screen (it opens at 2x, 3x ... the size of a game's logical screen). Runs of one colour become single FillRect calls. Scale 1 is the normal blit
    /// (the blitter, fast); larger scales are meant for machines with the fill rate to spare (AROS, RTG). An empty mask draws every pixel.
    /// </summary>
    public static void BlitMaskedScaled(ReadOnlySpan<byte> planar, ReadOnlySpan<byte> mask, int width, int height, int depth,
        int sx, int sy, int w, int h, int x, int y, int scale)
    {
        if (scale <= 1)
        {
            BlitMasked(planar, mask, width, height, depth, sx, sy, w, h, x, y);
            return;
        }
        int bytesPerRow = ((width + 15) / 16) * 2;
        for (int row = 0; row < h; row++)
        {
            int srcRow = sy + row;
            if (srcRow < 0 || srcRow >= height) continue;
            int col = 0;
            while (col < w)
            {
                int srcCol = sx + col;
                if (!SheetOpaque(mask, bytesPerRow, width, srcCol, srcRow))
                {
                    col++;
                    continue;
                }
                int value = SheetPixel(planar, bytesPerRow, height, depth, srcCol, srcRow);
                int run = 1;
                while (col + run < w
                    && SheetOpaque(mask, bytesPerRow, width, srcCol + run, srcRow)
                    && SheetPixel(planar, bytesPerRow, height, depth, srcCol + run, srcRow) == value) run++;
                FillRect(x + col * scale, y + row * scale, run * scale, scale, value);
                col += run;
            }
        }
    }

    private static bool SheetOpaque(ReadOnlySpan<byte> mask, int bytesPerRow, int width, int x, int y)
    {
        if (x < 0 || x >= width) return false;
        return mask.Length == 0 || (mask[y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }

    private static int SheetPixel(ReadOnlySpan<byte> planar, int bytesPerRow, int height, int depth, int x, int y)
    {
        int value = 0;
        for (int p = 0; p < depth; p++)
            if ((planar[p * bytesPerRow * height + y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0) value |= 1 << p;
        return value;
    }
}
