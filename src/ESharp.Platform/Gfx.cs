#if AMIGA
namespace ESharp.Platform;

/// <summary>intuition.library bindings (v39 or older). Pointers are opaque: Screen and Window structures are read with fixed offsets in <see cref="Video"/>.</summary>
public static unsafe class Intuition
{
    [AmigaLib("intuition", -612), Since(36)] public static extern void* OpenScreenTagList(void* newScreen, void* tagList);
    [AmigaLib("intuition", -606), Since(36)] public static extern void* OpenWindowTagList(void* newWindow, void* tagList);
    [AmigaLib("intuition", -66)] public static extern int CloseScreen(void* screen);
    [AmigaLib("intuition", -72)] public static extern void CloseWindow(void* window);
    /// <summary>Locks a public screen by name (null: the default one, normally Workbench) so it cannot close; release it with <see cref="UnlockPubScreen"/>.</summary>
    [AmigaLib("intuition", -510), Since(36)] public static extern void* LockPubScreen(void* name);
    [AmigaLib("intuition", -516), Since(36)] public static extern void UnlockPubScreen(void* name, void* screen);
    /// <summary>intuition.library AllocScreenBuffer() (v39): a struct ScreenBuffer for double buffering a screen (SB_SCREEN_BITMAP: the screen's own bitmap; SB_COPY_BITMAP: a new one with a copy of it), or null.</summary>
    [AmigaLib("intuition", -768), Since(39)] public static extern void* AllocScreenBuffer(void* screen, void* bitMap, uint flags);
    /// <summary>intuition.library FreeScreenBuffer() (v39). The buffer must not be on display and its messages must have arrived.</summary>
    [AmigaLib("intuition", -774), Since(39)] public static extern void FreeScreenBuffer(void* screen, void* screenBuffer);
    /// <summary>intuition.library ChangeScreenBuffer() (v39): shows the buffer's bitmap from the next frame; 0 if Intuition could not do it now (try again).</summary>
    [AmigaLib("intuition", -780), Since(39)] public static extern uint ChangeScreenBuffer(void* screen, void* screenBuffer);

    // BOOPSI (v36). Vectors from the classic fd file. A null classPtr with a classId names a public class ("propgclass").
    /// <summary>intuition.library NewObjectA(): a new BOOPSI object of a private class (<paramref name="classPtr"/>) or a public one by name, or null.</summary>
    [AmigaLib("intuition", -636), Since(36)] public static extern void* NewObjectA(void* classPtr, byte* classId, void* tagList);
    /// <summary>intuition.library DisposeObject(): disposes a BOOPSI object (a gadget must be out of any window first).</summary>
    [AmigaLib("intuition", -642), Since(36)] public static extern void DisposeObject(void* obj);
    /// <summary>intuition.library SetAttrsA(): sets attributes; non-zero if the object needs a refresh. Gadgets in a window use <see cref="SetGadgetAttrsA"/>.</summary>
    [AmigaLib("intuition", -648), Since(36)] public static extern nuint SetAttrsA(void* obj, void* tagList);
    /// <summary>intuition.library GetAttr(): stores one attribute (pointer-sized slot); 0 if the class does not know it.</summary>
    [AmigaLib("intuition", -654), Since(36)] public static extern nuint GetAttr(uint attrId, void* obj, void* storage);
    /// <summary>intuition.library SetGadgetAttrsA(): sets attributes of a gadget that may be in a window, so it can redraw itself.</summary>
    [AmigaLib("intuition", -660), Since(36)] public static extern nuint SetGadgetAttrsA(void* gadget, void* window, void* requester, void* tagList);
    /// <summary>intuition.library AddGList(): adds <paramref name="numGad"/> linked gadgets (-1: all) at a position (-1: the end); the position used.</summary>
    [AmigaLib("intuition", -438)] public static extern ushort AddGList(void* window, void* gadget, uint position, int numGad, void* requester);
    /// <summary>intuition.library RemoveGList(): takes gadgets out of a window again; their old position.</summary>
    [AmigaLib("intuition", -444)] public static extern ushort RemoveGList(void* window, void* gadget, int numGad);
    /// <summary>intuition.library RefreshGList(): redraws <paramref name="numGad"/> gadgets starting at <paramref name="gadgets"/>.</summary>
    [AmigaLib("intuition", -432)] public static extern void RefreshGList(void* gadgets, void* window, void* requester, int numGad);
    /// <summary>intuition.library DrawImage(): draws an image (also a BOOPSI image) at its own position plus the offsets.</summary>
    [AmigaLib("intuition", -114)] public static extern void DrawImage(void* rastPort, void* image, int leftOffset, int topOffset);
    /// <summary>intuition.library GetScreenDrawInfo() (v36): the screen's pens and font for gadgets that draw text (GA_DrawInfo).</summary>
    [AmigaLib("intuition", -690), Since(36)] public static extern void* GetScreenDrawInfo(void* screen);
    /// <summary>intuition.library FreeScreenDrawInfo() (v36).</summary>
    [AmigaLib("intuition", -696), Since(36)] public static extern void FreeScreenDrawInfo(void* screen, void* drawInfo);

