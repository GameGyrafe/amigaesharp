using System;

namespace ESharp.Platform;

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
    /// </summary>
    public static bool Open(int width, int height, int depth)
    {
        if (_screen != null) return false;
        // Kickstart 2.x opens a screen with more bitplanes than the chips have and then misbehaves: refuse it here.
        if (depth > 5 && !HasAga()) return false;
        return OpenMode(width, height, depth, 0);
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
        nuint[] tags = new nuint[8];      // tag lists are arrays of pointer-sized pairs
        uint id = CyberGfx.InvalidId;
        fixed (nuint* p = tags)
        {
            var t = new TagWriter(p, 8);
            t.Add(CyberGfx.CYBRBIDTG_Depth, 8);
            t.Add(CyberGfx.CYBRBIDTG_NominalWidth, width);
            t.Add(CyberGfx.CYBRBIDTG_NominalHeight, height);
            if (t.End()) id = CyberGfx.BestCModeIDTagList(p);
        }
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
        nuint[] tags = new nuint[8];
        fixed (nuint* p = tags)
        {
            var t = new TagWriter(p, 8);
            t.Add(Layout.BidNominalWidth, width);
            t.Add(Layout.BidNominalHeight, height);
            t.Add(Layout.BidDepth, depth);
            if (t.End()) id = Graphics.BestModeIDA(p);
        }
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
        nuint[] tags = new nuint[24];
        fixed (nuint* p = tags)
        {
            var t = new TagWriter(p, 24);
            t.Add(Intuition.SA_Width, screenWidth);
            t.Add(Intuition.SA_Height, screenHeight);
            t.Add(Intuition.SA_Depth, depth);
            t.Add(Intuition.SA_Quiet, true);
            t.Add(Intuition.SA_Type, Intuition.CustomScreen);
            if (displayId != 0) t.Add(Intuition.SA_DisplayID, displayId);   // native: let the system choose
            if (t.End()) _screen = Intuition.OpenScreenTagList(null, p);
        }
        if (_screen == null) return false;

        nuint[] wtags = new nuint[26];
        fixed (nuint* wp = wtags)
        {
            var w = new TagWriter(wp, 26);
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
            if (w.End()) _window = Intuition.OpenWindowTagList(null, wp);
        }
        if (_window == null)
        {
            Intuition.CloseScreen(_screen);
            _screen = null;
            return false;
        }
        _rastPort = *(void**)((byte*)_window + Intuition.WindowRPort);
        _viewPort = (byte*)_screen + Intuition.ScreenViewPort;
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
        if (_window != null) Intuition.CloseWindow(_window);
        if (_screen != null) Intuition.CloseScreen(_screen);
        Keys.Close();
        _window = null;
        _screen = null;
        _rastPort = null;
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
        if (width <= 0 || height <= 0) return;
        Graphics.SetAPen(_rastPort, color);
        Graphics.RectFill(_rastPort, x, y, x + width - 1, y + height - 1);
    }

    /// <summary>The palette index at a pixel, or -1 outside the screen.</summary>
    public static int GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return -1;
        return Graphics.ReadPixel(_rastPort, x, y);
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
        if (w <= 0 || h <= 0) return;
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
#elif SDL
    // SDL: a chunky 8-bit framebuffer and palette in the C runtime, shown in a window (integer scaled) at every
    // Frame.WaitVBlank and Dos.Delay. Drawing works on the framebuffer like the blitter works on the Amiga screen.

    /// <summary>SDL shows 256 colours on any desktop, so the AGA depths (6 to 8 bitplanes) are always available.</summary>
    public static bool HasAga() { return true; }

    public static bool Open(int width, int height, int depth) { return Crt.es_sdl_video_open(width, height, depth); }

    /// <summary>There is no RTG card on SDL: false. Use <see cref="Open"/>, which takes up to 8 bitplanes (256 colours).</summary>
    public static bool HasRtg(int width, int height) { return false; }

    public static bool OpenRtg(int width, int height, int depth) { return false; }

    public static bool IsRtg() { return false; }

    public static bool IsOpen() { return Crt.es_sdl_video_is_open(); }

    public static void Close()
    {
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
        _pixels = new byte[width * height];
        if (depth > 5 && !HostAga) return false;      // as on an OCS machine
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
        if (!Open(width, height, 8)) return false;
        _rtgScreen = true;
        return true;
    }

    public static bool IsRtg() { return _open && _rtgScreen; }

    public static bool IsOpen() { return _open; }

    public static void Close() { _open = false; _rtgScreen = false; }

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
