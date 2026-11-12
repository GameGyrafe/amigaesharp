# Status

## Minimum system: Amiga 1200 (decision)

The minimum system is now an **Amiga 1200: 68020, AGA, 2 MB chip RAM, Kickstart 3.1, hard disk install**. Classic builds use `-m68020` (target table, smoke test), the CLI defaults to `--model A1200`, and the docs and `AGENTS.md` say so. Planned beyond classic 68k: PowerPC (AmigaOS 4, MorphOS), RTG cards and AROS. Support for A500/A600, 68000 and Kickstart 1.x/2.x is dropped. Older "Kickstart 2.04 / A500+" results below are historical: they were true when run and are not part of the matrix any more.
- Cleanup done: the host fake's default chip budget is 2 MB (also in the host-gcc exec shim), and comments and `docs/MEMORY.md` no longer describe a stock A500. 136 unit tests pass.
- **68020 instructions used on purpose:** `Fix * Fix` is now `(a * b) >> 16` through a 64-bit product, which gcc turns into a single `muls.l` with a 64-bit result (checked in the compiled output: no helper call); the 68000 four-partial-product version is gone. `Fix / Fix` calls a prelude function `es_fixdiv` (new `[Native]` attribute for extern methods the emitter defines itself) that uses one `divs.l` (64/32) when the quotient fits in 32 bits and the software 64-bit divide when it does not; division by zero gives 0 on the Amiga and throws on the host. Verified: all 225 pairs of 15 edge values (negative, `int.MinValue`, quotients that overflow) match an independent exact-integer reference on Kickstart 3.1 (A1200), the same computation passes under host gcc (the portable path) in a unit test, 137 unit tests pass, and the 17-sample A1200 regression gives 300 `ok` with no `FAIL`. Not verified: speed (no cycle measurement was made, so the gain is expected, not measured), 68030/040/060, and `divs.l` on real hardware.
- **AROS (m68k) via FS-UAE's built-in AROS ROM replacement, `--model A1200`, no Kickstart:** the m68k builds run unchanged (same hunk executables). 15 of 17 samples print only `ok` lines (hello, phase2, memory, generics, properties, operators, language, cards, rummy, sound, collections, rtg, inherit, lambdas, linq). **2 checks fail, both pixel read-backs in graphics:** `samples/aga` "the blitted pixels have the 24-bit colours of the image" and `samples/screen` "blitted card matches the planar data pixel for pixel". Investigated for `screen`: on the AROS ROM replacement a blit leaves a destination pixel unchanged when the source pixel's palette index is 0, as if index 0 were transparent (every wrong pixel had expected index 0 and kept the old background colour; the other 5000+ pixels were right; it happens at word-aligned and unaligned x and at different rows). Giving the blit zero-filled planes instead of NULL planes for the missing depth did not change it, so it is not the NULL-plane convention. `aga` fails on the same kind of pixel (not separately confirmed). This looks like a quirk of that AROS version's `BltBitMapRastPort` (real AmigaOS copies index 0 like any colour), not a bug in E#; no workaround was added. A real AROS install was not tried. The ROM replacement is not a real AROS install: native AROS (hosted or x86/ARM, new toolchain image) is not built or run, and nothing was run on real AROS hardware.
- **AROS x86-64 target (`--target aros`, new):** defines `AMIGA`, `AROS`, `X86_64`; compiler `x86_64-aros-gcc` 9.1.0 from the toolchain image `amigadev/crosstools@sha256:9c4e978301da6b6584d68d4014caa1fa9e2689529e79e71f7190d40d1cb62e50` (about 5.4 GB on its own; it is now also a stage of the combined `esharp-amiga-toolchain` image from `docker/Dockerfile`, which is 5.66 GB with all four compilers; `docker/smoke-test.sh` builds an x86-64 ELF with it, and `linq` and `cards` were built with `--target aros --docker esharp-amiga-toolchain`; running those builds on hosted AROS from the combined image was not repeated), with `--sysroot=/opt/x86_64-aros`. All 17 samples compile to AROS ELF executables. **Run on hosted AROS (x86-64, nightly 20261104, `linux-x86_64-system` archive, run headless under xvfb with a patched `S:Startup-Sequence`; output to a file):** all 11 pure-computation samples ran: hello, phase2 (32 ok), generics, operators, properties, collections (20 ok), inherit (24 ok), language (28 ok), lambdas (16 ok), linq (37 ok), and memory (18 ok, 1 FAIL: "chip block is in the chip address range", which assumes 32-bit chip addresses). The first run exposed a real bug: `string.Length` was emitted as `__builtin_strlen`, which became an undefined `strlen` symbol that the AROS loader resolved wrongly (garbage length, crash in `Hashing_Text`). It is now an emitted `es_strlen` helper, libc-free on every target. A booted AROS x86-64 ISO under QEMU (software emulation) reached the Workbench desktop, but no E# program was run there. Not verified: the screen, sound, file and input samples (see below), real AROS hardware, and hosted AROS i386. **64-bit pointer port (done):** tag lists are pointer-sized (`nuint[]`; the emitter now maps `nint`/`nuint` to `intptr_t`/`uintptr_t`), file handles are `nuint`, the BitMap is built with typed writes, and struct offsets (Window, Screen, IntuiMessage, IOAudio) have a 64-bit set under `#if X86_64`, measured with `offsetof()` against the AROS headers. Pool items are 8-byte aligned. On hosted AROS x86-64: `screen` passes completely (including the card blit pixel for pixel, which fails on the m68k ROM replacement), `aga` passes its fallback checks, `rtg` passes except "the native screen is not RTG" (every AROS screen is RTG, so that check cannot hold there), file loading in `sound` passes (write, load, delete), `memory` passes with its 32-bit chip-address check skipped on AROS. **Higher resolution on AROS (cards, rummy):** after the 320x200 self-checks the games open a 640x400 screen. On an RTG mode they use a second asset set, drawn at that size and not enlarged: `tools/CardArt --hd` draws 64x80 cards with vector shapes (anti-aliased suits, stroke-drawn rank letters, court cards, pip layouts, card back, arrows) and a smoothed copy of the 5x7 font, edges baked against the table or button colour because the blit mask is one bit. The set is one 896x496 sheet (56 colours, 24-bit palette) generated once into `samples/shared/cards-hd/Assets.g.cs` under `#if AROS` and used by both games through an `esharp.include` file (the CLI adds the listed directories to the sample's sources) (new manifest key `"if"`), so the m68k builds do not carry it. Without an RTG mode they fall back to the 32x40 art drawn 2x2 (`Video.BlitMaskedScaled`, unit-tested). Seen working with scripted input on hosted AROS. `cards` now checks the drawn 64x80 card against the sheet pixel for pixel (it passes on hosted AROS, nightly 20261106; the 20261104 nightly is gone from SourceForge); `rummy` does the same for a normal glyph, a big glyph and the card back (all pass); the other glyphs, the court cards and the full rummy screen were reviewed by eye only. The A1200 / Kickstart 3.1 regression is unchanged (300 ok, no FAIL). The one generated source is about 2.5 MB. **Sound on AROS (works):** the earlier hang was the host's ALSA setup, not E# or AROS itself. AROS's `audio.device` is AHI-based; on a headless host with no usable sound card its ALSA mode hangs (in `OpenDevice`, or in the first write when the ALSA config is only half valid; a `ctl null` line is not a real ALSA plugin). With AHI's `void` mode as the only audio mode (it takes samples and paces them in real time, no card needed; `tools/aros-hosted/aros-hosted.sh` sets this up in its scratch copy of the archive) `sound` passes completely on hosted AROS (21 ok, no FAIL): writes finish after the right time, looping voices keep playing and two voices play together. One platform change was needed: AROS's `audio.device` shares its channels, so four requests for "any one channel" got units 1, 2, 1, 2 and a later request stole an earlier one's channel (its writes then failed with ADIOERR_NOALLOCATION); on AROS `Sound.Open` now asks for one fixed channel per voice (`#if AROS`; the Amiga path is unchanged and `sound` on the A1200 / Kickstart 3.1 still gives 21 ok). Real audible output on AROS (a real card) was not tried. The 64-bit IOAudio offsets are right (open, key and unit read back correctly), `cards` and `rummy` run on hosted AROS under Xvfb with scripted XTEST input (cards: three clicks on the higher/lower buttons gave three right guesses; rummy: a click on the stock drew a card, 36 left) and their self-checks pass (rummy 27 ok); screenshots taken with `xwd` look right. The harness (Xvfb, a patched `S:Startup-Sequence`, a ctypes XTEST helper) is `tools/aros-hosted/aros-hosted.sh` (re-run on the 20261106 nightly with `cards`). gcc still warns about incompatible pointer types and BPTR integer conversions (no behaviour change). The A1200 / Kickstart 3.1 regression after the port gives the same 300 `ok` lines and no FAIL as before. A 32-bit AROS (i386) toolchain was not found as an image.
- **Kept on purpose:** `HasAga()` and `HasRtg()` with their fallbacks. A classic A1200 always has AGA, but PowerPC, RTG-only and AROS targets may not, so the checks stay as the portable way to ask. **Not yet done:** using 68020 instructions deliberately and AROS (a new target, untested).
- Verified after the change: all 17 samples built with `-m68020` and run on Kickstart 3.1 (A1200) in FS-UAE: 300 `ok` lines, no `FAIL` (the script shows only the last 25 lines of each sample, so long ones are truncated), 136 unit tests pass. Not verified: PowerPC, real hardware, a 68000/Kickstart 2.04 run (no longer supported).

## Binding generator: `bindgen gen` and `--check` (issue #17)

`tools/BindGen gen <fd dir> <lib...> [--include <dir>]` now writes C# bindings in the style of `src/ESharp.Platform`: one `public static unsafe class <Lib>Library` per library (a name the hand-written classes do not use), `[AmigaLib(lib, vector), Since(n)] public static extern ...` for every public vector in vector order, private (`##private`) and reserved slots skipped while still counting their vectors, `Since` from the fd file's version markers, `[LibraryBase]` with the fd file's base name and a `SetBase` for every library except exec, dos, graphics and intuition (which the startup code opens), `--docs` for one-line XML docs (vector, version, argument registers), `--namespace`, `--class`, `--out <file or dir>`. Parameters keep the prototype order, because the emitter calls through the target's proto header, which puts each argument in its register. Types come from `clib/<lib>_protos.h` (the NDK form or the AROS macro form): struct pointers are `void*` (`void**` for a pointer to one), `STRPTR` is `byte*`, `CONST_STRPTR` and other read-only text `string`, `BPTR`/`IPTR` `nuint`, `BOOL` `int`, tag lists `void*`, `ULONG *` and similar stay typed; an unknown typedef or a function without a prototype is written as `nuint /* ... */` under a "check the marked type" line. Varargs stubs (`...Tags`) are not vectors and are left out.

`gen ... --check src/ESharp.Platform` (and `check`, which now does the same) compares every hand-written `[AmigaLib]` declaration with the generated one: name, vector, argument count, `Since` and each type. A difference that changes the call is a problem (exit 1): pointer against integer, a result truncated or invented (`int` for a `VOID` function), a `string` where the library writes, `int` for a `BPTR`, a `Since` that is wrong across the v40 line. A widening, a different signedness or `void*` for a typed pointer is a note.

`bindgen layout <aros> <m68k> [out]` writes Layout.g.cs from the two measured lists (`layouts.sh` now calls it instead of sed, and it refuses lists that measured different items).

Bindings fixed from the check: `Graphics.RectFill`, `Graphics.BltBitMapRastPort` and `Exec.AbortIO` returned `int` but the prototypes say `VOID` (a C# caller using the result would have produced C that does not compile; nothing used it), `Exec.AvailMem` is now `nuint` because AROS x86-64 returns an `IPTR` (`Mem.AvailChip`/`AvailFast` saturate at 4 GB - 1 instead of wrapping), and `DiskFontLib.OpenDiskFont` lost its `Since(34)` (the fd file gives no version: it is in the first diskfont). The generated C of 30 samples (m68k, aros, os4, emit-only) is byte-identical to origin/main except the four that use `Mem.AvailChip` (collections, generics, hwinfo, memory), where only the saturation was added; cards, rummy and shared were not in that comparison (copied out of the repo they lose `../shared`).

Verified:
- `bindgen gen <fd> exec dos graphics intuition gadtools lowlevel keymap utility diskfont iffparse asl timer --check src/ESharp.Platform` with the m68k toolchain's fd files: before the fixes (origin/main) 3 problems with the NDK prototypes (`--include` the image's `ndk-include`, NDK 3.2 headers: AbortIO, RectFill, BltBitMapRastPort) and 3 with the AROS headers (RectFill, BltBitMapRastPort, the `IPTR` result of AvailMem; AROS declares AbortIO `LONG`), after: 93 declarations, 0 problems (21 notes with the NDK, 39 with AROS). `alib` and `cybergraphics` have no fd file and are skipped.
- 374 unit tests pass, 2 skipped (the OS4 VM test and the SDK test below). New: generation from an invented library's fd and prototype text (written for the test, no SDK text), the type map, the check's problem and note cases, Layout.g.cs written back byte for byte from its own numbers (and every structs.txt item present for both targets), a host-gcc run of generated bindings against a C shim with `-Wall -Werror` (argument order and types, results back), and C# errors for using the result of the three `VOID` functions. The SDK test (`ESHARP_SDK=<fd dir>`, optional `ESHARP_SDK_INCLUDE=<include dir>`) passes with the NDK and with the AROS headers: no problems, and the generated classes for the 12 libraries compile through the emitter.
- All generated bindings for the 12 libraries compiled by the target compilers: a throwaway program calling every typed declaration (696 calls with the NDK types, `--target m68k`; 690 with the AROS types, `--target aros`; versions above 40 / 51 left out because ES0101 rejects them, as it should) builds with no errors.
- A1200, Kickstart 3.1 (40.069), FS-UAE: a throwaway program using only generated `ExecLibrary`, `DosLibrary` and `UtilityLibrary` printed 10 ok, no FAIL (AvailMem, FindTask, OpenLibrary, SMult32, UDivMod32, Strnicmp, Stricmp, ToUpper, DateStamp, Delay). Samples after the binding fixes (`esharp run --model A1200 --kickstart`): language 39 ok, screen 17, cards 15, fonts 7 (no LIBS:, as before), controls 9, memory 19, sound 21, no FAIL; iff and asl print "not installed" without LIBS: (as before; their bindings did not change).
- Hosted AROS x86-64 (nightly 20261106): memory 19 ok, screen 17, language 39, fonts 8, iff 10, no FAIL. `memory`, `screen` and `sound` build for os4.
- `layouts.sh` re-run: Layout.g.cs unchanged.

Not verified / not done:
- Typed struct pointers and a `TagItem` type: the generator writes `void*`, as the hand-written bindings do, because E# has no C# declarations of the system structs (their fields come from Layout.g.cs). A pointer to a struct pointer is `void**` in C#, which C compilers accept with an incompatible-pointer warning (an error with gcc 14 and later); that is the same as the existing `GadTools.CreateContext`. Generating struct declarations is a design question for Dave.
- The generated classes are not committed and the hand-written ones were not replaced by them; `Since` for AmigaOS 4 and MorphOS, their SDKs, and the `iffparse`/`asl`/`diskfont` samples with LIBS: (the Workbench-disk setup) were not run for this change; SDL was not run (the changed code is under `#if AMIGA`).

## SDL 3 target `sdl` (issue #15)

**The same game builds for the Amiga and for a desktop through SDL 3.** `--target sdl` defines `SDL` only (no `AMIGA`, no CPU or OS
symbol); the compiler is `gcc -O2 -fwrapv` in the `esharp-sdl` image and SDL 3 is linked statically (`pkg-config --static --cflags --libs
sdl3`, run under `sh -c`; `--docker` without a name picks `esharp-sdl` for this target). Platform code is now `#if AMIGA` / `#elif SDL`
/ `#else` (the dotnet host fake); the Amiga and host branches are unchanged.

- **C runtime for non-Amiga targets:** `src/ESharp.Compiler/CRuntime/es_std.h` (stdio/stdlib/time only) and `es_sdl.h` (SDL 3) are
  embedded in the compiler and pasted into the generated C when a `[Native]` extern of `src/ESharp.Platform/Crt.cs` named `es_std_*` or
  `es_sdl_*` is called, so a program that only prints needs neither SDL nor any Amiga header (no library bases are opened).
- **Platform on SDL:** `Dos` (PutStr to stdout, FlushOutput, Delay = SDL_Delay after showing the screen), `Mem`/`Heap`/`Arena`/`Pool`/`Vec`
  on calloc (no chip RAM; each kind counts against a nominal 256 MB, so `AvailChip`/`AvailFast` move with allocations and an impossible
  request returns null as on the Amiga), `Files` (stdio), `Date` (C clock and time zone; Split/TryJoin in code), `Strings` (the host's
  ISO-8859-1 rules), `Clock` (SDL_GetTicksNS, 1 MHz), `Frame` (a steady 50 fps like PAL: each `WaitVBlank` shows the screen and paces with
  SDL_DelayPrecise; vsync is not used), `Video` (a chunky 8-bit framebuffer and 256-entry palette in C, converted to an ARGB8888
  streaming texture on present, drawn integer-scaled (`SDL_LOGICAL_PRESENTATION_INTEGER_SCALE`, nearest) into a resizable window as large
  as fits the desktop, `ESHARP_SCALE=n` to choose; open/close, SetColor/SetColor24 (24 bits only above 5 planes, as on the Amiga),
  palettes, FillRect, GetPixel, planar Blit/BlitRegion/BlitMasked/BlitMaskedScaled, the built-in `Font`), `Input` (mouse in screen pixels,
  left button, keys mapped to the Amiga raw codes of the same US positions with the qualifier bits, closing the window is Esc and a second
  close exits; the `Input.Host*` calls feed the C layer too, so the games' input self-checks run on SDL), `Keys` (the host's US layout),
  `Sound` (four SDL audio streams on the default device, signed 8-bit mono at 3546895 / period, gain = volume / 64, fed from a callback for
  cycles and loops), `Trace` (standard error).
- **Not on SDL (Amiga-only):** `Gui`/GadTools, `Asl`, `Iff`/iffparse, `Fonts`/diskfont, `Joystick`/lowlevel and the raw bindings (`Exec`,
  `Intuition`, `Graphics`, `CyberGfx`, `Utility`, `Keymap`, `TimerDevice`, `Layout`) are not compiled for SDL: using one is the C# unknown-name
  error plus **ES0102** "'Gui' is Amiga-only and does not exist on target 'sdl'; put the code in #if AMIGA". `HasAga()` is true (256
  colours always work), `HasRtg`/`OpenRtg`/`IsRtg` are false (no RTG card: programs take their fallback path). `samples/controls`, `gui`,
  `asl`, `iff`, `fonts` therefore do not build for sdl.