    public const uint TagDone = 0;
    public const uint SA_Width = 0x80000023, SA_Height = 0x80000024, SA_Depth = 0x80000025;
    public const uint SA_DisplayID = 0x80000032, SA_Quiet = 0x80000038, SA_ShowTitle = 0x80000036, SA_Type = 0x8000002d;
    public const uint WA_Left = 0x80000064, WA_Top = 0x80000065, WA_Width = 0x80000066, WA_Height = 0x80000067;
    public const uint WA_CustomScreen = 0x80000070, WA_Borderless = 0x80000088, WA_Backdrop = 0x80000085;
    public const uint WA_Activate = 0x80000089, WA_RMBTrap = 0x8000008a, WA_SimpleRefresh = 0x8000008c;
    public const uint WA_NoCareRefresh = 0x80000087, WA_IDCMP = 0x8000006a, WA_ReportMouse = 0x80000086;
    public const uint IDCMP_MOUSEBUTTONS = 0x8, IDCMP_MOUSEMOVE = 0x10, IDCMP_RAWKEY = 0x400;
    public const uint CustomScreen = 0xf;

    // Offsets in struct Window, Screen and IntuiMessage, and sizeof(struct BitMap): measured per target (Layout.g.cs).
    public const int WindowRPort = Layout.WindowRPort, ScreenViewPort = Layout.ScreenViewPort, WindowUserPort = Layout.WindowUserPort;
    public const int MsgClass = Layout.MsgClass, MsgCode = Layout.MsgCode, MsgMouseX = Layout.MsgMouseX, MsgMouseY = Layout.MsgMouseY;
    /// <summary>struct BitMap: BytesPerRow, Rows (words), Flags, Depth (bytes), a pad word, then the plane pointers from offset 8.</summary>
    public const int BitMapSize = Layout.BitMapSize;
}

