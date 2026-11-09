# Bindings spike (issue #16)

Question: can we generate the C# declarations for Amiga libraries from the AROS headers, and what
would be missing? This is a local experiment. No AROS header text and no generated bindings are
committed (see issue #18 for the licence questions); this page records facts and counts only.

## What was done

A throwaway Python script (not committed) read the AROS 9.1 SDK headers from the toolchain image
`amigadev/crosstools@sha256:9c4e978301da6b6584d68d4014caa1fa9e2689529e79e71f7190d40d1cb62e50`
(`clib/<lib>_protos.h`) for `dos`, `graphics`, `intuition` and `exec`, and compared the result with the
hand-written `[AmigaLib]` declarations in `src/ESharp.Platform`.

## What the headers give us

For every function: name, return type, argument types and names, the m68k register for each argument,
the library vector number (LVO = -6 x the number in the header), and the **library version in which it
first appeared** (each prototype sits under a `LIBAPI >= N` guard). All 160 `dos`, 183 `graphics`,
143 `intuition` and 153 `exec` functions parsed (counts equal to the number of prototypes in each header).
Functions per first-version: dos 0/36/37/39/50 = 31/120/4/2/3; graphics 0/36/39/40 = 105/25/34/19;
intuition 0/33/36/39/50 = 64/8/41/11/19; exec 0/33/36/39/45/50/51 = 84/13/22/6/11/10/7. (Version 0 means
"from the first release"; versions 45 and above are AROS additions, not AmigaOS 3.1 functions.)
This is the data the planned per-OS-version bindings need: the guard becomes a `Since(n)` on the
declaration, and a call that needs a newer library than the target OS has becomes a compile error.

All argument types in these four libraries are plain: integers, `BPTR`/`APTR`/string pointers, a few
typedefs (`RAWARG`, callback types, `DisplayInfoHandle`, `ClassID`, `Msg`, `va_list`), and 27 to 41
distinct pointer types per library. No argument is passed on the stack; all are register arguments.
Up to 11 arguments per call (graphics).

## What the headers do not give us

- **Struct layouts** are in the other headers (`graphics/gfx.h`, `intuition/intuition.h`, ...), with
  fields, alignment and 32/64-bit differences. Binding structs needs a C declaration parser, or the
  existing per-target offset tables measured with `offsetof()`.
- **Tag items** (`WA_Width`, `SA_Depth`, ...) are `#define`s and enums in the headers: easy to extract, but
  they need typed wrappers to be pleasant in C#.
- **Ownership and nullability** (what must be freed, which pointers may be NULL) are in the autodocs, not
  the headers. Wrappers must be written by hand or from annotated sources.
- **Varargs stubs** (`OpenWindowTags(...)`) are not in these headers; they come from the SDK's stub libraries.
- **AROS versus AmigaOS**: this is the AROS view. For AmigaOS 3.1/3.2 and 4 the same data comes from the
  NDK `.fd` files and headers, subject to the licence question in #18. Types differ per target
  (`LONG` is 32 bits everywhere, `BPTR`/`IPTR` follow the pointer size): the generator needs a type map per
  target, which the emitter already knows for `nint`/`nuint`.

## Finding: a wrong vector in our hand-written binding

Comparing vectors found a real bug. Of our 38 hand-written declarations in these four libraries, 37 match the
headers. `Graphics.SetChipRev` used vector -840; the AROS header and the amitools `graphics_lib.fd` both
put it at **-888** (-840 is `ObtainBestPenA`). It is fixed in this change. Behaviour in the emulator did not
change (the A1200 gives AGA yes, an A1200 with `uae_chipset = ecs` gives AGA no, with the old and the new
vector), so the wrong call returned something harmless by luck there; on other machines it could have
misreported or leaked colour pens. Nothing in the test suite could have caught it, and it passed every
emulator check. A generator, or at least a test that compares our declarations against a vector table,
would. (The comparison used one header set; the NDK's own `.fd` was not available to cross-check apart from
the amitools copy.)

## Coverage today and what a generator would add

Hand-written: 10 `dos`, 10 `graphics`, 4 `intuition`, 14 `exec` functions, plus `alib` and
`cybergraphics`; the headers define 639 functions in these four libraries.

## Proposed shape (not built)

1. A generator tool under `tools/` that reads a user-provided SDK path (AROS headers or the NDK) and
   writes `Libs/<os-version>/<lib>.g.cs` with `[AmigaLib(lib, lvo)]` and `[Since(n)]`.
2. A per-OS-version binding set (for example `amigaos-3.1`, `amigaos-3.2`, `aros`), selected by target;
   structs and tags generated with per-target layouts; hand-written wrappers on top.
3. A test that fails when a hand-written declaration disagrees with the generated vector.
4. Committing generated output only if the licence of its source allows it; otherwise generate on the
   user's machine from their own SDK copy.

## Update: tools/BindGen

`tools/BindGen` is the first piece: `check <sdk include dir> src/ESharp.Platform` compares every `[AmigaLib]`
declaration (vector and argument count) with the SDK's prototypes, `facts` lists a library's functions with
vector and first version, and `gen` writes a candidate C# binding to stdout or a file for review (coarse type
map; unknown typedefs are marked). The SDK is always the user's own copy; nothing from it is committed. Verified:
on the AROS 9.1 SDK `check` reports 38 declarations, 0 problems, and with `SetChipRev` put back at -840 it
reports exactly that one (so it would have caught the bug). `gen` output for `dos` was read, not compiled.
`[Since(n)]` is now a real attribute (`ESharp.SinceAttribute`) and `gen` writes it; the analyzer rule **ES0101** is an error when a call needs a library version newer than the target's: m68k (Kickstart 3.1) is version 40, AROS is checked against 51 (the highest in the AROS 9.1 headers), and the PowerPC targets are not checked because their library versions are not verified. Verified by three unit tests (error on m68k with both versions in the message, no error for a call within the version or a declaration never called, no error on AROS; 141 tests pass). Our hand-written declarations now carry `Since` where the AROS headers give a version above 0 (11 of 38: v36 for `PutStr`, `Flush`, `AllocVec`, `FreeVec`, `CreateMsgPort`, `DeleteMsgPort`, `GetVPModeID`, `OpenScreenTagList`, `OpenWindowTagList`; v39 for `SetRGB32`, `SetChipRev`); all are within Kickstart 3.1, and the generated C for all 18 samples on m68k and AROS is byte-for-byte unchanged. ES0101 checks calls in user code only; calls inside `ESharp.Platform` are not checked (the platform gates them itself, for example `Video.HasAga` opens graphics v39 first). Checked with a throwaway program calling `Graphics.SetChipRev` with its `Since` raised to 41: ES0101 on m68k, and none at the real value. Not done: struct layouts, tags, `exec`/`graphics`
generation beyond candidates, a unit test (needs an SDK path, so it would skip by default).

