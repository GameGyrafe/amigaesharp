#if AMIGA
namespace ESharp.Platform;

/// <summary>intuition.library bindings (v37). Pointers are opaque: Screen and Window structures are read with fixed offsets in <see cref="Video"/>.</summary>
public static unsafe class Intuition
{
    [AmigaLib("intuition", -612)] public static extern void* OpenScreenTagList(void* newScreen, void* tagList);
    [AmigaLib("intuition", -606)] public static extern void* OpenWindowTagList(void* newWindow, void* tagList);
    [AmigaLib("intuition", -66)] public static extern int CloseScreen(void* screen);
    [AmigaLib("intuition", -72)] public static extern void CloseWindow(void* window);

    public const uint TagDone = 0;
    public const uint SA_Width = 0x80000023, SA_Height = 0x80000024, SA_Depth = 0x80000025;
    public const uint SA_DisplayID = 0x80000032, SA_Quiet = 0x80000038, SA_ShowTitle = 0x80000036, SA_Type = 0x8000002d;
    public const uint WA_Left = 0x80000064, WA_Top = 0x80000065, WA_Width = 0x80000066, WA_Height = 0x80000067;
    public const uint WA_CustomScreen = 0x80000070, WA_Borderless = 0x80000088, WA_Backdrop = 0x80000085;
    public const uint WA_Activate = 0x80000089, WA_RMBTrap = 0x8000008a, WA_SimpleRefresh = 0x8000008c;
    public const uint WA_NoCareRefresh = 0x80000087, WA_IDCMP = 0x8000006a, WA_ReportMouse = 0x80000086;
    public const uint IDCMP_MOUSEBUTTONS = 0x8, IDCMP_MOUSEMOVE = 0x10, IDCMP_RAWKEY = 0x400;
    public const uint CustomScreen = 0xf;

    /// <summary>Offsets in struct Window and struct Screen (checked against the NDK headers).</summary>
    public const int WindowRPort = 50, ScreenViewPort = 44, WindowUserPort = 86;
    /// <summary>Offsets in struct IntuiMessage.</summary>
    public const int MsgClass = 20, MsgCode = 24, MsgMouseX = 32, MsgMouseY = 34;
}

/// <summary>graphics.library bindings (v37).</summary>
public static unsafe class Graphics
{
    [AmigaLib("graphics", -288)] public static extern void SetRGB4(void* viewPort, int index, int r, int g, int b);
    /// <summary>graphics.library SetRGB32() (v39, AGA machines): components are 32-bit left-justified (0xRR000000, or 0xRRRRRRRR to repeat a byte).</summary>
    [AmigaLib("graphics", -852)] public static extern void SetRGB32(void* viewPort, uint index, uint r, uint g, uint b);
    /// <summary>
    /// graphics.library SetChipRev() (v39): turns on the best chip set the machine has and returns the bits now enabled
    /// (GFXF_AA_ALICE 4, GFXF_AA_LISA 8, GFXF_HR_DENISE 2, GFXF_HR_AGNUS 1). The system boots with the AGA modes off until
    /// something asks for them.
    /// </summary>
    [AmigaLib("graphics", -840)] public static extern uint SetChipRev(uint want);
    [AmigaLib("graphics", -342)] public static extern void SetAPen(void* rastPort, int pen);
    [AmigaLib("graphics", -306)] public static extern int RectFill(void* rastPort, int xMin, int yMin, int xMax, int yMax);
    [AmigaLib("graphics", -318)] public static extern int ReadPixel(void* rastPort, int x, int y);
    [AmigaLib("graphics", -606)] public static extern int BltBitMapRastPort(void* srcBitMap, int xSrc, int ySrc,
        void* destRastPort, int xDest, int yDest, int xSize, int ySize, int minterm);
    [AmigaLib("graphics", -636)] public static extern void BltMaskBitMapRastPort(void* srcBitMap, int xSrc, int ySrc,
        void* destRastPort, int xDest, int yDest, int xSize, int ySize, uint minterm, void* bltMask);
    [AmigaLib("graphics", -270)] public static extern void WaitTOF();
    /// <summary>graphics.library GetVPModeID() (v36): the display mode ID of a view port.</summary>
    [AmigaLib("graphics", -792)] public static extern uint GetVPModeID(void* viewPort);
}

/// <summary>
/// cybergraphics.library bindings (v40): the retargetable graphics (RTG) API that Picasso96, CyberGraphX and the AmigaOS 4 and
/// MorphOS graphics drivers all provide. The library is absent on a stock Amiga, so <see cref="Video"/> opens it first and only then calls these.
/// </summary>
public static unsafe class CyberGfx
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("cybergraphics", -54)] public static extern uint IsCyberModeID(uint displayId);
    [AmigaLib("cybergraphics", -60)] public static extern uint BestCModeIDTagList(void* tags);

    public const uint CYBRBIDTG_Depth = 0x80050000, CYBRBIDTG_NominalWidth = 0x80050001, CYBRBIDTG_NominalHeight = 0x80050002;
    public const uint InvalidId = 0xFFFFFFFFu;
}
#endif
