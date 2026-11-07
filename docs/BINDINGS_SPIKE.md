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

## Not verified

Everything on the Amiga side beyond the `SetChipRev` runs in the emulator (A1200, Kickstart 3.1) above; the
parser was checked by counting prototypes, not by compiling the generated declarations; the type map and the
`Since` attribute are design, not code; 3.2 and AmigaOS 4 differences were not looked at.