## Update: struct offsets measured, not typed in

`tools/BindGen offsets <spec> <target> --docker <image>` writes a C file of `offsetof`/`sizeof` constants for the
fields listed in `tools/BindGen/structs.txt` (our own list), compiles it to assembly with the target's own
compiler and headers (nothing is linked or run) and reads the numbers back. `tools/BindGen/layouts.sh` does this
for m68k and AROS x86-64 and writes `src/ESharp.Platform/Layout.g.cs`; `Gfx.cs` and `Sound.cs` now take their
Window, Screen, IntuiMessage, BitMap and IOAudio offsets from it instead of hand-typed tables. Verified: the
measured values equal every hand-written value on both targets (23 on m68k, 21 on AROS; the AROS table had no
`io_Device`/`io_Unit`, which nothing uses), the generated C for all 18 samples on m68k and AROS is byte-for-byte
unchanged, 141 unit tests pass. Not done: os4 and morphos (the script has their compilers but they were not run),
offsets for structs we do not read yet, a unit test (needs docker and the images).

## Update: tag lists

`ESharp.Platform.TagWriter` writes a tag list (pointer-sized tag/value pairs, ended by TAG_DONE) into a buffer
the caller owns: `t.Add(Intuition.SA_Width, width); t.Add(Intuition.SA_Quiet, true); if (t.End()) ...`. `Add`
takes `int`, `uint`, `nuint`, `bool` and pointers; a list that does not fit is never passed on half-written
(`End` returns false and leaves only TAG_DONE). `Video` builds its screen, window and RTG mode tag lists with it
instead of hand-numbered slots. Verified: a unit test compiles a tag list to C and runs it with host gcc (layout,
negative values, overflow); on the A1200 / Kickstart 3.1 emulator screen 17, aga 6, rtg 5, cards 15, rummy 28,
sound 21 ok, no FAIL (same as before); on hosted AROS (nightly 20261106) cards 16, rummy 31, screen 17 ok, no
FAIL; 142 unit tests pass. Not done: typed tag names (tags are still `uint` constants, so a wrong value type for a
tag is not caught), tags for other libraries, generating the tag constants from an SDK.