/// <summary>graphics.library bindings (v39 or older).</summary>
public static unsafe class Graphics
{
    [AmigaLib("graphics", -288)] public static extern void SetRGB4(void* viewPort, int index, int r, int g, int b);
    /// <summary>graphics.library SetRGB32() (v39, AGA machines): components are 32-bit left-justified (0xRR000000, or 0xRRRRRRRR to repeat a byte).</summary>
    [AmigaLib("graphics", -852), Since(39)] public static extern void SetRGB32(void* viewPort, uint index, uint r, uint g, uint b);
    /// <summary>
    /// graphics.library SetChipRev() (v39): turns on the best chip set the machine has and returns the bits now enabled
    /// (GFXF_AA_ALICE 4, GFXF_AA_LISA 8, GFXF_HR_DENISE 2, GFXF_HR_AGNUS 1). The system boots with the AGA modes off until
    /// something asks for them.
    /// </summary>
    [AmigaLib("graphics", -888), Since(39)] public static extern uint SetChipRev(uint want);
    [AmigaLib("graphics", -342)] public static extern void SetAPen(void* rastPort, int pen);
    [AmigaLib("graphics", -306)] public static extern void RectFill(void* rastPort, int xMin, int yMin, int xMax, int yMax);
    [AmigaLib("graphics", -318)] public static extern int ReadPixel(void* rastPort, int x, int y);
    /// <summary>graphics.library SetDrMd(): JAM1 (0) draws only the set bits of a template or text in the A pen, JAM2 (1) also the others in the B pen.</summary>
    [AmigaLib("graphics", -354)] public static extern void SetDrMd(void* rastPort, int drawMode);
    /// <summary>graphics.library BltTemplate(): draws a one-plane template (word aligned, chip RAM) in the rast port's pen and draw mode.</summary>
    [AmigaLib("graphics", -36)] public static extern void BltTemplate(byte* source, int xSrc, int srcModulo, void* destRastPort, int xDest, int yDest, int xSize, int ySize);
    /// <summary>graphics.library WaitBlit(): returns when the blitter is idle (blitter calls can return while the blit still runs).</summary>
    [AmigaLib("graphics", -228)] public static extern void WaitBlit();
    [AmigaLib("graphics", -606)] public static extern void BltBitMapRastPort(void* srcBitMap, int xSrc, int ySrc,
        void* destRastPort, int xDest, int yDest, int xSize, int ySize, int minterm);
    [AmigaLib("graphics", -636)] public static extern void BltMaskBitMapRastPort(void* srcBitMap, int xSrc, int ySrc,
        void* destRastPort, int xDest, int yDest, int xSize, int ySize, uint minterm, void* bltMask);
    /// <summary>graphics.library OpenFont(): a font that is already in memory (ROM topaz, or one loaded earlier), or null.</summary>
    [AmigaLib("graphics", -72)] public static extern void* OpenFont(void* textAttr);
    [AmigaLib("graphics", -78)] public static extern void CloseFont(void* textFont);
    [AmigaLib("graphics", -66)] public static extern void SetFont(void* rastPort, void* textFont);
    [AmigaLib("graphics", -60)] public static extern void Text(void* rastPort, string text, int count);
    [AmigaLib("graphics", -54)] public static extern int TextLength(void* rastPort, string text, int count);
    [AmigaLib("graphics", -240)] public static extern void Move(void* rastPort, int x, int y);
    [AmigaLib("graphics", -270)] public static extern void WaitTOF();
    /// <summary>graphics.library InitRastPort(): a rast port with default pens and modes and no layer (drawing through it is not clipped).</summary>
    [AmigaLib("graphics", -198)] public static extern void InitRastPort(void* rastPort);
    /// <summary>graphics.library AllocBitMap() (v39): a bitmap like <paramref name="friendBitMap"/> (display memory on a graphics card), or null.</summary>
    [AmigaLib("graphics", -918), Since(39)] public static extern void* AllocBitMap(uint width, uint height, uint depth, uint flags, void* friendBitMap);
    /// <summary>graphics.library FreeBitMap() (v39). Call <see cref="WaitBlit"/> first if a blit may still use it.</summary>
    [AmigaLib("graphics", -924), Since(39)] public static extern void FreeBitMap(void* bitMap);
    /// <summary>graphics.library BltBitMap(): copies a rectangle between two bitmaps; the number of planes copied.</summary>
    [AmigaLib("graphics", -30)] public static extern int BltBitMap(void* srcBitMap, int xSrc, int ySrc, void* destBitMap, int xDest, int yDest,
        int xSize, int ySize, uint minterm, uint mask, void* tempA);
    /// <summary>graphics.library GetVPModeID() (v36): the display mode ID of a view port.</summary>
    [AmigaLib("graphics", -792), Since(36)] public static extern uint GetVPModeID(void* viewPort);
    /// <summary>graphics.library BestModeIDA() (v39): the display mode that best fits the BIDTAG_ tags, or INVALID_ID.</summary>
    [AmigaLib("graphics", -1050), Since(39)] public static extern uint BestModeIDA(void* tags);
    /// <summary>graphics.library FindDisplayInfo() (v36): a handle for <see cref="GetDisplayInfoData"/>.</summary>
    [AmigaLib("graphics", -726), Since(36)] public static extern void* FindDisplayInfo(uint displayId);
    /// <summary>graphics.library GetDisplayInfoData() (v36): copies the record <paramref name="tagId"/> (DTAG_DIMS, ...) of a mode; the bytes copied, 0 on failure.</summary>
    [AmigaLib("graphics", -756), Since(36)] public static extern uint GetDisplayInfoData(void* handle, byte* buffer, uint size, uint tagId, uint displayId);
}

/// <summary>
/// cybergraphics.library bindings (v40): the retargetable graphics (RTG) API that Picasso96, CyberGraphX and the AmigaOS 4 and
/// MorphOS graphics drivers all provide. The library is absent on a stock Amiga, so <see cref="Video"/> opens it first and only then calls these.
/// </summary>
[LibraryBase("cybergraphics", "CyberGfxBase", "struct Library")]
public static unsafe class CyberGfx
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    /// <summary>Emitter intrinsic: true if a library base is set. On AmigaOS 4 SetBase leaves it null when the library's "main" interface cannot be obtained, so callers check this after SetBase.</summary>
    public static extern bool Ready();

    [AmigaLib("cybergraphics", -54)] public static extern uint IsCyberModeID(uint displayId);
    [AmigaLib("cybergraphics", -60)] public static extern uint BestCModeIDTagList(void* tags);

    public const uint CYBRBIDTG_Depth = 0x80050000, CYBRBIDTG_NominalWidth = 0x80050001, CYBRBIDTG_NominalHeight = 0x80050002;
    public const uint InvalidId = 0xFFFFFFFFu;
}
#endif