- **CLI:** `esharp run --target sdl [--docker [esharp-sdl]] [--headless]`: headless runs the binary inside the image (or on this machine
  without `--docker`) with `SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy`; without `--headless` the static binary runs on this desktop.
  `--input` takes the FS-UAE syntax (`click X,Y;rel DX,DY;press;key N[+M]`, plus `close` on this target: a window close request followed by SDL's quit event, which counts as one Esc, X11 key codes) with X,Y in the game's screen pixels; the CLI
  turns the key codes into Amiga raw keys and passes the script in `ESHARP_INPUT`; the program replays one step per poll (with gaps) after
  it prints SHOT. `--screenshot` saves the program's own frame (its screen size, not the scaled window): the program writes a PPM
  (`ESHARP_SCREENSHOT`) once the script has played (or when the screen closes) and the CLI converts it to PNG with the ESharp.Assets writer.
  `esharp test --target sdl --docker esharp-sdl` runs the host and the SDL build (headless) and compares the ok/FAIL lines like the m68k path.
- **Emitter fix on the way:** `fixed (byte* p = readOnlySpan)` now casts the span's const pointer, which removes the
  `-Wdiscarded-qualifiers` warnings that every build using the blitter had (also on m68k: `samples/cards` now builds without them).

Verified (Linux dev host, esharp-sdl image with SDL 3.4.20):
- `esharp test --target sdl --docker esharp-sdl` (host vs SDL headless in the image), host and SDL agree on every check: phase2 32,
  language 35, collections 20, generics 24, lambdas 16, linq 37, refs 18, inherit 24, operators 20, properties 18, names 12, dates 19,
  keys 8, timing 10, memory 19, aga 6, assets 17, screen 17, sound 21 (voices busy while playing and free after, through the dummy audio
  driver), **cards 20** and **rummy 36** (both including their scripted input checks). `rtg` differs on purpose: the host fake has a card,
  SDL has none, so SDL runs the 5 fallback checks (all ok). `hello` and `trace --trace` print as on the host.
- **Gate (same game on Amiga and SDL):** `esharp run samples/cards --target sdl --docker esharp-sdl --headless --input "click 60,145;key 9"
  --screenshot ...`: the self-checks pass, then `click 60 145`, `guess higher: right`, `quit`; the PNG shows the 320x200 table with the
  history row of four cards, the current card, the deck, both buttons and the streak pips. `samples/rummy` with `--input "click
  128,80;click 92,154"`: `player discarded`, `bot moved`; the PNG shows the bot's seven backs, stock 35, the pile, the player's seven cards,
  "YOUR TURN: DRAW FROM THE STOCK OR THE PILE" and "DEADWOOD 47". `samples/screen` screenshot shows its rectangles, the blitted card and
  the font line.
- **On a desktop (Xvfb on the dev host, outside docker):** the binary built in the image (dynamically linked to glibc only; SDL is inside)
  opened a 960x600 window (scale 3) centred on a 1280x1024 Xvfb screen, captured with `xwd`, showing the game. Driven by real X events
  (XTEST: a click at the Higher button in window pixels, then the Esc key): `click 60 145`, `guess higher: right`, `quit`, so the window
  scaling and mouse mapping are right. `esharp run --target sdl --docker esharp-sdl` without `--headless` (scripted input and screenshot)
  also worked there.
- 281 unit tests pass (new `SdlTargetTests`: target symbols, `#if SDL`, standard-C-only output for a print program, the SDL runtime only
  when Video is used, ES0102, the standard C runtime (memory, files, dates, keys) compiled with `-Wall -Werror` and run with the host gcc,
  collections and inherit on sdl with the dotnet output, the `--input` translation, the PPM reader, the "SDL" label in the comparison,
  and, when the `esharp-sdl` image exists, a program built with static SDL and run headless in it: drawing, a scripted click and Esc, a
  4-voice sound open, and the screenshot's pixels).
- Regression (Amiga branches unchanged): on A1200 / Kickstart 3.1 (40.069) in FS-UAE `cards` 15 ok (input: `click 120 120`, `click 80
  140`, `guess higher: right`, `quit`), `rummy` 28 ok (`click 120 120`, `click 120 80`, `quit`), `screen` 17 ok, `sound` 21 ok, `esharp test
  samples/language --target m68k`: host and Amiga agree (35 + 35); `cards` built with `--target aros` on hosted AROS (nightly 20261106):
  16 ok, a scripted click played a guess, the screenshot shows the high resolution table.

Not verified:
- A real desktop session (a physical X11 or Wayland display, a real sound card): only Xvfb and the dummy audio driver were used, so no
  sound was heard. Keyboard input from a real keyboard (only XTEST's Esc key and the scripted keys).
- Native macOS and Windows builds (no toolchain for them here; the C is plain C99 plus SDL 3, and `--docker` builds a Linux binary only),
  other CPU architectures, a newer or older SDL 3 than 3.4.20.
- The GitHub CI `sdl` job (builds the image, smoke test, then hello, `esharp test` language and cards, and a cards run with input and a
  screenshot): not run on GitHub yet.
- `ES0100` ("slow 64-bit arithmetic on a 68020") is no longer reported when compiling for sdl (the compiler drops it; an IDE analyzer cannot know the target, so the squiggle stays there).

### SDL 3 build image (part 1)

`docker/sdl/Dockerfile` builds the image `esharp-sdl`: Debian bookworm-slim pinned by digest, gcc/cmake/pkg-config, and SDL 3.4.20 built from the pinned release tarball (sha256 checked) as a static library with the X11, Wayland, ALSA, PulseAudio, DRM and GL/EGL backends (dlopened at run time). About 877 MB. `docker/sdl/smoke-test.sh` links a C program statically (`pkg-config --static sdl3`) and runs it with the offscreen video and dummy audio drivers: window, renderer, streaming texture, pixel read-back and a played audio stream all pass on the Linux dev host. CI has an `sdl` job that builds the image and runs the smoke test (the CI job itself not yet run on GitHub).

## Clang-clean C

The generated C builds without warnings under clang `-Wall -Werror` as well as gcc: no doubled parentheses around if/while/for conditions
(`-Wparentheses-equality`; an assignment used as a condition keeps exactly one pair, for `-Wparentheses`), and `x ??= y` as a statement no
longer yields an unused value. The C tests honour `ESHARP_TEST_CC` (default `gcc`). Verified: the whole suite passes with gcc on Linux
(269), with Ubuntu clang 18.1.3 in a container (268, before the assignment test was added), and on macOS 27 / Apple Silicon where `gcc` is
Apple clang 21.0.0 (269, run by Dave; this is where the warnings were found); CI repeats the run with clang; samples/language agrees on the
A1200 emulator.

## String equality, null-forgiving `!`, base/derived comparisons (issue #49, part of #12)

- **`==` and `!=` on strings compare the characters**, as in C# (before, the C compared the addresses: a silent miscompile). They call an emitted helper `es_streq` (our own loop, no libc `strcmp`, for the same AROS reason as `es_strlen`): null equals null, null never equals a string (not even `""`). A comparison with the null constant (`s == null`, `s != default`) stays a pointer test. The same applies to `string.Equals(a, b)` and `a.Equals(b)` (a null receiver gives false, not an exception), `switch` statements on strings (each label is matched in order, then a C `switch` on the label's number, so `break` and `default` behave as before; `case null` works), string constant patterns (`s is "x"`, `"a" or "b"`, switch expression arms, property patterns on string fields), `==` inside lambdas and LINQ lambdas, and LINQ `Contains` on string arrays. `Hashing.SameText` (used by `StringMap`/`StringSet`) is now just `a == b`.
- **`x!` emits only `x`** wherever it appears: member access (`_last!.Next`), calls and receivers (also of struct methods), arguments including `ref`/`out`, assignments. An implicit conversion that applies to `x!` or `(x)` (class to interface, array to span, a user-defined `implicit operator` such as `Fix f = n!;`) is applied once (the first version applied it twice there, which gcc rejected).
- **`s is K` with a named constant** (`const string K = "abc"`) is a constant pattern: strings compare by characters (`s is Nothing` with a null constant is a pointer test), numbers, chars, bools and enums of the constant's own type compare with `==`; other subjects give `ES0099`. Before, it took the class type-test path and produced C that gcc rejected.
- **A base-typed and a derived-typed class reference compare as untyped pointers** (`(const void *)a == (const void *)b`), so gcc no longer warns about distinct pointer types. Interface values against class references already compared the object pointers.
- **New on the way:** `a ?? b` on class, string, interface and delegate values (a is evaluated once, b only when a is null; derived classes are cast to the result type) and `x ??= y` on a variable or field of those types. Before, both were copied into the C and failed in gcc.
- **`ES0099` instead of wrong or broken C:** `==` on tuples, records (any `record` declaration; before it crashed the emitter), `string.Equals` with an `object` argument, a string constant pattern on a value not typed `string`, LINQ `Min`/`Max` of strings or structs, LINQ `Contains` on struct elements, `??` and `??=` on nullable value types. A `ReadOnlySpan<string>` now gets a valid C type (it gave `const const char **`, which gcc rejects with `-Werror`).
- Workarounds removed: `samples/gui` compares the gadget texts with `==` (its byte-compare helper is gone), `GtWindow.Add` uses `_last` directly, `samples/language` uses `==` instead of its `Same` helper.
- Verified: 266 unit tests pass (new: host-gcc `-Wall -Werror` compile-and-run tests for string equality with equal contents at different addresses, null cases and literals; `switch`, switch expressions and patterns on strings; `==` in LINQ and delegate lambdas and `Contains`; `!` in member access, receivers, arguments, `ref`/`out`, assignments; base/derived compares, `?:`, `??`, `??=` and interface compares; `ES0099` for the forms above). `samples/language` has 7 new checks (35 in all): host `esharp test` 35 ok; m68k on A1200 / Kickstart 3.1 (40.069) in FS-UAE 35 ok, no `FAIL`, no gcc warnings; `--target aros` on hosted AROS x86-64 (nightly 20261106) 35 ok, no `FAIL`. `samples/gui` on the host: 7 ok. Review fixes: 268 unit tests pass (new compile-and-run tests: `!` and parentheses where a class-to-interface, array-to-span, derived-to-base or `Fix` implicit conversion applies, `Names!.Contains`, `null!` and `(null)` as an interface; `s is K`, `s is not K`, null constant, int and enum named constants), and `esharp test samples/language` on A1200 / Kickstart 3.1 (40.069) in FS-UAE again gave 35 ok, host and Amiga agree (hosted AROS not re-run for the review fixes: they change only which C is emitted, not anything word-size dependent). m68k builds: `samples/gui` went from 2 gcc warnings (distinct pointer types at `e.Source == hello`) to 0; `samples/cards` has the same 4 warnings before and after (const qualifiers, unrelated).
- Not verified: `samples/gui` and `samples/cards` were only built for m68k after the change, not run in FS-UAE; the hardware test pack was not rebuilt; PowerPC; real hardware. String `switch` with `goto case` (not supported by the emitter in general).

## `esharp test` (issue #8)

`esharp test <dir>` builds the project's `.cs` files into a temporary dotnet project (`obj/host`, `HOST` defined, referencing `ESharp.Target.Host`), runs it, and counts output lines starting `ok ` and `FAIL `. A FAIL line, a non-zero exit or no ok/FAIL lines at all fails the run. With `--target m68k --docker <image> [--kickstart <rom>] [--model A1200]` it also builds for m68k, runs headless in FS-UAE (the `run` code) and lists every check that is ok on one side and FAIL or missing on the other. It needs the E# source tree (found from the CLI's location, or `ESHARP_ROOT`; `ESHARP_REPO` still works as a deprecated alias). Passing `--docker`, `--kickstart`, `--model` or `--fsuae` without `--target m68k` prints a warning that they are ignored.

`skip NAME` lines (new, issue #57): a program prints one for a check it cannot run on this machine (no working sound hardware, an emulator that is not real time). A skip is neither ok nor FAIL, the summary counts it (`OS4: 13 ok, 0 FAIL, 8 skipped`), and a check that one side skipped is not a difference. `samples/sound`, `samples/assets` and `samples/timing` use it; a FAIL on the other side, or a check missing without a skip line, still is a difference (`TestReportTests`).

Verified:
- 154 unit tests pass, including parsing and comparison (`TestReportTests`).
- `esharp test samples/controls` on the host: 12 ok, 0 FAIL, exit 0.
- `esharp test samples/linq --target m68k` with the m68k image and Kickstart 3.1 (40.069) on A1200: host 37 ok, Amiga 37 ok, no differences, exit 0.

Not verified:
- A failing program through the CLI (exit code 1 on FAIL or a difference): covered by unit tests of the comparison only, not run end to end.
- Other targets (morphos, aros) are not supported by `test` (`--target sdl` and `--target os4` are, see their sections).

## VS Code support (issue #20)

`esharp new <name>` now writes `Program.cs`, `<name>.csproj`, `.gitignore` and `.vscode/{tasks,launch,extensions}.json` (`src/ESharp.Cli/ProjectTemplate.cs`). The csproj references `ESharp.Target.Host` (runtime, platform, host fakes, `HOST` defined) and `ESharp.Analyzers` as an analyzer (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`), defining `HOST` like `esharp test` does, using an `ESharpRoot` property that `esharp new` fills in from the CLI's location (or `ESHARP_ROOT`; one shared lookup, `SourceTree.Find`, also used by `esharp test`). `esharp new games/demo` creates `games/demo/` with project name `demo`; a name that is not a valid C# identifier (`my-game`, a keyword) is rejected with a suggestion (`my_game`). Tasks call `dotnet <path to esharp.dll> build|run ...` (nothing has to be on PATH; the docker image is a `${input:dockerImage}` prompt defaulting to the pinned m68k image) and `dotnet run --project`. `launch.json` is a coreclr launch of the host build.

- Verified (in a scratch directory, from the shell): `esharp new` output; the JSON files parse; `dotnet build` and `dotnet run` of the new project print `Hello from E#!`; a deliberate violation gives an `ES` error from `dotnet build` (see the PR for the output); the task command lines run. Unit tests check that the files are valid JSON with the expected tasks, the name rules, and `esharp new sub/dir` through the CLI.
- **Verified on macOS (Dave, 2026-11-10; macOS 27, Apple Silicon, .NET 10 SDK, VS Code with the C# extension):** `dotnet test` (269 passed after the clang fix), `esharp new mygame` from a checkout, the project loads in VS Code, the analyzer shows ES0001 live while typing (squiggle and hover), the tasks "check subset and emit C" (error in the Problems panel; then `obj/m68k/mygame.c`), "run on the host" (`Hello from E#!`), "build for the host", F5 "debug on the host (.NET)" (breakpoint hit, step over), and "build (m68k)" through Docker Desktop (pulled the pinned m68k image under emulation, `Built .../obj/m68k/mygame`). Clicking a task error in Problems to jump to the line was not reported back.
- Not verified: the editor on Windows and Linux, a CLI installed away from a checkout (the root cannot be found then without `ESHARP_ROOT`), and no NuGet package for the runtime exists yet.

## Joystick and clock: lowlevel.library and timer.device (samples/controls)

`Joystick` (lowlevel.library v40) reads a joystick or CD32 pad: `Read(port)` returns direction and button bits (`Up`, `Fire`, `Fire2`, the CD32 colour and transport buttons), `Direction(port)` gives x and y as -1, 0 or 1, `Kind(port)` says what is plugged in, and `Force(port, gamepad)` sets the port type. A mouse port reads 0, because a mouse's low bits are counters. `Clock` (timer.device) reads the E clock: `Ticks`, `Frequency`, `Microseconds(from, to)` and `Milliseconds(from, to)`. On the host both are fakes: `Joystick.HostSet`, and `Clock.HostAdvance` (`Dos.Delay` advances it too).

lowlevel.library is on the Workbench disk, not in ROM. A program that uses it must therefore start without it, so `[LibraryBase]` is new: a class with that attribute calls through a base variable of its own, which `SetBase` fills in. The usual base name (`LowLevelBase`, `TimerBase`) is renamed in the generated C, so neither libnix nor AROS's autoinit tries to open the library. cybergraphics now uses the same mechanism instead of its special case. The joystick bits, timer unit and struct sizes are measured from the toolchain headers (`value` lines in `tools/BindGen/structs.txt`), and the vectors come from the classic fd files.

Verified:
- 147 unit tests pass. They include the base mechanism compiled with host gcc and run, the sample on the host, and the m68k C output (no `LowLevelBase` or `TimerBase` left for the startup code).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, with `LIBS:` assigned to the Workbench 3.1 disk (read-only): 12 ok, no FAIL. The E clock runs at 709379 ticks per second, `Delay(10)` measures about 200 ms, and forcing port 1 to a pad and back to a joystick reads back.
- The same machine without `Libs:`: the program starts, with 9 ok and every joystick read 0.
- Hosted AROS (nightly 20261106): no FAIL. The clock runs at 200 ticks per second there, and AROS's lowlevel refuses a forced port type (`Force` returns false), which the sample reports rather than fails on.
- Regression after the cybergraphics change, on the A1200 / 3.1: rtg 5, aga 6, screen 17 and cards 15 ok. Cards on hosted AROS: 16 ok.

Not verified:
- Real stick or pad input. No buttons were pressed in the emulator: the bits are measured from the headers, but no reading of a pressed button was checked.
- A real CD32 pad, and real hardware in general.
- AmigaOS 4 and MorphOS.

## Frame timing (samples/timing, issue #13, timing part)

`Frame` (src/ESharp.Platform/Frame.cs) is the game-loop timing API. `Frame.WaitVBlank()` is graphics.library `WaitTOF`, so drawing syncs to the display, and counts frames in `Frame.Count` (`ResetCount()`). `Frame.FramesPerSecond` is 50 or 60 (`IsPal`, `FrameMicroseconds`), read once from the PAL or NTSC bit of `GfxBase.DisplayFlags`, falling back to `ExecBase.VBlankFrequency` when neither is set (offsets and flags measured into `Layout.g.cs` for m68k and AROS x86-64). `FrameTimer(stepsPerSecond, maxSteps)` is a fixed-timestep helper over `Clock`: `Begin()`, then `Update()` each frame returns the logic steps owed, capped at `maxSteps` with the backlog dropped so a stall cannot spiral; `ElapsedMicroseconds`/`ElapsedMilliseconds` since `Begin`, and `Fraction` (0..1000) for interpolation. `Video.WaitVBlank()` is a shortcut, and `Video.WaitFrames(n)` is now n `WaitTOF` calls on the Amiga (it was `Dos.Delay(n)`, 50 Hz ticks, which is wrong on NTSC and not synced to the display). Host fake: `WaitVBlank` advances the fake `Clock` by one frame (20000 or 16666 microseconds; `Frame.HostPal` + `Frame.Redetect()` pick the rate), so tests drive game loops deterministically; the host `Video.WaitFrames` used to do nothing and now advances the fake clock too.

**WaitTOF that does not wait (issue #57):** on AmigaOS 4 in QEMU, `WaitTOF` returns at once on the emulated sm501 display ("50 WaitVBlank calls took 0 ms"). `Frame.WaitVBlank` now reads the E clock after `WaitTOF` (its own timer.device request, `UNIT_MICROHZ`, opened on the first call); if it returned less than half a frame after the previous `WaitVBlank`, `WaitTOF` did not wait; and if `WaitTOF` itself took under a tenth of a frame on three calls in a row, the display is marked broken for good (`_tofBroken`, cleared by `[AtExit]`) and every later frame is padded to a whole frame from the previous return, so a game with 0.5 to 1 frame of work per frame still runs at the display rate, and a `TR_ADDREQUEST` (`DoIO`) waits out the rest of the frame. `Frame.PacedFrames` counts those waits (always 0 on a working display, on SDL and on the host). On a working display consecutive returns are a whole frame or more apart, so the check never fires there (hosted AROS's E clock runs at 200 Hz, so a frame is 4 ticks and the half-frame threshold is 2: one tick of jitter does not trigger it). The request and its message port are given back after `Main` returns through a new `[AtExit]` attribute (the emitter calls every `[AtExit]` method of a class the program uses, after the entry point; unit-tested with host gcc, including an `int Main` exit code). New offsets and constants (`TrCommand`, `TrSecs`, `TrMicro`, `TimerAddRequest`) are measured into `Layout.g.cs`.
- Verified: 300 unit tests, including a "stuckwork" mode (as stuck, with 0.7 frame of simulated work between waits: 50 frames take about 1000 ms; fails without the sticky flag) and the shimmed m68k timing test in a new "stuck" mode (WaitTOF does nothing, `DoIO` advances the shim clock): 50 waits take 999 ms, ten frames owe 9 steps, 60 frames paced. AmigaOS 4.1 VM (`esharp test samples/timing --target os4`): before 8 ok, 1 FAIL (`50 WaitVBlank calls took 0 ms`); after 10 ok, 0 FAIL, "50 WaitVBlank calls took 1000 ms", "10 frames owed 9 steps", and the note "the display's WaitTOF did not wait for 64 of the frames, so timer.device paced them"; host and OS4 agree. A1200 / Kickstart 3.1 (40.069) in FS-UAE: 10 ok, 1000 ms, no pacing note (WaitTOF works there). Hosted AROS (nightly 20261106): 10 ok, 1000 ms, no pacing note. SDL headless (`esharp-sdl`): 10 ok, host and SDL agree (SDL keeps its own 50 Hz pacing; the fallback is Amiga only).

Verified:
- 224 unit tests pass (this adds the sample on the host at 50 and 60 Hz, and the generated m68k C for `Frame`/`FrameTimer` compiled and run with host gcc against a shimmed timer and graphics, PAL and NTSC; the shim is a model, not the OS).
- A1200, Kickstart 3.1 (40.069) in FS-UAE headless (PAL): 10 ok, no FAIL. `50 WaitVBlank calls took 1001 ms`, so under Xvfb the emulator ran in real time on this host (the host was shared and not idle). Ten frames owed 9 steps of 1/50 s (the timer's rounding, within the sample's 8..12), a one second stall owed exactly the cap of 5, and the next update owed 0.
- Regression after `Video.WaitFrames` moved to `WaitTOF`, same machine: screen 17 ok, cards 15 ok, rummy 28 ok, no FAIL.
- Hosted AROS x86-64 (nightly 20261106): first run showed 60 (NTSC) with frames at a 50 Hz pace, because hosted AROS sets neither the PAL nor the NTSC bit. Fixed: `Detect` answers 60 only when the NTSC bit is set, and with neither bit it reads `ExecBase.VBlankFrequency` (exec.library base from `OpenLibrary("exec.library")`; offset 1096 on AROS x86-64, 530 on m68k, measured into `Layout.g.cs`). Re-run on hosted AROS: 14 ok, no FAIL, `display runs at 50 frames per second (PAL)`, `50 WaitVBlank calls took 1000 ms`. A1200 / Kickstart 3.1 re-run: 14 ok, no FAIL, PAL, 1001 ms. Unit test covers PAL bit, NTSC bit and neither-bit (50 and 60 via the ExecBase byte) against a shim. Not verified: a hosted AROS configured for 60 Hz, real NTSC hardware.

Not verified:
- Real hardware (the sample is in the hardware pack as `timing`), an NTSC machine (60 is checked only against the host fake and the gcc shim), MorphOS, and AmigaOS 4 on real hardware (only the QEMU VM, where the timer paces the frames). A display whose WaitTOF waits only sometimes was not seen. The sticky flag was re-run on the OS4 VM and Kickstart 3.1 with the timing sample only (10 ok each, 1001 ms / 1000 ms); a busy game loop on the OS4 VM, hosted AROS and SDL were not re-run after the flag (the code is Amiga-only and unchanged for SDL/host).
- The emulator was not run at other speeds, so what the sample does on a slow emulator (it prints the note) was not seen.

## Dates and case-insensitive text: utility.library (samples/dates)

`Date.Now()` reads the system clock (dos.library `DateStamp`) as whole seconds since 1 January 1978, the Amiga epoch, in local time. `Date.Split(seconds)` gives a `DateParts` (year, month, day, hour, minute, second, weekday; utility `Amiga2Date`). `Date.TryJoin(parts, out seconds)` goes back (`Date2Amiga`) and returns false for a date that does not exist. It checks by splitting the result back, not with `CheckDate`: `CheckDate` returns 0 for 1 January 1978 00:00:00, a real date, and hosted AROS's `CheckDate` accepts 29 February 2001. `Strings.CompareIgnoreCase` (with an optional length), `EqualsIgnoreCase`, `ToUpper` and `ToLower` use utility's `Stricmp`, `Strnicmp`, `ToUpper` and `ToLower`. They follow ISO-8859-1, so é and É fold, and the locale's rules when locale.library has set them up. utility is in ROM, but it is opened on first use through its own `[LibraryBase]`; `Date.Close()` closes it. The ClockData and DateStamp layouts and `TICKS_PER_SECOND` are measured. On the host, `Date.HostSetNow` sets the fake clock, `Dos.Delay` moves it on, and the conversions use .NET's calendar.

Verified:
- 152 unit tests pass.
- A1200, Kickstart 3.1 in FS-UAE: 19 ok. This covers fixed dates both ways, weekdays, leap years, invalid dates, the clock moving about 3 s in `Delay(150)` (the emulated A1200 has no clock battery, so it reads 1978-01-01), case-insensitive compare, and Latin-1 folding.
- Hosted AROS (nightly 20261106): 19 ok, no traps, with the real host time read.
- The hardware pack builds with `dates`.

Not verified:
- A different locale (`locale.library` with a non-English language).
- Real hardware, AmigaOS 4 and MorphOS.

## Positional and list patterns (samples/patterns, issue #12)

**Positional patterns.** `(p1, p2)` on a value tuple tests `Item1`, `Item2`, ...; on a struct or class it calls the type's instance `void Deconstruct(out ..)` once, into temporaries (a struct is copied first, as its methods take the receiver by address), then tests each part. They nest with constant, type, relational, `and`/`or`/`not`, `var`, discard, property and list subpatterns, take a type (`Rook(0, var r)` after a class test) and a designation, and `var (a, b)` deconstructs the same way. `switch (x, y) { case (0, _): ... }` makes the tuple once.

**Property patterns** now read properties with a getter as well as fields and auto-properties (the getter runs once per test, into a temporary), the extended form `{ A.B: p }` (A tested for null on the way, as C# does), `Length` of arrays, spans and strings. A reference-typed subject never matches `{ }`, `T { ... }`, `T x` or `T` when it is null (before this change `obj is { }` and `obj is T t` with `obj` already a `T` were always true in the C).

**List patterns** over arrays, `Span`/`ReadOnlySpan`, strings and any type with `Length` or `Count` and an `int` indexer that returns a value (so `Vec<T>`): `[a, b]`, `[first, ..]`, `[.., last]`, `[_, .. var middle, _]`. The length is read once; elements before the slice are indexed from the start, those after it from the end (no bounds check needed: the length test comes first). A slice with a subpattern is a view `{ ptr + before, length - before - after }` on spans only; over an array or a string it would copy, and is `ES0099` (`.. _` and a bare `..` work on all of them).

**`switch` statements with pattern labels** (any `case` with a pattern or `when`, or a type name): the subject goes into a temporary once (an array is used in place), the labels are tried in source order (the default last), and a C `switch` on the number of the label that matched keeps `break` and the sections as they were. Pattern variables of all sections (and of switch expression arms, including their `when` clauses) are declared once per name before the test; the same name with two different types in one switch is `ES0099` (rename one).

**Subjects evaluated once:** an `is` subject is used in place only when it is a local, a parameter, a field or an array/span element; a call, a getter or an indexer goes into a temporary first.

Still `ES0099`: an extension `Deconstruct`, a positional pattern on `ITuple` or an interface, a `Deconstruct` with ref/in parameters, `var (a, b)` on a type with two `Deconstruct` overloads of the same arity, type tests against interfaces and structs, list patterns on interfaces or on types whose indexer takes `Index` or returns by ref, a slice with a subpattern over anything but a span, an extended property pattern through a property with a getter in the middle (`{ Getter.X: 1 }`; write `{ Getter: { X: 1 } }`), and records (all of them).

Verified:
- 312 unit tests (311 pass, 1 skipped: the OS4 VM test, no VM configured in the test run). New in `CEmitterPatternTests`: host-gcc compile-and-run tests for positional patterns (tuples, struct and class `Deconstruct`, nested, `var (a, b)`, subject evaluated once; 10 checks), property patterns (getters read once, extended names, null; 8 checks), list patterns (arrays, spans with a slice, strings, `Vec`; 8 checks), switch statements with pattern labels (5 checks), `samples/patterns` as host-gcc C (23 ok), and `ES0099` for slice captures over an array and a string.
- `esharp test samples/patterns`: host (dotnet) 23 ok; m68k on A1200 / Kickstart 3.1 (40.069) in FS-UAE 23 ok, 0 FAIL, host and Amiga agree; `--target sdl --docker esharp-sdl` 23 ok, host and SDL agree.
- Hosted AROS x86-64 (nightly 20261106), `--target aros` build: `samples/patterns` prints 23 ok, no FAIL, no traps.
- Regression on the A1200 / 3.1 after the change: `samples/language` 35 ok, `samples/inherit` 24 ok, host and Amiga agree.

Not verified: real hardware, AmigaOS 4 (the change is not PPC-specific; not run in the VM) and MorphOS.

## Ref locals, ref returns and foreach over enumerators (samples/refs, issue #12)

**ref locals and ref returns.** `ref T x = ref place;` is a C pointer `T *x = &place;` and every use of `x` reads or writes through it; `x = ref other;` points it somewhere else. The place can be a local, a field, a static, an array or span element, a `ref`/`out`/`in` parameter, another ref local, a ref-returning call or `c ? ref a : ref b`. A method declared `ref T F(...)` or `ref readonly T F(...)` returns `T *`; a call used as a value is `(*F(...))`, so `F(a, i) = 5`, `F(a, i) += 1`, `F().X = 3`, `F().Method()` and `G(ref F())` work. `foreach (ref var x in span)` and `foreach (ref readonly var x in readOnlySpan)` give a pointer to each element. `ref readonly` is C#'s check only; in C it is the same plain pointer. Still `ES0099`: ref returns from properties, indexers, lambdas, delegates, interface methods and virtual, abstract or override methods; ref locals of an array type or declared in a `for` header; ref fields (tested: they give `ES0099`, as do ref-field uses and `h.R = ref x`). `c ? ref a : ref b` also works as a value, an assignment target and an `in` argument (host-gcc tested; `ref (c ? ref a : ref b)` as a ref argument is still `ES0099`).

**foreach over the GetEnumerator pattern.** Besides arrays and spans, `foreach` now works on any struct or class with an instance `GetEnumerator()` returning a struct or class that has `bool MoveNext()` and a `Current` property (an auto-property or one with a getter). It becomes `{ E e = GetEnumerator(&coll); while (MoveNext(&e)) { T x = Current(&e); body } }`: a struct enumerator is a local on the stack, nothing is allocated, and `break`/`continue` are the `while` loop's. A collection that is a call result is copied into a local first. A numeric widening of the element (`foreach (long x in ints)`) is a cast. `Vec<T>` has a `GetEnumerator` (a `VecEnumerator<T>` struct) that walks the items as they were when the loop started: do not add or remove inside the loop. Still `ES0099`: an interface-typed collection or enumerator (that would box), an extension `GetEnumerator`, an enumerator that has `Dispose` (E# would not call it), `foreach` by ref over an enumerator, a ref-returning `Current`, any other element conversion, `await foreach`, and `foreach (var (a, b) in ...)`. The hash collections (`Dictionary`, `HashSet`, `StringMap`, `StringSet`) and `Pool<T>` still have no enumerator.

Verified:
- 190 unit tests pass (new: ref locals and returns compiled with host gcc and run, 14 checks; foreach over a `Vec`, user struct enumerators, a class enumerator, a call result, nested loops with `break`/`continue`, 9 checks; `ES0099` for a ref indexer, an interface collection, a ref `Current` and an abstract ref return; `samples/refs` on dotnet and as host-gcc C with the same output).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, m68k build: `samples/refs` prints 18 ok, no FAIL.
- Hosted AROS x86-64 (nightly 20261106): `samples/refs` prints 18 ok, no FAIL, no traps.
- Regression on the A1200 / 3.1 after the change: collections 20, linq 37, inherit 24 ok, no FAIL. The hardware pack builds with `refs` (22 programs).

Not verified: real hardware, AmigaOS 4 and MorphOS.
## Inline assembly and interrupt servers (samples/interrupts, issue #12)

**`[InlineAsm]`.** A static method with a C# body and `[InlineAsm("68020 code", Ppc = "PowerPC code")]` becomes one GCC extended asm
statement on a target with code for its CPU (m68k: the constructor argument; os4/morphos: `Ppc`), under `#if defined(__mc68000__)` /
`#if defined(__PPC__)` with the C# body in the `#else` branch. Everywhere else (host, sdl, AROS x86-64, PowerPC without `Ppc`) the C#
body is the method, so it is required (an `extern` one is `ES0099`). Operands, kept minimal: the result is `%[result]` (`%0`), an
early-clobber output register; each parameter is `%[name]` (`%1`, ...), an input register the code must not change. Integers up to 32
bits, chars and enums go in data registers (`d` / `r`), pointers in address registers (`a` / `b`). `cc` and `memory` are always
clobbered, the statement is `volatile`; extra registers are named in `Clobbers` (m68k) or `PpcClobbers`. `ES0099` for: instance or
generic methods, ref parameters, 64-bit, float, bool, struct or class operands, a parameter named `result`, `[InlineAsm]` together with
`[Interrupt]`.

**`[Interrupt]`.** A static method `void M(void* data)` (or no parameter, or an `int`/`uint` result) is emitted as usual plus
`M_isr(void *data)`, a C function returning an int32 (0 for a void method). `&M` (a C# function pointer, only allowed for `[Interrupt]`
methods; any other `&Method` is `ES0099`) is the server address: on m68k a small assembly entry at the end of the file that pushes
is_Data from a1, calls `M_isr` and does `tst.l d0` so the Z flag matches d0 (a void server returns 0 and the chain goes on); the C
function keeps d2-d7/a2-a6 as exec wants. On AROS the address is `M_isr` itself (AROS calls servers as C functions with is_Data first).
PowerPC (AmigaOS 4, MorphOS) and sdl give `ES0099` with a message (OS4 servers take other arguments and the QEMU sm501 has no vertical
blank interrupt to test with). C# `volatile` on a static field is now `volatile` in C.

**`Interrupts` (platform).** `Interrupts.AddVBlank(&M, data)` puts up to 4 servers on exec's INTB_VERTB chain (AddIntServer, nodes of
`struct Interrupt` in one MEMF_PUBLIC|MEMF_CLEAR block, NT_INTERRUPT, priority 0, name "E# VBlank"); `RemoveVBlank(id)` takes one off;
an `[AtExit]` method takes the rest off and frees the block after Main returns (also after an index-out-of-range or out-of-memory exit, which runs the [AtExit] methods first; not after a crash: then the server stays on the
chain pointing at freed code). On the dotnet host each `Frame.WaitVBlank` runs the servers once. Amiga-only on sdl (`ES0102`). The
offsets come from `tools/BindGen/structs.txt` (re-measured with `layouts.sh`: m68k node 22 bytes, AROS 48).

Verified:
- Unit tests: `CEmitterAsmTests` (19): the m68k and PowerPC asm text and constraints, the C# fallback compiled with host gcc and run
  (the asm is under `#if defined(__mc68000__)`, so the host takes the `#else`), no asm on AROS, the `ES0099` cases, the m68k server entry
  text, and the server functions called through their address with host gcc as AROS would (is_Data passed, 0 returned for void).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, `esharp test samples/interrupts --target m68k` (amigadev m68k image): host 14 ok, Amiga 14 ok,
  "Host and Amiga agree on every check"; both servers counted 25 in 25 frames, a removed server stopped, the program exits cleanly with
  the second server removed by `[AtExit]`. The disassembly shows the `swap` and the entry (`move.l a1,-(sp)`, `jsr`, `tst.l d0`, `rts`).
- Hosted AROS x86-64 (nightly 20261106): the same 14 ok (25 and 25), no traps.
- AmigaOS 4.1 in the QEMU VM (`run --target os4`): the 6 inline asm checks pass with the PowerPC code (`rotlwi`, `add`/`stw` seen in the
  disassembly); the interrupt part is compiled out there (`#if !SDL && !PPC`).
- sdl (`run --target sdl --headless`): the 6 checks pass with the C# bodies. MorphOS: builds, not run.

Not verified: real hardware, MorphOS runs, `[Interrupt]` with other chains than INTB_VERTB (only `&M` and exec's AddIntServer are
needed for them, but nothing was run), the server's stack use on a busy system, and the old-Kickstart rule that a VERTB server must
leave a0 = $dff000 (not needed from Kickstart 3.1; not done).

## C names of E# types (samples/names, issues #39 and #12)

Every E# class, struct, interface and enum, in user code and in the embedded Runtime/Platform sources, is named in C
`Es_` + namespace + type, joined with `_`: `CardsDemo.Board` is `Es_CardsDemo_Board`, `ESharp.Platform.Dos` is
`Es_ESharp_Platform_Dos`, a nested type adds its containing types, a type in the global namespace is `Es_Board`.
Functions, static fields, enum members, constructors (`_new`), class tables (`_vt`, `_cvt`, `_alloc`) and interface
thunks start with that name. Inside longer names (overloads, `ES_Span_`/`ES_Tuple_` typedefs, generic copies such as
`Box__NamesSample_Left_Point`) a type is written with its namespace but without the prefix. No identifier in the
m68k toolchain's NDK/libnix headers or in the AROS SDK headers starts with `Es_` (checked with grep in both images); the
emitter's own helpers keep `es_` (library bases, runtime functions) and `ES_` (spans, tuples, delegates, type ids).
Delegate types were already structural (`ES_Wfn_N`). `[AmigaLib]` and `[Native]` functions keep their system names.

`samples/names` declares classes `Window`, `Gadget`, `Screen`, `Node`, `List`, `Menu`, `Image`, `Task`, `Message`
(with inheritance, a virtual member, an interface and a static field), a struct `Rectangle`, an enum `Border`, a
delegate `Hook`, and `Item`, `Point` and `Tools` twice, in `NamesSample.Left` and `NamesSample.Right` (also through a
generic struct over both `Point`s). It calls intuition's `CurrentTime`, so `proto/intuition.h` is included.

Verified:
- 186 unit tests pass. They include the sample compiled to C with `AMIGA` defined and built with host gcc against a
  stand-in `proto/intuition.h` that declares those structs, the sample on the host, same-named classes in two
  namespaces, and the ES0099 for names that meet.
- `--target m68k` and `--target aros` builds against the real headers. The same C with the old, unprefixed names
  fails in m68k-amigaos-gcc (redefinition of `struct Rectangle`, `struct Node`, `struct List`, `struct Screen`, ...).
- A1200, Kickstart 3.1 (40.069) in FS-UAE: 12 ok, no FAIL.
- Hosted AROS (nightly 20261106): 12 ok, no FAIL, no traps.
- Regression on the A1200 / 3.1: inherit 24 ok, cards 15 ok (with the scripted click, guess and quit).
- The hardware pack builds with `names` (all 22 programs build for m68k with the new names).

Not verified:
- OS4 and MorphOS builds of the sample, real hardware.
- The other samples on the emulator after the rename (only inherit and cards were re-run); they pass as host-gcc and
  dotnet unit tests.

## Keyboard text: keymap.library (samples/keys)

`Keys.Char(rawKey, qualifier)` turns an Amiga raw key code into the character it types with the user's keymap (keymap.library `MapRawKey`), so text follows the keyboard the user has rather than a US layout. Characters are ISO-8859-1 codes; 0 means the key types no single character (cursor and function keys send escape sequences, modifier keys, key releases, a dead key waiting for the next key). `Keys.Find(ch)` goes the other way (`MapANSI`): the raw key and qualifier that type a character. The qualifier bits (`Keys.Shift`, `CapsLock`, `Control`, `Alt`, `LeftAmiga`, `RightAmiga`, `Repeat`) are measured from the headers. `Input` now also gives `LastChar` and `LastQualifier` for the last key pressed in the `Video` window. They are mapped while the IDCMP message is still held, with its previous-keys data, so dead keys can combine. keymap.library is opened on first use through its own `[LibraryBase]`, and closed by `Video.Close` or `Keys.Close`. On the host a fixed US layout is used, and `Input.HostKey(code, qualifier)` feeds a key with modifiers. `esharp run --input` takes `key 50+38` to hold keys (here Shift) while the last one is pressed.

Verified:
- 150 unit tests pass. They include the sample on the host, `LastChar`, `LastQualifier` and `Find` on the host, and the m68k C output (`KeymapBase` renamed, `MapRawKey` and `MapANSI` called).
- A1200, Kickstart 3.1 (40.069) in FS-UAE: all 8 checks pass on the ROM's default (US) keymap: letter, shift, Caps Lock, space, return, escape, backspace, cursor up, key release, and `Find` both ways. A scripted A (`--input "click 400,300;key 38"`) gives raw key 32 and character 97. Shift+A (`key 50+38`) gives qualifier 0x8001 and character 65. Both run through the real IDCMP path.
- Hosted AROS (nightly 20261106): the 8 checks pass, with no traps.
- Regression on the A1200: cards still 15 ok with scripted play (Escape quits). The hardware pack builds with keys in it (20 programs).

Not verified:
- Other keymaps and dead keys. Only the default US keymap was run: no `SetMap` to a German or French keymap, and no dead-key combination.
- Scripted keys on hosted AROS. The XTEST key reached nothing there (the mouse clicks do work for cards); the reason was not looked into.
- Real hardware, AmigaOS 4 and MorphOS.
- The full hardware pack was not re-run.

## Library versions and tag lists (issue #17: `[RequiresLib]` and `TagList`)

**ES0101 now has an opt-in.** A call to an extern binding whose `[Since(n)]` is above the target's library version is error ES0101. The versions come from one table, `ESharp.Analyzers.LibraryVersions`: m68k (AmigaOS 3.x) 40 (Kickstart 3.1), AROS 51 (the highest in its 9.1 headers); os4, morphos and sdl are not checked (their library versions are not verified, or there are no Amiga libraries). To use a newer function on purpose, mark the code `[RequiresLib("graphics", 41)]` (method, class/struct, or `[assembly: RequiresLib(...)]`; `"graphics.library"` also works; may be repeated for several libraries). It silences ES0101 for calls to that library whose Since is at most the stated version. It does not open anything: the program must make the promise true, for example `Exec.OpenLibrary("graphics.library", 41)` and an early return on null (the compiler cannot raise the version that the startup code opens a library with). The check applies to user code only; the platform gates its own calls. A `[RequiresLib]` on a local function does not cover its caller.

**`TagList`** (`ESharp.Platform`, in Tags.cs) is a tag list with its own storage: a struct local with room for 15 tag/value pairs and TAG_DONE, so building one needs no array, no `fixed` and no capacity number: `TagList t = new TagList(); t.Add(Intuition.SA_Width, 320); nuint* list = t.End();` (`End` returns null if more than 15 pairs were added; the pointer is valid while the local lives). `TagWriter` stays for lists in caller-owned memory. Video (screen, window, RTG and mode-id lists), Joystick, `Asl.PickFile`, and the GadTools window and gadget lists in Gui now use `TagList` (behaviour unchanged). The user samples (asl, gui, fonts) build no tag lists themselves: they go through these wrappers. Not done: typed tag names (tags are still `uint` constants, a wrong value type is not caught) and tags for further libraries.

Verified:
- 353 unit tests pass, 1 skipped (the OS4 VM test): new are 8 `[RequiresLib]` cases (method, long library name, higher version, type; local function, too low a version, another library, none: ES0101), assembly-level opt-in, the version table, and a host-gcc run of `TagList` (layout, negative values, 15 pairs fit, the 16th gives null, TAG_DONE after the last pair).
- A1200, Kickstart 3.1 (40.069) in FS-UAE: `esharp run` gui 7 ok (the AGENTS.md input line), fonts 7 ok (no Libs:), screen and cards finished with their last lines ok (only the tail of the output was looked at); with the Workbench 3.1 disk as `LIBS:` (the hand-written config described above): asl 3 ok, with `RAM:asl-open` the scripted mouse gives "result: picked RAM:asl-open" and "result: cancelled" (the file requester is built with `TagList`), fonts with the Fonts disk 10 ok, no FAIL (ruby 12 opens, height 12).
- Hosted AROS x86-64 (nightly 20261106, `--target aros`): gui 7 ok (click Hello, then the close gadget), asl 3 ok, and with `RAM:asl-open` the requester appears on screen with its title and drawer RAM: (64-bit tag slots) and Cancel gives "result: cancelled", screen 17 ok, cards 16 ok, controls ok; no traps.
- `--target os4` builds gui, asl and cards; `--target sdl` runs cards (20 ok) and `esharp test samples/language` (78 ok); host `esharp test samples/gui` 7 ok.

Not verified: os4 in the VM and MorphOS for this change (`--target morphos` fails to compile gui, asl and cards with pointer-type errors, including in code this change does not touch; not investigated), real hardware, `[RequiresLib]` with a binding that really needs a newer library than 3.1 (checked with test bindings only: no shipped binding has Since above 40), and a `TagList` with more than 15 pairs on a target (host-gcc only).

## Fonts, IFF files and the file requester: diskfont, iffparse and asl (samples/fonts, iff, asl; part of issue #17)

All three libraries are on the Workbench disk (`Libs:`), not in ROM, so each wrapper uses its own `[LibraryBase]` and starts cleanly without the library: `Open()` / `DiskFontsAvailable` return false and the sample prints that it is not installed.

- **`Fonts.Open(name, size)`** returns a `FontHandle` (`IsOpen`, `Height`, `Width`, `Baseline`). With diskfont.library available it calls `OpenDiskFont` (memory and ROM first, then `FONTS:`; exact size preferred, else the closest, so check `Height`); graphics `OpenFont` alone returns the closest size in memory, so without the library only an exact size counts (ROM topaz 8 still opens). `Fonts.Draw(font, text, x, y, color)` and `Fonts.TextWidth` use the `Video` window's rastport (`SetFont`, `Move`, `Text`), so a font is usable by drawing code as long as a `Video` screen is open; the built-in `Font` class is unchanged. `Fonts.Close(ref font)` and `Fonts.Close()` (the library). Host (same rule): topaz 8 and 9 plus `Fonts.HostAdd`, `Fonts.HostDiskFonts = false` for the missing-library path; text is drawn with the built-in 5x7 font.
- **`Iff`** writes and reads one FORM: `Iff.Create(path, Iff.Id("ESHP"))`, then `BeginChunk` / `Write` / `EndChunk` (or `WriteChunk`) and `Finish()`; `Iff.OpenRead(path)` gives `FormType`, and `Next()` steps through the chunks (`ChunkId`, `ChunkSize`) with `Read(buffer, max)`. It uses iffparse in raw-step mode on a dos file (`InitIFFasDOS`), so sizes are patched and pad bytes written by the library. Host: the same file format written and read with plain .NET (a unit test checks the byte layout), `Iff.HostAvailable = false` for the missing-library path. Only a FORM at the top level and flat chunks (no nested FORM/LIST/CAT, no property or collection chunks).
- **`Asl.PickFile(title, drawer)`** shows asl.library's file requester (on the `Video` screen if one is open, else the default public screen) and returns the path (drawer and file joined) or null on Cancel or a missing library; the string is valid until the next call. `Asl.CanAllocate()` allocates and frees a requester. Host: `Asl.HostPick` is the answer, `Asl.HostAvailable = false` for the missing-library path.

The samples print `ok` / `FAIL`. `samples/asl` only opens the requester if `RAM:asl-open` exists (so an unattended run does not wait for a click); `RunAll` in the hardware pack creates it and deletes it afterwards.

Layout and bindings: `TextAttr`, `TextFont`, `IFFHandle.iff_Stream`, `ContextNode`, `FileRequester`, the ASL tags, `ID_FORM`, the IFF constants and error codes are in `tools/BindGen/structs.txt`, measured into Layout.g.cs for m68k and AROS x86-64. `bindgen check <fd dir>`: 35 declarations, 0 problems. See BINDINGS_SPIKE.md.

Verified:
- 189 unit tests pass (host runs of the three samples with the library present and missing, the IFF byte layout, and the m68k C output of each sample with its private library base).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, DH0 holding the program, DF0 the Workbench 3.1 disk (read-only, in place) with `Assign LIBS: DF0:Libs ADD`, `C:` assigned the same way, `ENV:` assigned to a RAM: drawer (asl and diskfont otherwise raise a "Please insert volume ENV" requester), and for disk fonts the Fonts disk (Disk 6 of 6, read-only) in DF1 with `Assign FONTS: DF1: ADD`:
  - `iff` with LIBS:: 10 ok, no FAIL (write, read back, three chunks including an odd-sized one and a chunk written in two pieces). Without LIBS:: "iffparse.library is not installed: nothing to do".
  - `fonts` with LIBS: and the Fonts disk: 10 ok, no FAIL; `ruby 12` opened from FONTS: with height 12, width 11, baseline 8, and the screenshot shows topaz 8 and the ruby text. With LIBS: and no Fonts disk: 8 ok ("no ruby font in FONTS:"). Without LIBS: 7 ok ("diskfont.library is not installed: disk fonts skipped"); topaz 8 still opens.
  - `asl` with LIBS:: 3 ok (library opens, requester allocated and freed twice); without the flag file it stops there. With `RAM:asl-open` and the scripted mouse (`click 400,300;rel -100,-161;press;rel -8,274;press`: pick the listed file, then OK): "result: picked RAM:asl-open". Cancel (`click 400,300;rel 0,130;rel 300,0;press`): "result: cancelled". Screenshots show the requester titled "E# file requester" on the sample's screen, drawer RAM:. Without LIBS:: "asl.library is not installed: nothing to do".
- Hosted AROS x86-64 (nightly 20261106, `--target aros`): `iff` 10 ok (so the 64-bit `IFFHandle` and `ContextNode` offsets are right), `fonts` 8 ok (no disk font in that archive's FONTS:, `diskfont.library` opens), `asl` allocate/free 3 ok; with the flag file created in the startup sequence, a click on Cancel gives "result: cancelled" and a click on the listed file then Ok gives "result: picked RAM:asl-open". No traps.
- The hardware pack builds with fonts, iff and asl in it (24 programs).

Not verified:
- A disk font from a real hard-disk `FONTS:` (the Fonts disk in DF1 was used), other font names or sizes than ruby 12, proportional or outline (`.otag`, bullet) fonts, and the font style flags.
- `RunAll` itself with the three new entries (each sample was run on its own).
- Reading IFF files written by other programs (ILBM, 8SVX), nested FORM, LIST, CAT, `PropChunk` and `StopChunk`, the clipboard, an IFF file larger than a few hundred bytes, and a write to a full disk.
- Other ASL requesters (font, screen mode), the extra tags (patterns, multi-select, save mode), and a requester when no public screen exists and no `Video` screen is open.
- A keyboard or mouse click on a real machine, real hardware, AmigaOS 4 and MorphOS. The XTEST mouse positions above are specific to that FS-UAE window (the relative-move scale was about 0.89 Amiga pixels per unit).
- `esharp run` cannot mount the Workbench disk: the Workbench-disk runs used a hand-written FS-UAE config and startup-sequence (the same way as for `controls`), not the CLI.

## FS-UAE debugger trial (issue #14)

Tried the debugger built into FS-UAE 3.2.35 (A1200, Kickstart 3.1, headless under Xvfb, `samples/hello`); recipe and findings in [DEBUGGING.md](DEBUGGING.md#fs-uae-debugger-trial). Verdict: **adopt as a manual recipe**, no `esharp` integration. Verified: `console_debugger = 1` plus the F12+D hot key (sent with XTEST) enters the debugger, its prompt is on FS-UAE's stdin/stdout (needs a pty), and registers (`r`), memory (`m`), disassembly (`d`), breakpoints (`f <addr>`, `g`) and single-step (`t`) work. A breakpoint at the `_main` address (`nm` offset 0xfe plus the load base 0x1B1B8, found through a breakpoint on dos `PutStr`) hit on the first instruction of the E# program, in two separate runs. Not working: `fp "<name>"`, memory search `s`, `T`. Not verified: `w`/`z`/`fi`, programs other than hello, other ROMs, real hardware, any `esharp run` flag (none was added).

## Serial trace and `esharp addr` (samples/trace, docs/DEBUGGING.md; issue #14)

`Trace.Write/Line/Value` (src/ESharp.Platform/Trace.cs) write to the serial port through exec `RawIOInit`/`RawPutChar` (called by vector, LVO -504/-516: they are in no m68k NDK header; the C prelude has the stubs) and to standard error on the host. They are `[Conditional("TRACE")]`: the emitter drops such call statements, and what only they reach, unless the build has `--trace` or `--debug`. `esharp run --serial <file>` (headless) points FS-UAE's `serial_port` at a file and prints it after the run. `esharp addr <exe|dir> <hunk> <hex offset> --docker <image>` maps a hunk-0 offset to the C# file, line and C function using `<name>.o`, which every m68k build now also writes (the same C with `-g -c`, read with `m68k-amigaos-objdump --dwarf=decodedline`).
Verified: 213 unit tests pass, including the trace sample on the host (stderr text), the C with and without `TRACE` (calls and `Trace` functions gone without it; the sample builds and runs with host gcc both ways), and `esharp addr` on a real m68k build in the docker image (addresses of `Square` and `Main` from the exe's symbols map to lines in those functions, and the first statement of Main to its line). Run in FS-UAE (A1200, Kickstart 3.1 40.069, `--serial`): the nine trace lines arrive in the file in order, and `samples/trace` without `--trace` writes nothing and its exe is 1 KB smaller. Run on hosted AROS (20261106 nightly, x86-64): the nine lines reach the bootstrap output (`aros-hosted.sh trace --log`). A deliberate zero divide on KS 3.1 gives the "Software Failure ... error #80000005" requester with no PC, so no address was taken from a real crash.
Not verified: real hardware (serial cable, baud rate; the vectors are as I know them from the exec autodocs/fd layout, and they work in FS-UAE with the real 3.1 ROM and on AROS), a hunk offset from a real crash (needs SegTracker/Enforcer, not tried), `esharp addr` for optimised code on a statement boundary, MorphOS (the trace compiles to a no-op stub there; os4 now uses `DebugPrintF`, see "AmigaOS 4.1 VM runs"), AROS x86-64 `esharp addr` (m68k only). The platform/runtime code has no `#line`, so an address in it is reported as runtime code without a source line.

## GadTools windows (samples/gui, issue #19, first milestone)

`GtWindow` opens a window on the default public screen (Workbench) with GadTools gadgets, stacked in one column. Gadgets: `GtString { Label, MaxChars, Text }`, `GtButton { Label }`, `GtText { Label, Text }` (copied text: `GTTX_CopyText`, so an interpolated string is fine), all from a `GtGadget` base. `win.Add(gadget)` (up to 16), `win.Open()`, then either a pull loop, `GuiEvent e = win.Wait()` (`e.Kind` is `Close`, `Clicked` for a button or `Changed` for a string gadget accepted with Return; `e.Source` is the gadget), or `win.Run(handler)` with a class implementing `IGuiHandler` (`Clicked(g)`, `Changed(g)`), which loops until the close gadget. `win.Close()` releases in order (CloseWindow, FreeGadgets, FreeVisualInfo, UnlockPubScreen, then the library) and frees the gadget objects that were added; the window object itself is freed by the program (`Heap.Free(ref win)`). Layout follows the screen font: each row is the font height plus 6, the label column is as wide as the longest label times the font's width, and the window's inner height is computed from the row count. Events go through `GT_GetIMsg` / `GT_ReplyIMsg`; refresh messages are handled inside `Wait` (`GT_BeginRefresh` / `GT_EndRefresh`). `GtString.Text` reads the live gadget (`GT_GetGadgetAttrsA`, v39) so it includes what the user typed; the string stays valid until the gadget is edited. Labels and the title are copied into memory the window owns. gadtools.library is opened (v39) by the first window and closed with the last, through its own `[LibraryBase]`.

Host: nothing is shown. `win.HostClick(gadget)` (a button click, or Return in a string gadget), `win.HostType(stringGadget, text)` and `win.HostClose()` queue what a person would do; `Wait()` returns `Close` when the queue is empty so a loop cannot hang; `GtWindow.HostCanOpen = false` makes `Open()` fail. One window at a time.

Not in this milestone, by decision: menus, BOOPSI, MUI, hooks, typed tags, subclassing platform classes, keyboard shortcuts (an underscore in a label is shown as is), other gadget kinds, window resizing or a layout other than one column. Gadgets can be added only before `Open()`.

Bindings added: gadtools `CreateGadgetA`, `FreeGadgets`, `GT_SetGadgetAttrsA`, `GT_GetIMsg`, `GT_ReplyIMsg`, `GT_RefreshWindow`, `GT_BeginRefresh`, `GT_EndRefresh`, `CreateContext`, `GetVisualInfoA`, `FreeVisualInfo`, `GT_GetGadgetAttrsA` (v39; in the fd file it sits after six reserved slots, vector -174); intuition `LockPubScreen`, `UnlockPubScreen`; exec `WaitPort`. Vectors are from the fd files (computed from the `##bias` and `##private` lines); `bindgen check` against the AROS include dir did not cover gadtools (that include dir has no gadtools headers, so it checked 39 declarations and only the old `alib.BeginIO` note remains); `bindgen compare ... gadtools` shows only the two v47 functions (`SetDesignFontA`, `ScaleGadgetRectA`) that AROS lacks. `NewGadget`, `Screen` border and font, `Gadget.GadgetID` offsets, the `*_KIND`, `GT*`, `WA_*` and `IDCMP_*` values are in `structs.txt`, measured into Layout.g.cs for m68k and AROS x86-64 (`NewGadget` is packed on m68k: 30 bytes, `ng_Flags` at 18).

`LockPubScreen(NULL)` in the `esharp run` setup (Kickstart 3.1 ROM, no Workbench disk): it returns a screen titled "Workbench Screen", so no Workbench-disk setup is needed.

Verified:
- 211 unit tests pass (6 new): the sample on the host fake, the fake's queue and `IGuiHandler` dispatch, and the m68k and AROS C output (private `GadToolsBase`, `GT_GetIMsg`/`GT_ReplyIMsg`, close order CloseWindow, FreeGadgets, FreeVisualInfo, UnlockPubScreen), and `win.Run(handler)` emitted for m68k and AROS (the sample itself uses the pull loop, so `Run` is checked as generated C, not run on a target).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, no Workbench disk: the window opens on the "Workbench Screen" (screenshots looked at: three rows, Name / Hello / Reply, topaz 8). `--input "click 400,300;rel -96,-100;press;rel -72,-63;rel -42,-20;press"` (Hello, then the close gadget) printed `ok` for each step (window opens, default name read back, Hello clicked, "reply: Hello, Amiga!", a click arrived, the reply is Hello, Amiga!, the close gadget arrived, the window is freed) and exited cleanly. A screenshot after the Hello click shows "Hello, Amiga!" in the Reply field.
- Hosted AROS x86-64 (nightly 20261106, `--target aros`): the same output with `click 208,74;click 47,37`, 0 traps, and a screenshot after the click shows the reply. So the 64-bit `NewGadget` offsets are right.

Not verified:
- Typing into the string gadget (keys do not reach hosted AROS; on FS-UAE no key script was run for this sample). The default name is what proves the round trip.
- Real hardware, a Workbench disk setup (other fonts than topaz 8, a taller screen font, a different screen than Workbench), AmigaOS 4 and MorphOS (only the compiler targets exist; the sample was not built for them), several windows at once, a window that is covered and refreshed (`GT_BeginRefresh` path not exercised), `Run(handler)` on a real target (host only).
- Emitter limits seen on the way (string `==` compared addresses, `x!` was copied into the C, base/derived compares warned in gcc) are fixed (issue #49, see its section above); the sample now uses `==` on strings and builds without gcc warnings for m68k. The fixed sample was built for m68k but not re-run in FS-UAE.

## Hardware test pack (tools/hwpack, samples/hwinfo)

`tools/hwpack/build.sh <m68k image>` builds every sample for the A1200 plus `samples/hwinfo` into one drawer (`obj/hwtest`: the programs, `RunAll`, `README.txt`, an empty `results/`) that can be copied to a real Amiga and run with `Execute RunAll`. `hwinfo` prints the Kickstart and library versions, CPU and FPU flags (read from ExecBase), vertical blank frequency, free chip and fast RAM and whether AGA is present. `RunAll` writes one file per program into `results/` and a `SUMMARY.txt` (hwinfo, then any line containing `FAIL `); `README.txt` tells the tester what to look at by eye and what to report (machine, accelerator, display path, timing). Verified: the pack built from commit 87cc3bb plus this change ran end to end in FS-UAE (A1200, Kickstart 3.1 40.069, the Workbench 3.1 disk mounted read-only as DF0: only to supply `C:`): 17 programs, 330 `ok` lines, no `FAIL`, about 75 seconds, `SUMMARY.txt` clean; `hwinfo` reports 40.10 exec, 68020, no FPU, PAL, 1.97 MB chip free, no fast RAM, AGA yes. Not verified: any real hardware (this is the pack for it, issue #7), the pack on a machine with a different CPU or no `Search`/`Echo` in C:, what the screens and sound look and sound like on a real display and card (the README asks the tester), `Protect RunAll +s` being needed after a copy from a PC (written from knowledge of AmigaDOS, not tested).

## Lambdas that capture, and LINQ over arrays (samples/lambdas, samples/linq)

**Delegates carry an environment.** A delegate (`Func<>`, `Action<>`, `Predicate<>`, a user `delegate` type) is now a C struct of a function pointer and an environment pointer. A static method or a lambda that captures nothing has a null environment. A lambda that uses a local, a parameter or `this` gets an environment struct of pointers to those variables, built on the stack of the method that made it, so it shares the variables with the method (a write inside the lambda is seen outside, and the other way round), as in C#. Lambdas inside lambdas capture through both levels. `d == null`, `d != null` and `null` work on delegates, and `Pick()(x)` (calling a returned delegate) works.

- `long`, `ulong` and enums in interpolated strings: checked by a unit test against host gcc output (an enum member prints its name, an unknown value its number, `D` the number) and by a scratch program on Kickstart 2.04 (A500+) and 3.1 (A1200) (not kept as a sample). 135 unit tests pass.
- **Limit (by design, no heap closures):** a capturing lambda only lives as long as the method that created it. It may be passed as an argument (also down several calls) or be the initializer of a local variable. Returning it, storing it in a field, a static or an array, or assigning it to a variable declared earlier is `ES0099`. A callee that keeps a closure it was given (in a field, say) would leave a dangling pointer after the creating method returns; the compiler cannot see that. A lambda that captures an array is `ES0099` (pass it as a `Span<T>`). Local functions still may not capture.
- **LINQ** is turned into loops, for arrays and `Enumerable.Range` only. Steps: `Where`, `Select` (also the `(x, i)` index forms), `Take`, `Skip`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` (also `orderby` in a query). Terminals: `Count`, `Sum`, `Min`, `Max`, `Any`, `All`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Contains`, `Aggregate` (with and without a seed). A chain, or a query with one `from`, `where`, `let` and `select`, is one loop with no sequence in memory, so it must be consumed on the spot: by a terminal call, or by `foreach`. The lambdas are pasted into the loop, so they use the surrounding locals directly (no closure) and must be expression lambdas, static method groups or delegate variables. **A sort** copies the elements and their keys into stack arrays as large as the source, sorts them with a stable insertion sort (O(n²): fine for hands of cards, slow for thousands) and goes on from the sorted copy, so keep the source small: the stack on a 68000 is only a few KB by default. Keys must be numbers, `char`, `bool` or enums (not strings or structs); a `let` before an `orderby` in a query is `ES0099` (put it after). `ToArray` and `ToList` cannot exist: arrays have a fixed compile-time size and there is no growable list. Everything else (`ToArray`, `ToList`, `Distinct`, `GroupBy`, `Join`, `SelectMany`, `Zip`, `Reverse`, `Average`, joins and ordering in queries, `from` twice, `group`, `into`, a chain stored in a variable, LINQ over a `Span` or `Vec`) is `ES0002` (the analyzer) or `ES0099` (the emitter). `Min`, `Max`, `First` and `Last` on an empty source give 0 or `default` instead of throwing (E# has no exceptions). `Take(n)` stops before it pulls the next element.
- Sorting added afterwards: `samples/linq` is now 37 checks (8 for `OrderBy`/`ThenBy`/`orderby`: two keys, stability, structs, `Range`, with `First`/`Skip`/`Take` after) and gives the same output on dotnet, host gcc and Kickstart 2.04 (A500+) and 3.1 (A1200); 136 unit tests pass. Not verified: PowerPC, speed of the insertion sort on a real 68000, a sort of a large source (stack use).
- Verified (before sorting): `samples/lambdas` (16 checks: capture of one and two locals, writes through a capture, a captured local changed after the lambda was made, a user delegate type, method groups, a lambda in a lambda, closures made inside a loop, `null` delegates, `this`) and `samples/linq` (29 checks: every step and terminal above, index forms, structs, `Enumerable.Range`, `foreach` over a chain, queries with `let`, a chain inside a lambda) match dotnet and host gcc, and run on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE. 134 unit tests pass. Full regression with both samples added: 17 samples on both Kickstarts, 579 checks ok, no failures.
- Not verified: PowerPC; speed on a real 68000 (a delegate call is two loads and an indirect call; a fused LINQ loop is the loop you would write by hand); the generated C compiled with `--debug`; a delegate as a struct field or a static field initializer (the typedefs may come in the wrong order); a block lambda with more than one statement inside a LINQ chain (`ES0099`: use an expression lambda or a loop).

## Inheritance, virtual members, `is` / `as` (samples/inherit)

A class can derive from one other plain class of the program: fields, methods and properties are inherited, `abstract` classes and members, `virtual` and `override` methods and properties (get/set bodies required; an automatic property cannot be virtual), `sealed override`, `base.Method()` and `base.Property`, constructors with `: base(...)` and `: this(...)` (a constructor without an initializer runs the nearest explicit parameterless base constructor first), and a derived class used where its base class is expected (assignment, arguments, returns). `x is Derived`, `x is Derived d`, `x is not Derived`, `x is Derived { Field: ... }`, `x as Derived` and `(Derived)x` work between classes of a hierarchy. A class that implements an interface passes that to its derived classes; an interface call reaches the most derived override. How it is built in C: a derived class's struct starts with its base class's struct (`es_base`), so a pointer to the derived object is a pointer to the base object. A hierarchy with virtual members or subclasses has one const table of function pointers per class (`X_cvt`, whose first member is the base's table) and the root object starts with a pointer to its class's table; a virtual call is one load and one indirect call. `is`/`as` compare the class's id (its table's address) along the base chain, so they cost a short loop. Classes outside any hierarchy compile to exactly what they did before. Class constructors are now two functions (`X_new` allocates, `X_new_init` runs the body on an existing object), which also fixes a bare `return;` in a class constructor.

- Verified: `samples/inherit` (24 checks: inherited fields, constructor order through `this(...)` and `base(...)`, two levels, virtual calls through the base type, a base method calling virtual ones, virtual property, base calls, `is`/`as`/pattern forms, downcast, interfaces on derived classes including an interface call that must reach an override, a derived class without a constructor over a base with one, a class outside any hierarchy, `Heap.Free` through a base variable) matches dotnet and host gcc, and runs on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, also with `--debug`. 123 unit tests pass. Full regression after the change (constructors of every class were reshaped): 15 samples on both Kickstarts, 497 checks ok, no failures.
- Not verified: PowerPC, speed of virtual calls and type tests on a real 68000, inheritance combined with generics (a generic base class is rejected with ES0007), classes from the library as base classes (rejected), `is`/`as` against an interface or a struct (ES0099), a cast `(Derived)x` that is wrong is not checked (there are no exceptions: it is a plain pointer cast, as `as` is the safe form), `new` hiding of virtual members.

## RTG: graphics cards (samples/rtg)

`Video.HasRtg(w, h)` says whether a retargetable graphics driver (Picasso96, CyberGraphX, the AmigaOS 4 or MorphOS graphics system: anything that provides `cybergraphics.library` v40) has an 8-bit mode of at least that size. `Video.OpenRtg(w, h, depth)` opens the screen on the card (always 8 bits per pixel; `depth` is the number of colours the program uses, 1..8, so the planar assets and `SetColor24` work unchanged), and `Video.IsRtg()` says which kind of screen is open. `Video.Open` is unchanged and uses the native chipset. The library is opened only while a call needs it and its base is a private variable (`es_CyberBase`, set through the `CyberGfx.SetBase` intrinsic): linking the usual `CyberGfxBase` makes libnix refuse to start the program on a machine without the library, which a first attempt did (it hung behind a "can't open library" requester on the A1200).

- Verified: on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, which have no cybergraphics.library, `samples/rtg` starts, reports "no RTG", `OpenRtg` is refused, `IsRtg` is false and the native screen draws (6 checks ok on both). On the host the fake runs the RTG path and the fallback; 121 unit tests pass, including a compile test that the generated C uses the private base. Full regression after the AGA work: 13 samples on both Kickstarts, 439 checks ok, no failures.
- Not verified: any run on an RTG screen (no Picasso96 or CyberGraphX install here to test with), the AmigaOS 4 and MorphOS builds of this code, the speed of planar blits through an RTG driver (each is converted; a chunky blit path is not written yet), mode selection other than "best 8-bit mode of at least this size", windowed or non-8-bit RTG screens.

## AGA: 256 colours, 24-bit palettes (samples/aga)

`Video.Open(w, h, depth)` now takes up to 8 bitplanes (256 colours) on an AGA machine (A1200, A4000; Kickstart 3.0 or newer). `Video.HasAga()` says whether the chipset is there (it asks graphics.library for the best chip set with `SetChipRev`, which a program has to do before the AGA modes work: a bare boot has them off). Without AGA, `Open` refuses more than 5 bitplanes (Kickstart 2.04 would otherwise open such a screen and then misbehave), so a game can try 8 and fall back to 5. `Video.SetColor24` / `SetPalette24` take 0x00RRGGBB (8 bits per channel); on a screen with 5 planes or fewer they round to 4 bits, so the same code runs on OCS. `esharp assets` takes `"aga": true` with up to 256 colours and writes `uint[] NamePalette` (0x00RRGGBB, exact when the image has at most 256 colours, median cut above that; no dithering yet) next to the 8-plane data. The blitter calls (`Blit`, `BlitRegion`, `BlitMasked`, `FillRect`) are the same ones, now with up to 8 planes.

- Verified: `samples/aga` on Kickstart 3.1 (A1200 in FS-UAE, which emulates AGA): 8-plane screen opens, the 256-colour image blits with its exact 24-bit colours (all 256 checked by reading pixels back), pen 255 draws, a 16x16 colour chart screenshot looks right (`docs/images/phase5-aga-a1200.png`). On Kickstart 2.04 (A500+, no AGA) the same program checks the fallback: the 8-plane screen is refused, a 5-plane screen opens and `SetColor24` rounds to 4 bits. Host and unit tests cover the 24-bit asset conversion.
- Not verified: real AGA hardware, hires or interlaced AGA modes (the screen is the default lores mode, 320 wide), HAM8, copper effects, speed of 8-plane blits on a 68020/68030 (the blitter does 8 planes; the CPU does little), AGA fast-memory sprites, PPC.

## Hash collections (samples/collections)

`Dictionary<TKey,TValue>` and `HashSet<T>` (unmanaged keys and values; keys are hashed and compared by their bytes, so use integers, chars, enums or padding-free structs), plus `StringMap<TValue>` and `StringSet` for string keys (compared by characters; the map keeps the key strings it is given and does not copy them, so use literals or other long-lived strings). Open addressing in raw memory (`Mem.AllocFast`), grows at 3/4 full, no exceptions: `Add` returns false on a duplicate or out of memory, a missing key reads as the zero value. `Set`/indexer add or replace, `TryGetValue`, `ContainsKey`, `Remove`, `Clear`, `Destroy`; enumerate with `for (int i = d.First(); i >= 0; i = d.Next(i)) { d.KeyAt(i); d.ValueAt(i); }`. Compound assignment through an indexer with computed arguments (`counts[Word(i)] += 1`) now works.

- Verified: `samples/collections` prints 20 `ok` lines on the host (no leaked blocks), natively through host gcc with the same output, and on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, including a free-memory check around a quiet block of add/remove/destroy.
- Not verified: speed on a real 68000 (the byte-wise hash is a simple loop), tables with hundreds of thousands of entries, PPC.
- Limits: values and keys must be unmanaged (so no class or `string` values; store a `Vec<T>` or a plain struct), no `foreach` over a table, no reference-type keys, no custom comparers. The E# names clash with `System.Collections.Generic` when both are imported (the test project removes that implicit import).

## Sound (samples/sound)

`Sound` (in `ESharp.Platform`) plays 8-bit samples through audio.device: up to four voices, one Paula channel each, allocated by `Sound.Open()`. Samples are generated in code (`Square`, `Triangle`, `Noise`; chip RAM, freed with `Sound.Free`) or loaded from sound files: `Sound.Load(path)` reads a WAV (PCM, 8 or 16 bit, mono or stereo, mixed down to 8-bit mono) or an IFF 8SVX (uncompressed or Fibonacci-delta; first octave only, loop points ignored) into chip RAM, and `LoadWav` / `Load8svx` do the same from bytes already in memory (for example an embedded asset). `Sample.Rate` is the file's rate and `Sound.PeriodForRate(rate)` gives the period for natural pitch. A sample is cut off at 131070 bytes (one audio.device write). A failed load gives an empty sample (`Data == null`). The file calls are `Files.ReadAll`, `WriteAll` and `Delete` (whole files; dos.library on the Amiga, `System.IO` on the host). `Play(in sample, period, volume, cycles)` uses a free voice (or the oldest), `Busy`, `Stop`, `Close`, and `PeriodFor(hz, length)` convert a pitch. On the host the calls are recorded (`Sound.HostPlays` etc.) and `Dos.Delay` lets voices finish, so game logic that plays sounds can be tested under dotnet.

- Verified: `samples/sound` prints 21 `ok` lines (9 of them for loading: WAV 8-bit mono, WAV 16-bit stereo, 8SVX, Fibonacci-delta 8SVX built in memory, refusal of other data, a write-then-load round trip through a file, a missing file, delete, period for a rate) on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, and on the host. The loading checks use test files the sample makes itself (`RAM:` on the Amiga), not files from real tools. That shows audio.device opens, accepts writes, reports a voice busy and then free, and that Stop and Close work.
- `Sound.Available` (new, issue #57): true when sound is open or a trial `Open` succeeds (closed again); a failed trial is remembered. False where no audio hardware works (AmigaOS 4 in the QEMU VM, see "AmigaOS 4.1 VM runs"); a game should then run silently. `samples/sound` and `samples/assets` then print a note and `skip` lines for the playback checks; on a classic Amiga (`AMIGAOS3`) a failed open is still a FAIL, because Paula and audio.device are always there. Host: `Sound.HostHasAudio = false` makes `Open` fail (unit test: the sound sample with no audio has 0 FAIL, its skips, no leaks, and agrees with the normal host run). Re-run after the change: A1200 / Kickstart 3.1 21 ok (sound) and 17 ok (assets), SDL headless 21 ok (sound), host and target agree.
- **Not verified:** how it sounds (the headless emulator gives no audible output), real hardware, volume and period accuracy, PPC. Not verified: loading a WAV or 8SVX written by a real sound editor (only files this repo's sample builds), 8SVX with several octaves, WAV with extra chunks before `data` beyond the skip logic, a WAV over 128 KB, a PPC run. The gcc build of `Files.WriteAll` warns about a discarded `const` (harmless).
- Full ten-sample regression re-run after string interpolation: no failures on both ROMs.

## Regression after classes and interfaces

All ten samples (hello, phase2, memory, generics, properties, operators, language, screen, cards, rummy) re-run on Kickstart 2.04 (A500+) and 3.1 (A1200) after the class and interface work: 360 checks ok, no failures. `samples/language` also passes with `--debug` (22 ok on both ROMs). After interface values were added the ten samples were run again: 366 ok, no failures, and `samples/language` passes with `--debug` too (26 ok on both ROMs). Not verified: PowerPC, real hardware.

## Rummy sample (samples/rummy)

A two-player rummy against a greedy bot (draw from stock or pile, discard, knock at 10 or less deadwood), written from scratch from the public rules with original code and the shared card sprite sheet. It exercises `Vec<byte>`, `ref`/`out` parameters, tuples, switch expressions, a lambda, the `Font` and the mouse. Verified: 35 checks on the host (including 40 bot-against-bot games with all 52 cards accounted for), and on Kickstart 2.04 (A500+) and 3.1 (A1200) the self-checks (3 bot games there) plus a scripted mouse game (draw, discard, bot reply, second turn on the A1200; the A500+ is slow enough that the script's later clicks arrive while the bot is still thinking) with a screenshot (`docs/images/phase4-rummy-a1200.png`). Not verified: real hardware, how long a bot turn takes on a real 68000 (a few hundred hand analyses; it is noticeable on the emulated A500+), knocking by mouse on the emulator, a full game played to the end on the emulator.

Found while building it: `out var` declared in an `if` condition was scoped to the `if` in the C output but lives on in the enclosing block in C# (fixed, tested). `Input.PressX/PressY` give the pointer position at the moment of the click.

## Bitmap font and strings

`Font` (`src/ESharp.Platform/Font.cs`) is a built-in 5x7 font for printable ASCII (`Draw`, `DrawChar`, `DrawInt`, `Width`, integer `scale`), drawn with filled rectangles in one colour on a transparent background, so it needs no assets and works at any screen depth. Strings now support `s.Length` (bytes) and `s[i]` (a byte). Verified by 5 pixel-exact checks in `samples/screen` on the host and on Kickstart 2.04 (A500+) and 3.1 (A1200). The previously unrun samples (hello, phase2, memory, generics, properties, operators, language, screen, cards with scripted input) were all re-run on both Kickstarts after the language batch: 296 checks ok, no failures. Not verified: speed of long texts on a 68000 (one `RectFill` per pixel run), non-ASCII text.

## Phase 1: Hello world (emulator gate met; real hardware not yet run)

Gate: runs in an emulator and on the host.

| Item | State |
|---|---|
| Repo scaffold (solution, projects, README crediting Amiga E) | Done |
| Roslyn front end + subset analyzer (`ES0001`-`ES0010`, `ES0100`) | Done |
| C emitter: static classes/methods, ints, structs, enums, statics, control flow, `[AmigaLib]` calls, `#line` | Done |
| `[EntryPoint]` -> `main()` | Done |
| `esharp new` / `esharp build` / `esharp run` | Done |
| .NET test host + xUnit | Done (`HostConsole` captures `Dos.PutStr`) |
| Generated C compiled and run natively (host gcc + `proto/dos.h` shim) | Done, in tests |
| Pinned toolchain image (`docker/Dockerfile`): m68k, AmigaOS 4 PPC, MorphOS PPC, AROS x86-64, by digest | Done |
| Hello sample builds with the real compilers for m68k, os4 and morphos (`esharp build --target X --docker`) | Done: m68k yields a valid hunk executable, os4/morphos PPC ELF |
| `esharp run` launches FS-UAE with the output mounted as DH0:; `--headless` captures output | Done: hello prints `Hello from E#!` on an emulated A500 (FS-UAE 3.1.66, built-in AROS ROM replacement) |
| Same run on Dave's Linux dev host (Ubuntu, dotnet 10.0.402, FS-UAE 3.2.35, AROS ROM replacement) | Done: prints `Hello from E#!`, exit 0 |
| Run in FS-UAE with a real Kickstart | Done on the Linux dev host (FS-UAE 3.2.35, headless): Kickstart 2.04 (37.175) on A500+ and Kickstart 3.1 (40.069) on A1200 both print `Hello from E#!`, exit 0, 5-8 s. Kickstart 1.x is out of scope (floor is 2.04) |
| Run on real hardware | **Not done** |
| Run the PowerPC builds (os4, morphos) | os4: **done in a QEMU pegasos2 VM** (AmigaOS 4.1 FE Update 3; `esharp run/test --target os4`, see "AmigaOS 4.1 VM runs"). MorphOS: **not done**. PowerPC real hardware: **not verified** |

## Phase 2: data types (done on host and both Kickstarts; PPC not run)

Gate: logic tests pass on host and Amiga. `samples/phase2` prints one `ok NAME` line per check and
must print the same 32 lines everywhere.

| Item | State |
|---|---|
| Fixed-size arrays: `T[] a = new T[N]`, `= { ... }`, `new[] { ... }` as locals and static fields; `Length`, indexing, `foreach` | Done: emitted as C arrays (`T a[N] = {0}`); `[ChipRam]` works on static arrays |
| `Span<T>` / `ReadOnlySpan<T>`: indexing, `Length`, `Slice`, `foreach`, implicit array -> span and span -> read-only span | Done: emitted as `{ ptr, length }` structs; arrays are passed to methods as spans |
| Inline arrays in structs (`unsafe struct` with `fixed byte X[8]`) | Done: C array member; indexed through a pointer, no span conversion |
| Subset rule `ES0011`: no heap, jagged, multi-dimensional, reassigned or non-constant-length arrays; no array parameters or return types | Done |
| Operators on structs (static `operator` methods, explicit conversions) and static methods in structs | Done: become C functions (`Es_ESharp_Fix_op_Multiply_ESharp_Fix_ESharp_Fix`) |
| Instance methods and constructors with arguments on structs | Done: a method is a C function taking `T *_this` first (`c.Bump(2)` -> `Card_Bump(&c, 2)`); a constructor returns the struct (`new Card(5, 1)` -> `Card_new(5, 1)`), with overloads, early `return`, `this.X` and unqualified calls. The receiver must be a variable, field, array/span element or pointer, not a call result |
| `Fix`: 16.16 fixed point (`+ - * /`, comparisons, `FromInt/FromRaw/ToInt/Frac`) in `ESharp.Runtime` | Done. Multiply is four unsigned 16x16 partial products: checked in the m68k-amigaos-gcc 6.5.0b `-m68000 -O2` assembly, it is four `mulu.w` and no `__mulsi3` call (a first signed version did call it), and it matches a 64-bit reference over about 6000 random and edge pairs. Divide by `Fix` uses a 64-bit intermediate (`__divdi3`), so it is slow on 68000. Cycle counts are **not measured** |
| Generics: generic structs and generic static methods, monomorphised | Done as a source-to-source pass before emission (`Monomorphizer`): each use (`Pair<int>`, `Pick<Fix>(...)`, inferred `Pick(true, 1, 2)`, nested `Pair<Pair<int>>`) is renamed (`Pair__int`) and a copy of the declaration with the type arguments substituted is added next to the original; the pass repeats until nothing generic is left (limit 16 rounds, ES0098). Copies carry `#line` so errors and the C `#line` output point at the generic source. Constraints are accepted and ignored. A library generic (such as `Pool<T>`) needs publicly accessible type arguments, otherwise Roslyn reports CS0122 in the library copy. Generic classes (ES0008), generic methods from outside E# source, generics inside generic types' methods called on other generic methods of a generic type, and generic nested types of generic types are not supported. Tested on host, host gcc and m68k, see `samples/generics` |
| `Pool<T>` (typed fixed-capacity pool, `ESharp.Platform`) | Done: O(1) `Alloc` (zeroed) / `Free` (returns false for foreign, misaligned or double-freed pointers, in every build), `Reset`, `Destroy`; freed slots are filled with 0xDD with `--debug`. `samples/generics` has 24 checks, same output on dotnet (no leaks), host gcc and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), with and without `--debug` |
| Properties on structs and static classes | Done. Auto-properties (`{ get; set; }`, get-only set in a constructor) are plain fields (instance) or static variables; properties with accessor bodies or `=>` become functions `T_get_X(&v)` / `T_set_X(&v, value)` (static ones take no receiver). Reads, assignment, compound assignment (including user-defined operators such as `Fix +=`) and `++`/`--` work through variables, pointers, array elements, `this`, and inside generic structs. A write to a property with accessor bodies must be a statement (its value is not kept): `x = (s.P = 3)` is `ES0099`, as is compound assignment on a receiver that is not a plain name/member/element chain. Indexers, object initializers that set a property with accessor bodies, and initializers on instance auto-properties are still `ES0099`. `samples/properties`: 18 checks, same output on dotnet, host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), with and without `--debug` |
| Chip RAM budget report (`esharp build --chip-budget 256k`) | Done for constant sizes, 68k only: sums static `[ChipRam]` data, `new Arena(n, true)`, `new Pool<T>(n, true)` and `Mem.AllocChip(n)`, prints each with its source line and the total, and fails with `ES0030` over the budget. Struct sizes use m68k-amigaos-gcc layout (alignment capped at 2), checked against the compiler's `sizeof` for two structs. Each call site counts once, however often it runs; sizes computed at run time are listed as such and not summed; reservations inside generic code that are not instantiated with constants are not seen. PowerPC targets report nothing. Not checked against a real run's `AvailMem` |
| Implicit user-defined conversions and indexers on structs | Done. `operator implicit` is applied wherever C# applies it (initializer, assignment, argument, return, operand) by calling the conversion function; `Fix` now converts implicitly from `int` (`Fix f = 3; f + 2`). Indexers (`this[int i]`, several indices, overloads, expression-bodied, in generic structs) are accessor functions like properties: `g[3]` is `Grid_get_Item(&g, 3)`, writes, `+=` and `++` are statements calling the setter. Same limits as properties (a write used as a value, or compound on a call-result receiver, is `ES0099`). `samples/operators`: 20 checks on dotnet, host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200) |
| Host: sample runs under dotnet and as generated C with host gcc (`-Wall -Werror -fwrapv`) | Done, in tests |
| m68k: sample built with m68k-amigaos-gcc 6.5.0b and run in FS-UAE 3.2.35 | Done: 32/32 ok on Kickstart 2.04 (37.175, A500+) and Kickstart 3.1 (40.069, A1200) |
| os4 / morphos builds and runs of the phase 2 sample | os4: 32 ok in the AmigaOS 4.1 VM, same as the host. MorphOS: **not verified** |
| Real hardware | **Not done** |

Notes and limits:
- Bounds checking is off by default: indexing out of range is undefined in a normal build. `esharp build --debug` (or `CompileOptions.BoundsChecks`) checks every array and span index (`ES_IDX`), prints `file(line): index N out of range 0..M` to stderr and exits with 20; `--debug` also defines `DEBUG`, which makes `Arena.Reset` fill with 0xDD. Raw pointer indexing is never checked. Verified: host gcc tests, and the memory and phase2 samples built with `--debug` pass on Kickstart 2.04 (A500+) and 3.1 (A1200). Not verified: where the stderr message appears on a real Amiga (a program started from the Workbench may show nothing), and the cost on a 68000.
- Overflow wraps: all targets now compile with `-fwrapv` to match unchecked C#.
- Arrays have no heap form; a `Span<T>` is the only way to hand one to another method. Spans cannot be stored in fields (C# forbids it for ref structs).
- `Slice`, `foreach` and `Length` on a span or array need a plain name or member access, not a call (a `foreach` over a GetEnumerator collection may use a call result: it is copied first).
- Only `Fix` (16.16) exists; an 8.8 `short` type and a `Fix` square root/trig table are not written.
- Constructor initializers (`: this(...)`), static constructors, calls on temporaries (`Make().Get()`) and a constructor combined with an object initializer are still `ES0099`.

## Memory (step 1 done)

| Item | State |
|---|---|
| `Mem.AllocChip/AllocFast/Free/AvailChip/AvailFast`, `Arena` (design in MEMORY.md) | Done: exec `AllocVec`/`FreeVec`/`AvailMem`; host fake with a 512 KB chip budget and leak tracking |
| `sizeof(T)` and `new Span<T>(pointer, length)` in the emitter | Done |
| `samples/memory`: 19 checks | Done: same output on dotnet (no leaks), host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), including a check that chip blocks are below 2 MB |
| AmigaOS 4 / MorphOS allocation calls, fast-RAM paths | **Not verified** |

## Phase 3: screen (gate met on the emulator; real hardware not run)

Gate: a PNG shows on an Amiga screen. `samples/screen` converts `card.png` with the asset pipeline,
opens a screen, draws blitter rectangles, blits the card, reads every pixel back and compares it with
the planar data (12 checks), then prints `SHOT` so `esharp run --headless --screenshot out.png` can save the emulator display
(see [images/phase3-screen-a1200.png](images/phase3-screen-a1200.png), Kickstart 3.1 on an A1200).

| Item | State |
|---|---|
| `Video` (`ESharp.Platform`): `Open(w, h, depth)`, `Close`, `SetColor`, `SetPalette`, `FillRect`, `GetPixel`, `Blit`, `WaitFrames` | Done on the Amiga: a quiet custom Intuition screen with a borderless backdrop window (system friendly, no hardware takeover), graphics.library `RectFill`/`BltBitMapRastPort`. 4 bits per channel (OCS) colours, palette-index pixels |
| Host fake of `Video` | Done: software framebuffer (`Video.HostPixels`, `HostPalette`), same planar decoding, so logic and asset tests run under dotnet |
| `intuition.library` / `graphics.library` bindings | Minimal set for the above; LVOs and tag values were read back from the NDK headers with the m68k compiler, not from memory. Struct offsets (Window->RPort 50, Screen->ViewPort 44) are hard-coded the same way |
| Asset pipeline: `assets.json` + PNG -> `Assets.g.cs` (`esharp assets <dir>`) | Done: own PNG reader (no dependencies), rounds to 12-bit colour, exact mapping when it fits the colour budget (2..32) or median cut otherwise, planar chip data (`[ChipRam]`, rows padded to 16 pixels) and `NamePalette`/`NameWidth`/`NameHeight`/`NameDepth`. Transparent pixels become entry 0 (black), and `"mask": true` also writes `NameMask`, a one-plane mask (1 = opaque) for masked blits. **Dithering, ILBM and sound inputs: see "Asset dithering, ILBM and sound assets" below**, and the step is incremental (below): `esharp build`/`run` regenerate `Assets.g.cs` when it is missing or older than `assets.json` or a listed input file (PNG, ILBM, WAV, 8SVX), and you commit it (a test fails if the committed file drifts). **Build step (new):** `AssetBuilder.Sync` compares mtimes (inputs: the manifest and every PNG it lists, including manifests of `esharp.include` directories) against `Assets.g.cs`; when stale it regenerates and rewrites the file only if the text changed, otherwise it just touches it, so a no-op build writes nothing and the working tree stays clean. `esharp assets` uses the same logic (`--force` rebuilds regardless). `dotnet build` of a csproj does not run the sync (an MSBuild target was drafted and dropped untested: it missed PNGs outside the project directory and a newly created file); use `esharp build`, `run`, `test` or `assets`. A malformed `assets.json` is reported as an `esharp:` error, not a crash. An input with a future time (clock skew) costs one regeneration, then builds are no-ops again. `esharp test` runs the same sync as build/run. Unit-tested: fresh file untouched, missing file regenerated byte-identical, touched PNG (same pixels: touched only; changed pixels: rewritten), newer manifest is stale. **Verified through the CLI (host only):** after the full `dotnet test` (160 passed) on the rebased branch, `esharp build samples/screen --emit-only` on a clean tree left the working tree clean; touching `card.png` (or the shared `samples/shared/cards-hd/assets.json`, reached through `samples/cards/esharp.include`) refreshed `Assets.g.cs` without changing its text (tree clean, next build a no-op); a copy of `samples/screen` in a temp directory with one pixel changed in its PNG got a different `Assets.g.cs`; `samples/cards` built for m68k with the docker image. Unit-tested since review: malformed manifests give an error message, a future-dated input settles after one build, `esharp.include` directories and a file argument are synced. Not verified: running the cards binary after this change, a real clock-skewed checkout. The chip budget report already counts the planes |
| `fixed` statement over arrays and spans in the emitter | Done |
| Library code is only emitted when used | Done (`ReachableMethods`): all of the user's own code is still emitted and checked, but library methods and statics only when reachable. Without this a console program would pull in every library header |
| `esharp run --headless --screenshot <png>` | Done: waits for the program to print `SHOT`, captures the Xvfb display with `xwd` and writes a PNG. `Dos.FlushOutput()` is needed because output redirected to a file is buffered until the program ends |
| Host tests | Done: host run of the sample, PNG round trip, quantiser, planar packing, stale `Assets.g.cs` check, generated C contains the library calls and `__chip` data |
| m68k: `samples/screen` in FS-UAE 3.2.35 | Done: 12/12 on Kickstart 2.04 (A500+) and 3.1 (A1200), also built with `--debug`; the screenshot shows the rectangles and the card. All other samples re-run on both ROMs after the library pruning change: unchanged |
| Not verified | Parity of the generated C against host gcc for the screen sample (no graphics shim), PAL/NTSC and screen modes other than the default, closing the program while a window is open, AmigaOS 4 / MorphOS (`OpenScreenTagList` etc. unchanged there but unrun), real hardware |

## Language additions for card games

Found by comparing what a real card game needs (heavy use of collections, tuples, patterns and out-parameters) with what the emitter did. All verified by tests that compile the C and run it with host gcc, and by `samples/language` (15 checks) on host, Kickstart 2.04 (A500+) and 3.1 (A1200), also with `--debug`.

| Item | State |
|---|---|
| `ref`, `out`, `in` parameters (passed as C pointers); `out var x`, `out int x`, `out _`, `in` with a temporary | Done. Arguments must be variables, fields or array elements (not properties). Constructors with ref parameters are still `ES0099` |
| Value tuples: return values, parameters, locals, `Item1`/named elements, `(a, b) = (b, a)`, `var (x, y) = f()`, discards | Done: each tuple type is a C struct (`ES_Tuple_int_int`). A tuple as a struct field, tuple equality and nested deconstruction are not supported |
| Patterns: constant, relational, `and`/`or`/`not`, `var`, declaration, property (fields and properties, `A.B:`), positional, list, `when`; `is`, `switch` expressions and `switch` statements | Done (GNU statement expressions in the C; gcc only, which is all we target). No match in a switch expression traps instead of throwing. Positional and list patterns and pattern `case` labels since issue #12 (see "Positional and list patterns"). Type tests are class tests only (interface or struct targets are `ES0099`) |
| Local functions (no capture) | Done: a local function that uses a local, parameter or `this` of the method around it is `ES0099` |
| Static constructors, `: this(...)` constructor initializers (structs) | Done. **Static constructors run once at start-up in type order**, not lazily like in C#. Before this fix a static constructor was silently ignored |
| `Vec<T>` (`ESharp.Platform`, named so as not to clash with `System.Collections.Generic.List<T>`): growable list of an unmanaged `T` | Done: `Add`, `Insert`, `RemoveAt`, `SwapRemoveAt`, `Clear`, indexer, `Count`, `Capacity`, `AsSpan`, `Destroy`; no exceptions (false / zero value). Counted in the chip budget by its initial capacity only |
| `Dos.PutInt` | Done |
| String interpolation and concatenation (`$"score {n,5:D3}"`, `"a" + n`) | Done: the text is formatted into one of 8 static 128-byte buffers and returned as a `string`, so it is **valid only until 8 more strings have been formatted** (use it at once: draw it, print it; copy it if it must stay) and is cut at 127 bytes. Holes: integers including `long`/`ulong` (the 64-bit ones use software division on a 68000, so they are slow; alignment `,N`/`,-N`, formats `D<n>`, `X<n>`, `x<n>`), `char`, `bool` (`True`/`False` like C#), `string`, enums (the member's name, or the number for a value that is none of them, like `ToString`; `D`/`X` give the number); `{{` and `}}` work; constant strings are folded. Not done (ES0099): floats and fixed-point, `[Flags]` combinations (printed as a number, not `A, B`), other format strings, `string.Format`, `StringBuilder`. Parity with dotnet is checked in `samples/language` |
| Heap classes (non-static `class`) | Done: fields, auto-properties, methods, constructors, static members, `new` (also with an object initializer), `null`, reference equality, arrays of references, `this`. A class variable is a C pointer (`Foo *`), `new` is `calloc` (stops with exit 20 when memory runs out), and objects are freed with `Heap.Free(ref obj)` (frees and sets the variable to null); objects never freed are released when the program ends. There is no garbage collector: keep one owner per object. **Single inheritance with `virtual`/`abstract`/`override` (see the inheritance section above), no generic classes (ES0008), no instance field initializers in classes (ES0099, assign in the constructor)**. Every E# type gets a prefixed, namespaced C name (`Es_<namespace>_<type>`, see the C names section), so a class may be called `Node`, `List` or `Window` like an NDK struct, and two types with the same name in different namespaces are apart |
| Interfaces | Plain-method interfaces (no properties, events, generic methods or default implementations). Two ways to use them: as generic constraints (`where T : IScorer`, monomorphised, any struct or class, static dispatch), and as **variable types** (`IRules rules = new FlatRules();`, fields, parameters, returns, statics): an interface value is a C struct of the object pointer and a table of functions, so the call is dynamic. Only **classes** can be converted to an interface value (a struct would need boxing: ES0099). `x == null`, `x is null`, `default`, equality (same object) and `Heap.Free(ref x)` (frees the object) work. Not done: interface-to-interface conversion, inheritance between interfaces, explicit casts back to the class (`as`/`is Foo`) |
| Lambdas, method groups and delegates (`Func<>`, `Action<>`, `Predicate<>`, user `delegate` types) | Done: a delegate is a function pointer plus an environment; lambdas may capture locals, parameters and `this` (by reference, on the stack: they cannot be returned or stored, see the top of this file). Calls `f(x)` and `f.Invoke(x)`, `null` and `== null` work. Not done: delegates as struct fields, `?.Invoke`, `delegate { }` anonymous methods, instance-method groups, combining delegates (`+=`), local functions that capture |
| LINQ | Over arrays and `Enumerable.Range`, as fused loops: Where, Select, Take, Skip, Count, Sum, Min, Max, Any, All, First, Last, Contains, Aggregate, OrderBy/ThenBy (stack copy, stable insertion sort) and simple queries (see the top of this file). No `ToArray`/`ToList`, no sequences stored in variables |
| Still missing (needed by bigger games) | Closures that outlive their method, `OrderBy`/`ToArray`/`ToList`, float values in strings, `is`/`as` against interfaces, inheritance from generic or library classes |

## Phase 4: card demo (in progress)

`samples/cards` is a playable higher-or-lower game on the Amiga screen. It is a **proof of the building blocks**, not the real target game, so the phase 4 gate (the real game's logic playable on an Amiga) is not met yet.
Mouse: Higher/Lower buttons; Esc quits; it also quits after 10 seconds without input (so headless runs end).
Screenshot: [images/phase4-cards-a1200.png](images/phase4-cards-a1200.png) (Kickstart 3.1, A1200, after scripted play).

| Item | State |
|---|---|
| `Video.BlitRegion` (sprite sheet sub-rectangle) and `Video.BlitMasked` (`BltMaskBitMapRastPort`, minterm 0xE0) | Done on the Amiga and the host fake; verified pixel for pixel against the sheet on both Kickstarts |
| `Input` (`ESharp.Platform`): `Poll`, `MouseX/Y`, `LeftDown/LeftPressed/LeftReleased`, `LastKey` (raw key codes), `KeyEscape/KeySpace/KeyReturn` | Done: IDCMP mouse buttons, mouse move and raw keys on the `Video` window (IntuiMessage offsets, tag values and codes read back from the NDK headers). Host fake takes `Input.HostMove/HostPress/HostRelease/HostKey` |
| Asset masks (`"mask": true` in `assets.json`) | Done, with a test that opaque black is distinct from transparency. The quantiser now lets opaque black use palette entry 0 (it used to be mapped to the nearest other colour when the image had transparency; `samples/screen/Assets.g.cs` was regenerated) |
| `tools/CardArt`: draws `samples/cards/cards.png` (52 faces, back, arrows, star, cross; original art) | Done; run it to regenerate the sheet |
| Card library (`src/ESharp.Platform/Cards.cs`): `Rng` (xorshift32, seeded shuffle), `Deck` (suit count is a parameter, 8 suits tested, `Sort`), `Rummy` (`Points`, `IsMeld`, `Deadwood`, `Melds`, `BestDiscard`: sets and runs, ace low, best split by exhaustive search over up to 10 cards) | Done, shared by `samples/cards` and `samples/rummy` |
| `Dos.PutInt` | Done |
| `esharp run --headless --input "click X,Y;rel DX,DY;press;key N"` | Done: XTEST events (libX11 + libXtst) after `SHOT`, before the screenshot. The emulator grabs the mouse on the first click and then reads deltas, hence `rel` (1 Amiga pixel = 2 display pixels at the default window size) |
| Emitter: `null` as a span argument | Fixed: it was emitted as a call to a BCL conversion that does not exist in C (found when the screen sample failed to compile after the library change); implicit conversions are only applied for operators written in E# source |
| m68k: `samples/cards` in FS-UAE 3.2.35 | Done: 15/15 self-checks plus scripted play (3 clicks on Higher/Lower, right answers, Esc quits cleanly back to Workbench) on Kickstart 2.04 (A500+) and 3.1 (A1200, also built with `--debug`). All other samples re-run on both ROMs after the changes: unchanged |
| Host tests | Done: host run with scripted input (19 checks), drift test for the generated sheet, emit shape (`BltMaskBitMapRastPort`, `GetMsg`), null span, mask |
| Not verified | Keyboard other than Esc, mouse moves (`MOUSEMOVE` is read but only the position is used), real mouse hardware, PPC targets, 1.x-style left-handed button swaps, timing/frame counters beyond `Delay`, how samples/sound sounds |

## Next

- Run hello on real hardware, the last open item in the phase 1 gate. The emulator side is done: AROS replacement, Kickstart 2.04 and Kickstart 3.1.
- OS4 findings from the first VM runs (see "AmigaOS 4.1 VM runs"): lowlevel port types, the RTG link error, the classic-only checks in memory/generics, scripted input for os4 runs. Done in wave 8: WaitTOF timing (timer fallback), the screen mode (centred), sound (clean skip; no working audio in the VM). Open: OS4 sound on real hardware.
- MorphOS runs (QEMU `pegasos2` can boot MorphOS too; not tried) and PowerPC real hardware.
- `esharp test` follow-ups: other targets (morphos, aros) and the Amiga program's return code (today only the ok/FAIL lines are compared; the os4 pipe hides it).
- Memory management ([MEMORY.md](MEMORY.md)): step 1 (`Mem.AllocChip`/`AllocFast`/`Free`, `Arena`) is done; step 2 (`Pool<T>`) and most of step 3 (`--debug`: bounds checks, arena and pool poisoning) are done; the chip budget report (step 4) is done for constant sizes.
- Phase 4 (rest): a bigger card game (rummy-style: melds, bots), a shared card library, timing, dithering and IFF/sample assets.

## AmigaOS 4.1 VM runs (wave 7: `esharp run/test --target os4`)

The os4 target now runs unattended in a QEMU Pegasos II VM with AmigaOS 4.1 Final Edition (Updates 1-3 installed; kernel
54.57, exec 54.57, dos 54.136), which Dave owns; no AmigaOS file is in the repo. `ESHARP_OS4_VM` names the VM directory
(`bboot`, `Kickstart.zip`, `amiga.img`, optional `qemu-data/`); unset, the CLI says "set ESHARP_OS4_VM to your AmigaOS 4.1
VM dir". Details and the exact hook lines: `tools/os4vm/README.md`.

How it works: the program, a script `autorun` and a helper `estype` (`tools/os4vm/estype.c`, embedded in the CLI and
compiled with `ppc-amigaos-gcc`) go on an ISO labelled `ESTEST` (Rock Ridge mode 755, so the files are executable in OS4).
QEMU boots a new qcow2 overlay of `amiga.img` (so test runs never write the installed disk) with `-append serial` (BBoot
passes it to the kernel, which then sends its debug output to the serial port) and `-display none`. A one-time hook at the
end of `S:User-Startup` executes `ESTEST:autorun` when that volume is present, before `LoadWB`. The script runs
`program | estype`; estype writes each line with `DebugPrintF`, prefixed `ES: `, between start and end markers that carry
a per-run nonce. The host polls `obj/os4/run/serial.log`, takes a `screendump` through the monitor socket after `SHOT`
(PPM -> PNG), and quits QEMU at the end marker or the timeout. Trace output (`--trace`) now uses `DebugPrintF` on
AmigaOS 4 (it was a no-op stub) and goes to the `--serial` file with the other non-program lines.

Measured: boot to the program about 9 s from the QEMU start; a whole `esharp test --target os4` (host run, os4 build,
CD, boot, run) 12-16 s per sample. Booting by hand to the Workbench screen takes about 23 s.

Decisions and things found on the way:
- `SER:` reaches the serial file too, but serial.device dropped characters (a typed script lost parts of most lines);
  `DebugPrintF` output was complete, so the program's output goes through estype.
- The AmigaOS 4.1 shell pipe delivers one empty line before the first command's output (`Echo "a" | estype` shows it; a
  redirect to a file does not): estype drops exactly one leading empty line on standard input (checked: `Echo ""` still
  gives one empty line).
- The hook first used `Run` (asynchronous): Workbench and its WBStartup window then came up over the cards sample's
  screen, so the screenshot showed Workbench. It now executes the script synchronously before `LoadWB`; the GadTools
  sample still opens its window on the Workbench screen (intuition opens it on demand).
- AmigaOS 4 renamed `Flush` and `DeleteFile` (`FFlush`, `Delete`): screen, cards and sound did not link. The generated C now
  includes the SDK's `dos/obsolete.h` after `proto/dos.h` on `__amigaos4__` (no change for other targets).
- Warm reboots do not work in this VM; every run is a fresh boot. Only one VM may run at a time: the CLI refuses while a
  `qemu-system-ppc` uses the VM directory, and esharp runs wait for each other (`/tmp/esharp-os4vm.lock`).
- Cleanup: the QEMU started by a run, and its `/tmp/esharp-os4-<id>` dir, are also removed on SIGTERM/SIGHUP/SIGINT and process
  exit (not only in the normal path); `/tmp/esharp-os4vm.pid` lets the next run reap a qemu orphaned by SIGKILL (unit test with
  a fake qemu). `install-hook`/`remove-hook` now wait 15 s + 15 s for the disk before the power-off `quit`, keep
  `amiga.img.bak-<time>`, and verify with a second boot before reporting success (run 2026-11-11 on a scratch copy of the disk, not the real amiga.img: remove-hook then install-hook, each with backup, commit and a passing check boot; the real disk was not touched. The explicit claim that 30 s is enough for the filesystem to flush is not verified, only that the result survived a reboot).

The hook (installed 2026-11-11 with `python3 -I tools/os4vm/os4vm.py install-hook`, which typed one command through the
monitor's `sendkey`, checked the result on the serial output and then ran `qemu-img commit` of the overlay; the original
file is kept as `S:User-Startup.pre-esharp` and `remove-hook` puts it back; both commands were run once):

```
;BEGIN ESHARP-TEST (E# os4 test harness hook, see tools/os4vm/README.md in the E# repo)
Assign >NIL: ESTEST: EXISTS
If NOT WARN
  If EXISTS ESTEST:autorun
    Execute ESTEST:autorun
  EndIf
EndIf
;END ESHARP-TEST
```

Read-only backup of the disk before any change: `amiga-backup-2026-11-11.img` in the VM directory (not touched).

Verified (`esharp test samples/X --target os4 --docker esharp-amiga-toolchain`, `ESHARP_OS4_VM` set, one VM at a time):

| Sample | Host | OS4 | |
|---|---|---|---|
| hello | prints `Hello from E#!` | same | `test` reports FAIL only because hello has no ok lines (as on m68k) |
| phase2 | 32 ok | 32 ok | agree |
| language | 35 ok | 35 ok | agree |
| collections | 20 ok | 20 ok | agree |
| generics | 24 ok | 23 ok, 1 FAIL | `impossible pool fails cleanly`: `new Pool<Card>(100000000, true)` asks for about 1.3 GB, which the 2 GB VM grants |
| lambdas | 16 ok | 16 ok | agree |
| linq | 37 ok | 37 ok | agree |
| refs | 18 ok | 18 ok | agree |
| inherit | 24 ok | 24 ok | agree |
| operators | 20 ok | 20 ok | agree |
| properties | 18 ok | 18 ok | agree |
| memory | 19 ok | 17 ok, 2 FAIL | `chip block is in the chip address range` (the check `< 0x200000` assumes classic chip RAM; OS4 has none) and `free returns the chip RAM` (OS4's `AvailMem(MEMF_CHIP)` did not grow back by 4096 after the free) |
| dates | 19 ok | 19 ok | agree |

Platform samples never run on OS4 before (`esharp run ... --target os4 --screenshot`):
- screen: 17 ok, screenshot viewed (rectangles, blitted card, font). The 320x200 screen shows in the top-left corner of a
  640x480 display (mode selection on the RTG system; not scaled). Now centred: see "Sound, timing and screen on OS4" below.
- cards: 15 ok, screenshot viewed (table, cards and buttons drawn; it then ends after 10 s without input).
- sound: 12 ok, 1 FAIL: `audio.device opens` (AmigaOS 4's audio.device is AHI's, and the VIA AC97
  driver reports "soft resume timed out" in QEMU; "no audio.device in this install" was wrong, see below). File loading and the sample checks pass.
- timing: 8 ok, 1 FAIL: `ten frames owe about ten steps at the frame rate`; "50 WaitVBlank calls took 0 ms", so `WaitTOF`
  does not wait on the sm501 display in QEMU (the sample's "not real time" note is printed too). The E clock part works.
  Fixed in wave 8 (below).
- trace (`--trace --serial f`): output `trace sample done, sum 14`, and the serial file has every Trace line
  (`trace start` ... `trace end`).
- Also run once: aga 6 ok, asl 3 ok, fonts 10 ok (topaz and a disk font), iff 10 ok, keys 8 ok, names 12 ok, rummy 28 ok,
  gui 2 ok (the window on the Workbench screen in the screenshot; it then waits for a click, so the run ends at the
  timeout: no `--input` for os4), assets 15 ok and 1 FAIL (`audio.device opens` again), controls 10 ok and 2 FAIL
  (lowlevel.library is present, but `port 1 set to a CD32 pad reads as a pad` and `port 1 then reads as a joystick`
  fail: the port reads kind 0), hwinfo prints exec 54.57, 68020 "yes", AGA "yes" and chip = fast free RAM (OS4 answers
  the classic questions that way).
- rtg does not link: `ICyberGfx` is undefined (the cybergraphics interface is not set up for os4).
- Workbench still boots normally without the test CD (screenshot of the Workbench screen after 35 s, via
  `os4vm.py start` on an overlay of the hooked disk), and with a CD labelled ESTEST without `autorun` (the remove-hook boot; checked with the first, `Run` form of the hook only).
- Regression on m68k after the emitter changes: `esharp test samples/language --target m68k` with Kickstart 3.1 on A1200:
  35 ok on both sides; `samples/trace` with `--trace --serial` prints every Trace line as before. 292 unit tests pass
  (`Os4VmTests`: settings, autorun script, serial parsing, QEMU arguments, the embedded estype, the os4 C; plus a VM run
  that is skipped when `ESHARP_OS4_VM` is unset, and passed here with it set).

### Fixes from the first VM runs (wave 8, issue #57)

| Sample | Before (OS4) | After (OS4) | Host, m68k A1200/3.1, hosted AROS, SDL |
|---|---|---|---|
| rtg | did not link (`ICyberGfx`) | 7 ok, 0 FAIL, host agrees | host 7; m68k 5 ok (no card, fallback checks); unchanged |
| memory | 17 ok, 2 FAIL | 19 ok | host 19, m68k 19, hosted AROS 19, SDL 19 |
| generics | 23 ok, 1 FAIL | 24 ok | host 24, m68k 24, hosted AROS 24, SDL 24 |
| controls | 10 ok, 2 FAIL | 12 ok (host 12) | m68k 9 ok (no lowlevel in Libs:), unchanged |
| gui, cards, keys with `--input` | no input | see below | |
| language (smoke) | 35 ok | 35 ok | m68k and AROS not re-run for this |

- rtg: `cybergraphics.library` has no interface in `-lauto`, so `ICyberGfx` was undefined. The emitter now defines it for
  `CyberGfxBase` on `__amigaos4__` only: `CyberGfx.SetBase(lib)` does `GetInterface(lib, "main", 1)` and `SetBase(null)` does
  `DropInterface` (the library is still opened and closed by `Video` around each use, as before; other targets expand the
  macro to the old plain assignment). On OS4 `Video.HasRtg(320,200)` is true (every OS4 screen is on the graphics system), the
  RTG screen opens and draws, and `Video.Open`'s screen reports `IsRtg()` true too, so the sample skips "the native screen is
  not RTG" there (`#if PPC`, prints a note, the check name stays so the counts agree).
- memory: the chip address range check (`< 0x200000`) and "free returns the chip RAM" are skipped on PPC (each prints a `note:` line and then `ok`
  with the same name so the counts agree). Observed on OS4 in the VM: the free chip figure did not come back up by 4096 after
  `FreeMem`; the cause (separate pool or allocator caching) was not determined. MorphOS is not verified (no run). m68k and AROS (`!AROS` already skipped the first) still check the second.
- generics: the 1.3 GB pool succeeded in the 2 GB VM. `Pool` now refuses a capacity whose block does not fit in 32 bits
  (before, `capacity * itemBytes` wrapped to a small block), and the sample asks for 2,000,000,000 items, which fails on every
  target without allocating.
- controls: with no pad or joystick attached, OS4's `ReadJoyPort` answers `JP_TYPE_NOTAVAIL` (kind 0) for port 1 even after
  `SetJoyPortAttrsA` accepted a forced type (it returns true). That is the documented "nothing usable here" value, so on PPC
  the two read-back checks accept it and print a note instead of failing; m68k, AROS and the host still require the forced kind. Whether a
  real pad on real OS4 hardware reads as a pad is not verified (no pad in the VM).
- `--input` for os4 (`Os4Input.cs`): see `tools/os4vm/README.md`. Verified on the OS4 VM: `samples/gui` `--input "click
  208,101"` shows `Hello, Amiga!` in the screenshot and prints `ok Hello clicked`; `"click 208,101;click 57,45"` (Hello,
  then the close gadget) ends the program (`ok a click arrived`, `ok the reply is Hello, Amiga!`, `ok the close gadget arrived`,
  `ok the window is freed`); `samples/cards` `"click 80,145;click 240,145"` prints `guess higher: right`, `guess lower: right`
  (the screenshot shows the new cards and the pointer on the button) and `"click 80,145;key 69"` prints `quit` (Esc);
  `samples/keys` `"click 400,300;key 96+32"` gives `pressed key 32 (qualifier 32769), typed 65` (Shift+A). Clicks land on the
  asked pixel without a scale factor. Keys outside the table (`Os4Input.KeyName`) are rejected with a message.
- Review nits from #56: the serial parser now takes `ES: ` only at the start of a line (stray control characters before it
  are skipped; the kernel lines seen in real logs never carried a prefix in front of it), with a test; the VM unit test is a
  real xunit skip unless `ESHARP_OS4_VM_TESTS=1` (it passed with it set: 13 s); `os4vm.py start/install-hook/remove-hook` take
  the same lock file with `flock` and wait (tested against another `flock` holder; interoperation with the CLI's .NET
  `FileShare.None` lock is not verified by a run, only by .NET's flock use on Unix).
- `dotnet test`: 296 passed, 1 skipped (the VM test, opt-in), 0 failed (was 292 plus the new tests for the parser, the input mapping and the cybergraphics C).

Not verified / not done:
- PowerPC real hardware (Pegasos II, AmigaOne, X5000, ...). MorphOS: still not run.
- The program's return code (the pipe hides it), a crash (it should end at the timeout with the Grim Reaper waiting; not
  provoked).
- All os4 findings from issue #57 are addressed: the ones above in this section, and sound, timing and the screen mode in the
  next section (sound in the VM is a QEMU limitation and is skipped cleanly).
- macOS or Windows hosts for the harness (it uses `/proc` and `/tmp`; Linux only so far).

### Sound, timing and screen on OS4 (wave 8, issue #57)

**Sound: a VM limitation; E# now degrades cleanly.** A probe program in the VM (a throwaway, `List`/`Version` through
dos `Execute` and test `OpenDevice` calls) found: `DEVS:audio.device` 53.3 and `DEVS:ahi.device` 6.6 are installed, with the AHI
drivers in `DEVS:AHI` (via-ac97, sb128, emu10kx, Envy24, CMI8738, cs4281, ...), their `DEVS:AudioModes`, `ENV:Sys/ahi.prefs` and
the AHI prefs editor. `OpenDevice("ahi.device", AHI_NO_UNIT)` succeeds (AHI itself loads), but `ahi.device` unit 0 and `audio.device`
both fail (-1, IOERR_OPENFAIL), and at that moment the serial log shows `[VIA-AC97] Error: soft resume timed out!`: the
driver for the Pegasos II's on-board VIA AC97 (QEMU's `via-ac97` function of the emulated VT8231) does not get the codec to
answer, so AHI unit 0 cannot open and audio.device, which plays through it, cannot either. That is the emulated sound
hardware, not E#. Decision: keep the audio.device path on AmigaOS 4 (it is the OS's own AHI-backed audio.device, so programs
need no second code path; an `ahi.device` path would fail the same way here because it uses the same unit 0 driver) and report
unavailability cleanly: `Sound.Available` is false, `samples/sound` and `samples/assets` print a note and `skip` lines.
Not tried: other QEMU sound devices (QEMU has `ES1370`, which AHI's sb128 driver may support, but that needs a QEMU argument in
the harness and AHI prefs for unit 0 on the VM disk; out of scope here). Not verified: sound on real AmigaOS 4 hardware.

**Timing: fixed in `Frame.WaitVBlank`** (timer.device fallback, see "Frame timing").

**Screen: centred in the nearest mode.** A second probe listed the display database: the sm501 board offers
640x480, 800x600, 1024x768, 1280x720, 1280x800, 1280x960 and 1280x1024 (CLUT8 and RGB16), and the classic mode IDs (`PAL:640x480
LowRes` and so on) are aliases of 640x480 board modes; `BestModeIDA` for 320x200 (depth 4, 5 or 8) answers `PAL:640x480 LowRes`.
There is no 320-wide mode, and the OS does not scale a smaller screen, so `Video.Open` on AmigaOS 4 (`#if AMIGAOS4`) now asks
`BestModeIDA` for the nearest mode, reads its nominal size (`GetDisplayInfoData`, `DTAG_DIMS`; offsets measured into `Layout.g.cs`),
opens the screen at that size with `SA_DisplayID`, and puts the backdrop window, which every drawing call and the mouse
coordinates use, in the middle. m68k (PAL lowres, window at 0,0) and AROS are unchanged; a card with a 320x200 or 320x240 mode
would get it and an offset of 0 (not verified: no such card here). Screenshots viewed: screen before (top-left corner) and after
(centred), cards, rummy, aga and assets centred on the 640x480 display.

Verified (AmigaOS 4.1 VM, `ESHARP_OS4_VM` set, `--docker esharp-amiga-toolchain`):

| Sample | Before (wave 7) | After |
|---|---|---|
| sound (`test`) | 12 ok, 1 FAIL | 13 ok, 0 FAIL, 8 skipped; host and OS4 agree |
| assets (`test`) | 15 ok, 1 FAIL | 15 ok, 0 FAIL, 2 skipped; host and OS4 agree; centred |
| timing (`test`) | 8 ok, 1 FAIL | 10 ok, 0 FAIL; 1000 ms for 50 frames, 64 frames paced; host and OS4 agree |
| screen (`test`) | 17 ok (corner) | 17 ok, centred; host and OS4 agree |
| cards (`run`) | 15 ok | 15 ok, centred |
| rummy (`run`) | 28 ok | 28 ok, centred |
| aga (`run`) | 6 ok | 6 ok, centred |

Regression elsewhere: A1200 / Kickstart 3.1 (40.069) in FS-UAE (`esharp test --target m68k`): sound 21 ok, timing 10 ok, screen
17 ok, assets 17 ok, all agreeing with the host; `run samples/screen` screenshot unchanged (top of the PAL display); `run samples/cards`
with the AGENTS.md input: 15 ok, `guess higher: right`. Hosted AROS (nightly 20261106, `--target aros` builds): timing 10 ok
(1000 ms, no pacing), cards 15 ok with a scripted click (`guess lower: wrong`), screenshot unchanged (top-left). SDL headless
(`esharp test --target sdl --docker esharp-sdl`): timing 10 ok, sound 21 ok, agreeing with the host. 299 unit tests pass.

## Known limits of the emitter

Reported as `ES0099` rather than miscompiled: capturing lambdas that would outlive their method, delegates in struct fields, `is`/`as` against an interface or struct, ref returns from properties, indexers, delegates and virtual or interface members, `foreach` over anything but arrays, spans and struct/class GetEnumerator types (interface enumerators, `Dispose`, ref `Current`), the pattern forms listed under "Positional and list patterns" (extension `Deconstruct`, `ITuple`, a slice capture that would copy), records, `==` on tuples, `??` on nullable value types; `[InlineAsm]` and `[Interrupt]` (done, see their section) still give it for: operands that are not 32-bit-or-smaller integers or pointers, instance methods, `[Interrupt]` on PowerPC and sdl, and `&Method` of anything but an `[Interrupt]` method.

Two types whose C names meet after joining with `_` (namespace `A_B` class `C` and namespace `A` class `B_C`) are reported
(ES0099); rename one.

## Targets

CPU and OS are separate axes. `AMIGA` is defined for every Amiga-family target; narrow with `M68K` / `PPC` and `AMIGAOS3` / `AMIGAOS4` / `MORPHOS`.

| `--target` | Compiler | Symbols |
|---|---|---|
| `m68k` (default) | `m68k-amigaos-gcc` 6.5.0b | AMIGA M68K AMIGAOS3 |
| `os4` | `ppc-amigaos-gcc` 11.5.0 | AMIGA PPC AMIGAOS4 |
| `morphos` | `ppc-morphos-gcc` 15.2.0 | AMIGA PPC MORPHOS |

PowerPC notes: both PPC targets are big-endian, so asset formats need no change. `[ChipRam]` is a no-op on PPC (chip memory is allocated at run time there). `[InlineAsm]` takes PowerPC code in `Ppc`; without it the PPC targets run the method's C# body. `[AmigaLib]` offsets are 68k library vectors; the PPC targets call through the same `proto/*.h` names, so the emitter output is identical. Not yet covered: WarpOS/PowerUP (no prebuilt toolchain found) and AROS.

## Asset dithering, ILBM and sound assets (samples/assets, issue #13)

`assets.json` gained three things; existing manifests produce byte-identical output (the drift tests still pass).
- `"dither": "fs"` (Floyd-Steinberg, left to right) or `"ordered"` (4x4 Bayer) on an image, default none. It works from the full 8-bit source colours when colours are reduced (median cut, the 12-bit OCS rounding even when the rounded colours fit the budget, and AGA). An image whose colours map exactly (each source colour equals its palette entry) is left alone, and transparent pixels neither receive nor give error, so the mask and index 0 are unchanged.
- An image `file` may be an IFF ILBM (`.iff`, `.ilbm`, `.lbm`; detected by content). `Ilbm.Read`: BMHD, CMAP, CAMG (read; HAM and Extra-Half-Brite are rejected with an `esharp:` error naming them), BODY uncompressed or ByteRun1, 1 to 8 planes (24-plane true colour also reads), masking none, mask plane (becomes alpha) or transparent colour; lasso masking and other compressions are rejected. `Sync` treats these files like PNGs (mtime inputs), also the sound files.
- `"samples": [ { "name": "Beep", "file": "beep.wav" } ]` (WAV PCM 8/16 bit mono/stereo, or 8SVX raw/Fibonacci-delta) writes `BeepData` (`[ChipRam]` 8-bit signed mono, padded to an even length of at least 4), `BeepLength` and `BeepRate`. More than 131070 samples are cut with a `warning:` line. To play: build a `Sample` from the array (`fixed`) and `Sound.Play(in s, Sound.PeriodForRate(rate), vol, n)`.

`samples/assets` shows the three gradient versions (Floyd-Steinberg, ordered, flat) and the ILBM badge (a masked 37x24 picture, odd width, ByteRun1) and plays the beep; its input files are made by `tools/AssetSamples` (original, generated in code). It prints 17 `ok` lines. Unit tests (`AssetFormatTests`) cover dithering on gradients, transparency and mask, exact mappings, ILBM (both compressions, odd widths, mask, transparent colour, CAMG, HAM/EHB rejection, truncation, 24 planes), WAV and 8SVX reading, the manifest, the length cap, and Sync with ILBM and sound inputs. The sample is built into `tools/hwpack` (17 ok expected).

Verified:
- A1200, Kickstart 3.1 in FS-UAE: 17 ok, screenshot viewed.
- After the review fixes: 248 unit tests pass, and the A1200 / Kickstart 3.1 run of samples/assets gives 17 ok again (its generated Assets.g.cs did not change). Hosted AROS was not re-run after the fixes (no platform code changed).
- Hosted AROS (nightly 20261106, run on a private copy of the AROS directory): 17 ok, screenshot taken but not viewed.
- The hwpack script was edited to include `assets` but it was not built or run.

Not verified: sound was not audible (no OpenAL device in the emulator; only Busy/Stop was checked), a malformed-ILBM size above 4096x4096 pixels is rejected (unit-tested only), ILBM files written by real paint programs (only files written by our own writer in tests, so odd real-world files such as 4-bit CMAPs are untested), dithered output of photographic images beyond the generated gradient, 8SVX with several octaves, real hardware.

## Emitter and SDL follow-ups (issue #12)

- A constant string compare (`const string K = "x"; static bool B = K == "x";`) is folded to `true`/`false` in C, so it is valid in a static initializer (it emitted an `es_streq` call).
- The `??` ES0099 names the case: "nullable value type" only for `T?`; other left types say what is supported.
- `a.Equals(b)`, `object.Equals(a, b)` and `object.ReferenceEquals(a, b)` on class references are reference equality, in a program where no class declares `Equals(object)`. ES0099 (with the reason) for: `Equals` on a struct or enum (`ValueType.Equals`), a user `Equals(object)` called or declared in the program (E# has no `object` type, so a virtual call of it is not supported), and boxed arguments. Not done: a virtual call of a user override.
- Member-level C name collisions (class `A_B` member `C` against class `A` member `B_C`: static fields, static properties, enum members, methods, constructors) are reported as ES0099 on the later declaration. They are not mangled apart.
- SDL: `es_sdl_blit` gets the data lengths and stops the program with a message when the planar data (depth planes) or the mask is too short, like the host fake's exception; ES0102 also explains CS0117 for a member that exists only in the Amiga build of a class sdl has (`Video.Window`, `Dos.DateStamp`); the key map has the keypad, Help (0x5F), and F11/F12 as 0x4B/0x6F (no classic Amiga keys; the codes USB keyboard drivers use: not checked against hardware).
- Checked: `dotnet test` (unit tests with gcc), `esharp test samples/language` host vs m68k (A1200, Kickstart 3.1) and host vs sdl (37 ok each), `samples/names` host vs m68k (12 ok), `samples/cards` on sdl headless with input. Not verified: the new keypad/F11/F12/Help keys on a real SDL window or with XTEST; the blit-length stop on SDL was only checked in the generated text, not run with a short buffer; hosted AROS and OS4 were not run (no target-specific change).