## Update: which libraries next, and where the facts come from

**Where each library lives on the minimum system.** I listed the resident modules of the Kickstart 3.1 (40.069)
A1200 ROM (read in place, RomTag scan) and the `Libs:` directory of the Workbench 3.1 (40.42) disk (read-only):

- In ROM, always there: exec, dos, graphics, intuition, layers, utility, keymap, gadtools, icon, workbench,
  expansion, mathffp and mathieeesingbas. The devices in ROM are timer, audio, gameport, input, keyboard and console.
- On disk, in `Libs:`, present only if installed: lowlevel, asl, diskfont, iffparse, datatypes, locale,
  commodities, amigaguide, bullet, realtime, nonvolatile, rexxsyslib, rexxsupport, the mathieee double and trans
  libraries and mathtrans.

This matters for the generator. libnix opens every library a program references at startup and refuses to start
if one is missing. A binding for a disk library therefore needs the optional-base pattern that cybergraphics
already uses (`CyberGfx.SetBase`), generalised. The ROM libraries can keep libnix's automatic open.

**The fd files are the better source of first versions for the classic OS.** The m68k toolchain ships the
classic function-description files (`ndk/lib/fd/*_lib.fd`), which carry "functions in Vnn or higher" markers.
BindGen now reads them (`Fd.cs`), and wherever it takes an SDK include directory it also accepts an fd directory.
`bindgen compare <aros-include> <fd-dir> <lib...>` lists the differences, and `gen ... --fd <fd-dir>` uses the fd
vectors and versions with the AROS argument types. Across the 19 libraries below, the comparison found:

- **AROS gives versions that would block working 3.1 calls:** all of datatypes is marked 45 (it is 40 in the fd
  file), and asl's AbortAslRequest and ActivateAslRequest are marked 50 (36 in the fd file). With the AROS
  numbers, ES0101 would reject these calls on 3.1.
- **Version differences that do not change anything on 3.1:** iffparse says 33 against the fd file's 36, and five
  dos functions and lowlevel's SetJoyPortAttrsA differ by one to three versions. Both sides are 40 or lower, or the
  difference stays on the AROS side of v40.
- **Vector differences:** only intuition's ShowWindow and HideWindow differ, and both are 3.2 (v47) additions, not
  3.1.
- **Present on one side only:** functions that only AROS has (for example timer GetUpTime and exec's Child*
  calls), the 3.2 outline-font engine in diskfont that AROS does not have, and private slots.
- **Our own bindings:** all 27 of our declarations match the fd files (`bindgen check <fd-dir> src/ESharp.Platform`).

So for m68k, Since should come from the fd files, and AROS keeps its own headers.

**Candidates, by use to a game.** Columns are the library, where it lives on 3.1, and its public functions at v40
or lower out of the total in the fd file.

| library | 3.1 | ≤v40 / all | why |
| --- | --- | --- | --- |
| lowlevel | disk | 15 / 15 | joystick and CD32 pad (ReadJoyPort), keyboard and VBlank interrupts, ElapsedTime |
| timer (device) | ROM | 5 / 5 | frame timing: ReadEClock, GetSysTime |
| keymap | ROM | 4 / 4 | MapRawKey: raw key to character, for text input |
| utility | ROM | 38 / 43 | tag-item helpers, hooks, 32-bit multiply/divide |
| diskfont | disk | 5 / 17 | OpenDiskFont: fonts other than topaz |
| iffparse | disk | 40 / 40 | read and write ILBM and 8SVX at run time (asset loading, saves) |
| asl | disk | 8 / 8 | file and screen-mode requesters (mode picker for RTG) |
| datatypes | disk | 15 / 23 | load any picture or sound the system understands |
| icon | ROM | 17 / 28 | tool types from the program's .info (settings) |
| gadtools | ROM | 19 / 21 | system menus and gadgets, for tools rather than games |
| locale | disk | 24 / 24 | translated strings (catalogs) |
| layers, commodities, realtime, nonvolatile | | | not needed for games now |

AHI (sound on RTG and PPC systems) is third-party, and its header does not use the AROS macro form BindGen
parses. It needs its own look. Presence on AmigaOS 4 and MorphOS is not verified for any of these libraries.

