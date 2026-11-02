using System;

namespace ESharp.Platform;

/// <summary>
/// A single full-screen indexed-colour display. On the Amiga it opens a quiet custom Intuition screen
/// with a borderless backdrop window (system friendly: no hardware takeover) and draws with the
/// graphics.library blitter calls. On the host it is a software framebuffer so game logic can be
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

    /// <summary>Opens a screen of <paramref name="width"/> x <paramref name="height"/> with <paramref name="depth"/> bitplanes (1..5 on OCS). False on failure.</summary>
    public static bool Open(int width, int height, int depth)
    {
        if (_screen != null) return false;
        uint[] tags = new uint[24];
        tags[0] = Intuition.SA_Width; tags[1] = (uint)width;
        tags[2] = Intuition.SA_Height; tags[3] = (uint)height;
        tags[4] = Intuition.SA_Depth; tags[5] = (uint)depth;
        tags[6] = Intuition.SA_Quiet; tags[7] = 1;
        tags[8] = Intuition.SA_Type; tags[9] = Intuition.CustomScreen;
        tags[10] = Intuition.TagDone;
        fixed (uint* p = tags)
        {
            _screen = Intuition.OpenScreenTagList(null, p);
        }
        if (_screen == null) return false;

        uint[] wtags = new uint[26];
        wtags[0] = Intuition.WA_CustomScreen; wtags[1] = (uint)_screen;
        wtags[2] = Intuition.WA_Left; wtags[3] = 0;
        wtags[4] = Intuition.WA_Top; wtags[5] = 0;
        wtags[6] = Intuition.WA_Width; wtags[7] = (uint)width;
        wtags[8] = Intuition.WA_Height; wtags[9] = (uint)height;
        wtags[10] = Intuition.WA_Borderless; wtags[11] = 1;
        wtags[12] = Intuition.WA_Backdrop; wtags[13] = 1;
        wtags[14] = Intuition.WA_Activate; wtags[15] = 1;
        wtags[16] = Intuition.WA_RMBTrap; wtags[17] = 1;
        wtags[18] = Intuition.WA_NoCareRefresh; wtags[19] = 1;
        wtags[20] = Intuition.WA_IDCMP; wtags[21] = Intuition.IDCMP_MOUSEBUTTONS | Intuition.IDCMP_MOUSEMOVE | Intuition.IDCMP_RAWKEY;
        wtags[22] = Intuition.WA_ReportMouse; wtags[23] = 1;
        wtags[24] = Intuition.TagDone;
        fixed (uint* wp = wtags)
        {
            _window = Intuition.OpenWindowTagList(null, wp);
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
        uint[] bm = new uint[10];                         // struct BitMap: 40 bytes
        bm[0] = ((uint)bytesPerRow << 16) | (uint)height;  // BytesPerRow, Rows
        // Planes the image does not have stay NULL: the blitter reads them as zeros, so every plane of the
        // screen gets written (a shallower bitmap would leave the remaining planes untouched).
        bm[1] = (uint)_depth << 16;                        // Flags, Depth, pad
        fixed (byte* data = planar)
        {
            for (int p = 0; p < depth; p++)
                bm[2 + p] = (uint)(data + p * bytesPerRow * height);
            fixed (uint* b = bm)
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

    /// <summary>Waits for <paramref name="frames"/> video frames (50 per second on PAL).</summary>
    public static void WaitFrames(int frames)
    {
        Dos.Delay(frames);
    }
#else
    private static byte[] _pixels = new byte[0];
    private static ushort[] _palette = new ushort[0];
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
        _palette = new ushort[1 << depth];
        _width = width;
        _height = height;
        _open = true;
        return true;
    }

    public static bool IsOpen() { return _open; }

    public static void Close() { _open = false; }

    public static void SetColor(int index, int r, int g, int b)
    {
        if (index >= 0 && index < _palette.Length)
            _palette[index] = (ushort)(((r & 15) << 8) | ((g & 15) << 4) | (b & 15));
    }

    public static void SetPalette(ReadOnlySpan<ushort> rgb4)
    {
        for (int i = 0; i < rgb4.Length && i < _palette.Length; i++) _palette[i] = (ushort)(rgb4[i] & 0xFFF);
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

    public static void WaitFrames(int frames) { }
#endif
}