## Update: lowlevel and timer bound, optional library bases

These are the first two libraries from the list above. `[LibraryBase(lib, base, type)]` replaces the cybergraphics special case: the class's calls go through a base variable of our own, which `SetBase` fills in. The usual base name is renamed with a `#define` before the proto header, so libnix's startup library list and AROS's autoinit never see it, and a missing disk library cannot stop the program from starting. `bindgen offsets` now also measures header constants (`Name = value MACRO`, written as `uint`), so the joystick bits and the timer unit come from the toolchain headers rather than from memory. See STATUS.md, "Joystick and clock", for the runs.

## Update: keymap bound

keymap.library (v36, in ROM) is the third. It uses `[LibraryBase]` too, even though it is in ROM, so that the startup code does not need to know about it. `MapRawKey` and `MapANSI` vectors come from the fd file, checked against it and against AROS (`check`: 0 problems for keymap). The InputEvent and IntuiMessage fields it needs, the qualifier bits and `IECLASS_RAWKEY` are measured into Layout.g.cs. The run found a real bug before it shipped: the InputEvent buffer was sized as `nuint[(size + 7) / 8]`, which is 12 bytes on m68k (a `nuint` is 4 bytes there), not 22, so `MapRawKey`'s event overwrote the stack and the next `OpenScreen` hung. It is now sized in 4-byte words. A buffer sized from a Layout value must divide by the element size of the target, not by 8. See STATUS.md, "Keyboard text: keymap.library".

## Update: utility bound

utility.library (v39, in ROM) is the fourth: dates (`Amiga2Date`, `Date2Amiga`, `CheckDate`) and case-insensitive text (`Stricmp`, `Strnicmp`, `ToUpper`, `ToLower`), plus dos.library `DateStamp`. Its 32/64-bit multiply and divide functions are not bound, because the 68020 and gcc do that work. The tag, hook and named-object functions are not bound either, because E# code has no use for them yet. The one finding: hosted AROS's `CheckDate` accepts 29 February 2001, so `Date.TryJoin` checks a date by splitting it back. See STATUS.md, "Dates and case-insensitive text".

## Update: diskfont, iffparse and asl bound

The three disk libraries that need `[LibraryBase]` (all in `Libs:` on the Workbench disk, not in ROM) are bound: `DiskFontLib` (`OpenDiskFont`, v34), `IffParse` (11 of 40 functions: `AllocIFF`, `OpenIFF`, `ParseIFF`, `CloseIFF`, `FreeIFF`, `ReadChunkBytes`, `WriteChunkBytes`, `PushChunk`, `PopChunk`, `CurrentChunk`, `InitIFFasDOS`; v36) and `AslLib` (`AllocAslRequest`, `FreeAslRequest`, `AslRequest`; v36). Vectors and versions come from the classic fd files: `bindgen check <fd dir> src/ESharp.Platform` gives 0 problems for 35 declarations, and against the AROS headers only the old `alib.BeginIO` note remains. `compare` shows the known version differences (iffparse 33 in AROS against 36, asl Abort/Activate 50 against 36), which do not matter for the functions bound here. Struct offsets and constants (TextAttr, TextFont, IFFHandle `iff_Stream`, ContextNode, FileRequester `fr_File` and `fr_Drawer`, the ASL tags, `ID_FORM`, the IFF error codes) are in `structs.txt` and Layout.g.cs. One tool fix came with it: `bindgen offsets` stored every value plus 1 so no constant would be zero, which made `-1` (`IFFERR_EOF`, `IFFSIZE_UNKNOWN`) print as `?`; it now XORs with a fixed mask instead. The existing numbers did not change. graphics gained `OpenFont`, `CloseFont`, `SetFont`, `Text`, `TextLength` and `Move`. The wrappers are `Fonts`, `Iff` and `Asl` (see STATUS.md). Not looked at: the remaining 29 iffparse functions (handlers, collections, clipboard), `AvailFonts` and the 3.9/3.2 diskfont additions, the other ASL requesters (font, screen mode), and AmigaOS 4 and MorphOS.

## Not verified

Everything on the Amiga side beyond the `SetChipRev` runs in the emulator (A1200, Kickstart 3.1) above; the
parser was checked by counting prototypes, not by compiling the generated declarations; the type map and the
`Since` attribute are design, not code; 3.2 and AmigaOS 4 differences were not looked at.
