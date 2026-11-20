# Status

## Minimum system: Amiga 1200 (decision)

The minimum system is now an **Amiga 1200: 68020, AGA, 2 MB chip RAM, Kickstart 3.1, hard disk install**. Classic builds use `-m68020` (target table, smoke test), the CLI defaults to `--model A1200`, and the docs and `AGENTS.md` say so. Planned beyond classic 68k: PowerPC (AmigaOS 4, MorphOS), RTG cards and AROS. Support for A500/A600, 68000 and Kickstart 1.x/2.x is dropped. Older "Kickstart 2.04 / A500+" results below are historical: they were true when run and are not part of the matrix any more.
- Cleanup done: the host fake's default chip budget is 2 MB (also in the host-gcc exec shim), and comments and `docs/MEMORY.md` no longer describe a stock A500. 136 unit tests pass.
- **68020 instructions used on purpose:** `Fix * Fix` is now `(a * b) >> 16` through a 64-bit product, which gcc turns into a single `muls.l` with a 64-bit result (checked in the compiled output: no helper call); the 68000 four-partial-product version is gone. `Fix / Fix` calls a prelude function `es_fixdiv` (new `[Native]` attribute for extern methods the emitter defines itself) that uses one `divs.l` (64/32) when the quotient fits in 32 bits and the software 64-bit divide when it does not; division by zero gives 0 on the Amiga and throws on the host. Verified: all 225 pairs of 15 edge values (negative, `int.MinValue`, quotients that overflow) match an independent exact-integer reference on Kickstart 3.1 (A1200), the same computation passes under host gcc (the portable path) in a unit test, 137 unit tests pass, and the 17-sample A1200 regression gives 300 `ok` with no `FAIL`. Not verified: speed (no cycle measurement was made, so the gain is expected, not measured), 68030/040/060, and `divs.l` on real hardware.
- **AROS (m68k) via FS-UAE's built-in AROS ROM replacement, `--model A1200`, no Kickstart:** the m68k builds run unchanged (same hunk executables). 15 of 17 samples print only `ok` lines (hello, phase2, memory, generics, properties, operators, language, cards, rummy, sound, collections, rtg, inherit, lambdas, linq). **2 checks fail, both pixel read-backs in graphics:** `samples/aga` "the blitted pixels have the 24-bit colours of the image" and `samples/screen` "blitted card matches the planar data pixel for pixel". Investigated for `screen`: on the AROS ROM replacement a blit leaves a destination pixel unchanged when the source pixel's palette index is 0, as if index 0 were transparent (every wrong pixel had expected index 0 and kept the old background colour; the other 5000+ pixels were right; it happens at word-aligned and unaligned x and at different rows). Giving the blit zero-filled planes instead of NULL planes for the missing depth did not change it, so it is not the NULL-plane convention. `aga` fails on the same kind of pixel (not separately confirmed). This looks like a quirk of that AROS version's `BltBitMapRastPort` (real AmigaOS copies index 0 like any colour), not a bug in E#; no workaround was added. A real AROS install was not tried. The ROM replacement is not a real AROS install: native AROS (hosted or x86/ARM, new toolchain image) is not built or run, and nothing was run on real AROS hardware.
- **AROS x86-64 target (`--target aros`, new):** defines `AMIGA`, `AROS`, `X86_64`; compiler `x86_64-aros-gcc` 9.1.0 from the toolchain image `amigadev/crosstools@sha256:9c4e978301da6b6584d68d4014caa1fa9e2689529e79e71f7190d40d1cb62e50` (about 5.4 GB on its own; it is now also a stage of the combined `esharp-amiga-toolchain` image from `docker/Dockerfile`, which is 5.66 GB with all four compilers; `docker/smoke-test.sh` builds an x86-64 ELF with it, and `linq` and `cards` were built with `--target aros --docker esharp-amiga-toolchain`; running those builds on hosted AROS from the combined image was not repeated), with `--sysroot=/opt/x86_64-aros`. All 17 samples compile to AROS ELF executables. **Run on hosted AROS (x86-64, nightly 20261104, `linux-x86_64-system` archive, run headless under xvfb with a patched `S:Startup-Sequence`; output to a file):** all 11 pure-computation samples ran: hello, phase2 (32 ok), generics, operators, properties, collections (20 ok), inherit (24 ok), language (28 ok), lambdas (16 ok), linq (37 ok), and memory (18 ok, 1 FAIL: "chip block is in the chip address range", which assumes 32-bit chip addresses). The first run exposed a real bug: `string.Length` was emitted as `__builtin_strlen`, which became an undefined `strlen` symbol that the AROS loader resolved wrongly (garbage length, crash in `Hashing_Text`). It is now an emitted `es_strlen` helper, libc-free on every target. A booted AROS x86-64 ISO under QEMU (software emulation) reached the Workbench desktop, but no E# program was run there. Not verified: the screen, sound, file and input samples (see below), real AROS hardware, and hosted AROS i386. **64-bit pointer port (done):** tag lists are pointer-sized (`nuint[]`; the emitter now maps `nint`/`nuint` to `intptr_t`/`uintptr_t`), file handles are `nuint`, the BitMap is built with typed writes, and struct offsets (Window, Screen, IntuiMessage, IOAudio) have a 64-bit set under `#if X86_64`, measured with `offsetof()` against the AROS headers. Pool items are 8-byte aligned. On hosted AROS x86-64: `screen` passes completely (including the card blit pixel for pixel, which fails on the m68k ROM replacement), `aga` passes its fallback checks, `rtg` passes except "the native screen is not RTG" (every AROS screen is RTG, so that check cannot hold there), file loading in `sound` passes (write, load, delete), `memory` passes with its 32-bit chip-address check skipped on AROS. **Higher resolution on AROS (cards, rummy):** after the 320x200 self-checks the games open a 640x400 screen. On an RTG mode they use a second asset set, drawn at that size and not enlarged: `tools/CardArt --hd` draws 64x80 cards with vector shapes (anti-aliased suits, stroke-drawn rank letters, court cards, pip layouts, card back, arrows) and a smoothed copy of the 5x7 font, edges baked against the table or button colour because the blit mask is one bit. The set is one 896x496 sheet (56 colours, 24-bit palette) generated once into `samples/shared/cards-hd/Assets.g.cs` under `#if AROS` and used by both games through an `esharp.include` file (the CLI adds the listed directories to the sample's sources) (new manifest key `"if"`), so the m68k builds do not carry it. Without an RTG mode they fall back to the 32x40 art drawn 2x2 (`Video.BlitMaskedScaled`, unit-tested). Seen working with scripted input on hosted AROS. `cards` now checks the drawn 64x80 card against the sheet pixel for pixel (it passes on hosted AROS, nightly 20261106; the 20261104 nightly is gone from SourceForge); `rummy` does the same for a normal glyph, a big glyph and the card back (all pass); the other glyphs, the court cards and the full rummy screen were reviewed by eye only. The A1200 / Kickstart 3.1 regression is unchanged (300 ok, no FAIL). The one generated source is about 2.5 MB. **Sound on AROS (works):** the earlier hang was the host's ALSA setup, not E# or AROS itself. AROS's `audio.device` is AHI-based; on a headless host with no usable sound card its ALSA mode hangs (in `OpenDevice`, or in the first write when the ALSA config is only half valid; a `ctl null` line is not a real ALSA plugin). With AHI's `void` mode as the only audio mode (it takes samples and paces them in real time, no card needed; `tools/aros-hosted/aros-hosted.sh` sets this up in its scratch copy of the archive) `sound` passes completely on hosted AROS (21 ok, no FAIL): writes finish after the right time, looping voices keep playing and two voices play together. One platform change was needed: AROS's `audio.device` shares its channels, so four requests for "any one channel" got units 1, 2, 1, 2 and a later request stole an earlier one's channel (its writes then failed with ADIOERR_NOALLOCATION); on AROS `Sound.Open` now asks for one fixed channel per voice (`#if AROS`; the Amiga path is unchanged and `sound` on the A1200 / Kickstart 3.1 still gives 21 ok). Real audible output on AROS (a real card) was not tried. The 64-bit IOAudio offsets are right (open, key and unit read back correctly), `cards` and `rummy` run on hosted AROS under Xvfb with scripted XTEST input (cards: three clicks on the higher/lower buttons gave three right guesses; rummy: a click on the stock drew a card, 36 left) and their self-checks pass (rummy 27 ok); screenshots taken with `xwd` look right. The harness (Xvfb, a patched `S:Startup-Sequence`, a ctypes XTEST helper) is `tools/aros-hosted/aros-hosted.sh` (re-run on the 20261106 nightly with `cards`). gcc still warns about incompatible pointer types and BPTR integer conversions (no behaviour change). The A1200 / Kickstart 3.1 regression after the port gives the same 300 `ok` lines and no FAIL as before. A 32-bit AROS (i386) toolchain was not found as an image.
- **Kept on purpose:** `HasAga()` and `HasRtg()` with their fallbacks. A classic A1200 always has AGA, but PowerPC, RTG-only and AROS targets may not, so the checks stay as the portable way to ask. **Not yet done:** using 68020 instructions deliberately and AROS (a new target, untested).
- Verified after the change: all 17 samples built with `-m68020` and run on Kickstart 3.1 (A1200) in FS-UAE: 300 `ok` lines, no `FAIL` (the script shows only the last 25 lines of each sample, so long ones are truncated), 136 unit tests pass. Not verified: PowerPC, real hardware, a 68000/Kickstart 2.04 run (no longer supported).

## NuGet packages: AmigaESharp.Tool and AmigaESharp.Sdk

E# is packed as two nuget.org packages (the ID `esharp` is taken, so the prefix is `AmigaESharp`; `amigaesharp.tool` and `amigaesharp.sdk` were free when checked on 2026-11-13). **Published:** 0.1.1 is on nuget.org (2026-11-14, Release run 43534423320 started by hand: test, pack, smoke test, trusted-publishing push, tag `v0.1.1` and GitHub release all passed). Every later push to main that changes more than Markdown or workflows releases `0.1.<run number>`.

- **AmigaESharp.Tool** (`src/ESharp.Cli`, `PackAsTool`, command `esharp`): the compiler with the Runtime/Platform sources and the C runtime headers embedded (as before), the asset pipeline, the project template, the FS-UAE / SDL / os4 harnesses, and two new folders next to `esharp.dll`: `host/` (ESharp.Runtime, ESharp.Platform, ESharp.Target.Host built with `HOST`, copied from the build of `ESharp.Target.Host`) and `os4vm/` (`os4vm.py`, `estype.c`, README). `esharp test` builds the host run against `host/` (by DLL reference) unless `ESHARP_ROOT` names a checkout; a checkout found by walking up is only the last fallback. New commands: `esharp --version` (also `esharp version`) and `esharp os4vm check|start|install-hook|remove-hook` (runs the shipped `os4vm.py` with `python3 -I`). A docker exit code 125 (usually an image that is not there) now says that `esharp-amiga-toolchain`/`esharp-sdl` are built from the repository and names the pinned m68k image.
- **AmigaESharp.Sdk** (`src/ESharp.Sdk`, packaging only): `lib/net10.0` holds ESharp.Runtime, ESharp.Platform and ESharp.Target.Host; `analyzers/dotnet/cs` holds ESharp.Analyzers. No package dependencies.
- **`esharp new`:** an installed tool writes `<PackageReference Include="AmigaESharp.Sdk" Version="<tool version>" />` and tasks that call `esharp` (on PATH after `dotnet tool install -g`); `--package [version]` forces that. A CLI that runs from a checkout (or `--source [dir]`, or `ESHARP_ROOT`) writes the old ProjectReference csproj (contributors). The template program now also prints one check (`ok sum of 1 to 10`) so `esharp test` works on a new project.
- **Package icon:** both packages carry `src/icon.png` (`PackageIcon`), a procedural red and white checkered ball with a drop shadow, redrawn by `tools/PackageIcon` (original artwork, no existing image used).
- **Versioning:** `Directory.Build.props` holds one version for both packages (local builds are `0.1.0-dev`) and the package metadata (MIT expression, Gyrafe Software, repository URL, `src/PackageReadme.md` as the package README with the Amiga E credit). The release passes `-p:Version=$VERSION_PREFIX.<run number>$RELEASE_SUFFIX` (`VERSION_PREFIX` is `0.1` in `release.yml`); no versioning tool was added.
- **CI:** `.github/workflows/ci.yml` has a `packages` job: pack with version `0.0.0-ci.<run>`, then `tools/package-smoke.sh` (install the tool into a tool path from the folder feed, `esharp new`, `dotnet build` and run, the packaged analyzer gives ES0001 for a `try`, `esharp test`, `esharp build --emit-only`, an m68k build in the pinned image). `.github/workflows/release.yml` runs on every push to main (except `**.md` and `.github/workflows/**` only) and on demand: test, pack, the same smoke test, upload the packages as an artifact, then a separate `publish` job (the only one with `id-token: write`) logs in with `NuGet/login` (trusted publishing, a short-lived key) and runs `dotnet nuget push --skip-duplicate`; after that a `github-release` job tags the commit `vX.Y.Z` and creates a GitHub release with the packages.

Verified (2026-11-13, this host, packages `0.1.0-local.1` in a folder feed under the scratch directory, a separate NuGet cache, no ESHARP_ROOT, everything outside the repository):
- `dotnet test`: 415 tests, 413 passed, 2 skipped (as before), including new tests for the package csproj, the tasks calling `esharp`, the version, the `host/` and `os4vm/` files next to the CLI, the DLL-reference host project, `esharp new --package/--source` through the CLI, and the template program compiled to C and run with host gcc.
- `dotnet tool install AmigaESharp.Tool --tool-path ...`; `esharp --version` prints `0.1.0-local.1`; `esharp new demo` writes the PackageReference; `dotnet build` and `dotnet run` print `Hello from E#!` and `ok sum of 1 to 10`; a `try` gives `error ES0001` in `dotnet build` (analyzer from the package); `esharp test` passes on the host; `esharp build --emit-only` and `esharp build --target m68k --docker <pinned m68k image>` work.
- `esharp test --target m68k --docker <pinned m68k image> --model A1200 --kickstart <Kickstart 3.1 40.069>` of the new project: host 1 ok, Amiga 1 ok, no differences, TEST PASSED.
- `samples/screen` copied outside the repository: `esharp assets` regenerates an identical `Assets.g.cs`; `esharp run screen --headless --model A1200 --kickstart <3.1> --screenshot` prints 23 ok, no FAIL, and the screenshot shows the rectangles, the card and the font.
- `tools/package-smoke.sh` passes locally with the pinned m68k image; `esharp os4vm --help` runs the shipped script; `esharp test samples/language` from the checkout (now through `host/`) gives 39 ok.

Not verified:
- Checked after the 0.1.1 release (Linux x86-64, 2026-11-14): `dotnet tool install AmigaESharp.Tool --version 0.1.1` from nuget.org into a tool path, `esharp new demo` (PackageReference AmigaESharp.Sdk 0.1.1), `dotnet run` (Hello from E#!) and `esharp test` (1 ok). The `packages` CI job runs on every PR. Not verified from nuget.org: an m68k build or emulator run with the installed tool, Windows and macOS installs, and how the nuget.org README page renders.
- `esharp os4vm install-hook`/`start` from the installed tool (only `--help`); `--target os4`, `sdl` and `aros` runs from the installed tool; Windows and macOS installs; VS Code with the package csproj.
- Still needs a checkout: the toolchain images `esharp-amiga-toolchain` (all four Amiga targets) and `esharp-sdl` (`docker build` from `docker/` and `docker/sdl`; the m68k-only pinned image works without one), `tools/aros-hosted/aros-hosted.sh` (expects `samples/<name>/obj/aros`), `tools/hwpack` (builds the repository's samples), BindGen, CardArt and AssetSamples.
- Library packages as in issue #31 (E# source packages that `esharp build` reads from `project.assets.json`) are not started; the Sdk package ships compiled host assemblies only, and the Amiga build still uses the sources embedded in the tool, so the Sdk package version must match the tool's (the template writes the tool's version).

## Double buffer: `Video.EnableDoubleBuffer` and `Video.Flip` (samples/dbuffer, issue #66 part 1)

A game can redraw the whole screen without flicker: after `Video.EnableDoubleBuffer()` every drawing call (`FillRect`, `Blit`,
`BlitRegion`, `BlitMasked`, `BlitMaskedScaled`, `Font`, `Fonts.Draw`, and `GetPixel` for reading) goes to a back buffer that is not on
display, and `Video.Flip()` shows it in one go and waits for the display to take it (one frame, counted in `Frame.Count`). The API is
the same on every target: `EnableDoubleBuffer()` (true if on), `EnableBackBuffer()` (ask for the copy mode below), `Flip()` (without
double buffering it is just `WaitVBlank`), `DisableDoubleBuffer()` (the frame on display stays and drawing goes to it again; `Close` does
it too), `IsDoubleBuffered()` and `Buffering()`, a new enum `VideoBuffering { Single, Swap, Copy }`. Existing programs that never turn
it on are unchanged. The back buffer starts as a copy of the screen. After a `Flip` it holds an older frame: the one before the frame
just shown with `Swap`, the frame just shown with `Copy`. So redraw the whole screen each frame (or all that changed in the last two).

Limits: with `Swap`, Intuition still draws on the screen's own bitmap, which is not on display while the second one is shown, so anything
Intuition draws (the backdrop window, an ASL requester) can be invisible and the next `Flip` overwrites it. `Asl.PickFile` therefore turns
double buffering off first (the frame on display stays); enable it again after the requester. `DisableDoubleBuffer` retries the switch
back to the screen's own bitmap for about ten seconds (500 frames); if the system still refuses it leaks the two buffers and their ports
instead of freeing the bitmap that is on display (tested in the shim with a `ChangeScreenBuffer` that never succeeds).

- **Classic (m68k, graphics/intuition v39):** `Swap` uses two screen bitmaps: `AllocScreenBuffer` with `SB_SCREEN_BITMAP` (the
  screen's own) and `SB_COPY_BITMAP` (a second one, in chip RAM: width x height x depth / 8 bytes, 30 KB for 320x256x3, 50 KB for
  320x256x5, 80 KB for 320x256x8), `ChangeScreenBuffer` at `Flip`, and the DBufInfo messages on two message ports: `Flip` waits for the
  "safe to write" message before drawing goes to the old front bitmap, and for the "on display" message of the previous change before
  the next `ChangeScreenBuffer`. Drawing goes through our own rast port (`InitRastPort`, no layer) on the back bitmap, so `Video` now
  clips rectangles, blits and the template text to the screen itself (the window's layer did that; with one buffer the result is the
  same) and `Fonts.Draw` draws only lines that fit and the characters that fit in width when double buffered. The first wait polls (one
  `Delay(1)` per try, about a second at most) so a system that never sends the messages cannot hang it; after the first message it
  sleeps in `WaitPort`; without messages each `Flip` waits a frame instead. `ChangeScreenBuffer` returning 0 (Intuition busy) is retried
  once a frame for 50 frames. `Copy` (the fallback when screen buffers cannot be had, and what `EnableBackBuffer` asks for) is an
  offscreen bitmap (`AllocBitMap`, a friend of the screen bitmap, so it is in the card's format on RTG) that `Flip` copies to the window
  with one `BltBitMapRastPort` after the vertical blank. Turning `Swap` off while the second bitmap is shown copies it into the screen's
  own bitmap (`BltBitMap`) and shows that again before the buffers are freed. New bindings: intuition `AllocScreenBuffer`,
  `FreeScreenBuffer`, `ChangeScreenBuffer`; graphics `InitRastPort`, `AllocBitMap`, `FreeBitMap`, `BltBitMap`; new measured layout
  items (RastPort size and BitMap, ScreenBuffer, DBufInfo reply ports, `SB_*`, `BMF_*`) for m68k and AROS x86-64 (`tools/BindGen/layouts.sh`).
- **AmigaOS 4 and AROS:** the same calls (the window's place on a larger centred OS4 mode is added to the coordinates in `Swap`, where
  drawing goes to the full screen bitmap). On the OS4 VM the DBufInfo messages come back at once (the QEMU sm501 has no vertical blank),
  so the frame count after a swap goes through the same timer.device padding as `Frame.WaitVBlank` (new internal `Frame.CountFrame`:
  a frame less than half a frame after the previous one is padded to a whole frame); on a classic display that never happens.
- **RTG (cybergraphics):** no own code path: `Swap` where the RTG system gives screen buffers, else the `Copy` fallback with a friend
  bitmap. Neither was run on an RTG system (FS-UAE has no card here; the OS4 VM screen is RTG, see below).
- **SDL:** a second framebuffer in the C runtime (`es_sdl_dbuf_enable`, `es_sdl_flip`, `es_sdl_dbuf_disable`); the window and the
  screenshot show the front one, drawing goes to the back one, and `Flip` swaps them (or copies with `EnableBackBuffer`) and then waits a
  frame, which presents once. `Dos.Delay` and `Frame.WaitVBlank` present the front buffer too.
- **Host fake:** `Video.HostPixels` is where drawing goes (the back buffer when double buffered), new `Video.HostShown` is what is on
  display, `Video.HostFlips` counts flips, and `Video.HostScreenBuffers = false` makes `EnableDoubleBuffer` fall back to `Copy`.

`samples/dbuffer` (30 checks): single-buffered `Flip`, swap or copy, the back buffer starts as a copy, drawing goes to the back buffer,
the older frame after `Flip`, clipping of rectangles and blits over the edges, text, 50 whole-screen frames with one `Flip` each (and
their time printed), turning it off keeps the last frame, then the same for `EnableBackBuffer`, then a picture for the screenshot ("SHOT").

Verified:
- `dotnet test`: 422 passed, 2 skipped, 424 in all (9 new: `DoubleBufferTests`: the Amiga Video code built with the host gcc against a shim of
  screen buffers, bitmaps and message ports that checks the order of buffer changes, which bitmap is on display when buffers are freed,
  the reply ports at the measured DBufInfo offsets and that no `WaitPort` would hang, with and without messages; the sample compiles for
  m68k, os4, aros and sdl with the expected calls; in `HostTargetTests`: the host fake's front and back buffers in both modes and the
  sample on the host with and without screen buffers).
- `esharp test samples/dbuffer --target m68k`, A1200 / Kickstart 3.1 (40.069) in FS-UAE: 30 ok, no FAIL, host and Amiga agree; `Swap`
  (two screen bitmaps). 50 whole-screen frames (a full-screen fill, 9 bars, scaled text) took 2010-2012 ms with `Swap` (the drawing takes a
  bit more than a frame) and 3010-3015 ms with `Copy` (plus the vertical blank wait and the copy). Screenshot (`esharp run --headless
  --screenshot`) looked at: the last frame, complete.
- `esharp test samples/dbuffer --target sdl --docker esharp-sdl`: 30 ok, host and SDL agree (999 ms per 50 frames in both modes);
  screenshot looked at.
- `esharp test samples/dbuffer --target os4` in the AmigaOS 4.1 VM: 30 ok, host and OS4 agree, `Swap`; 1003 ms per 50 frames (850 ms with
  `Copy`, paced by `Frame.WaitVBlank` as before); the screenshot shows the frame centred on the 640x480 mode.
- `--target aros` and `--target morphos` builds: aros builds (its gcc warnings are the existing timer ones, none from this code);
  morphos fails as before on existing code (timer, tag lists, the blit BitMap), none of its errors is in the new code.

Not verified: real hardware, hosted AROS runs (not available on the host now), an RTG card (Picasso96/CyberGraphX: whether they give
screen buffers or the `Copy` fallback is used), MorphOS runs, `ChangeScreenBuffer` failing (Intuition busy) and a system that never
sends the DBufInfo messages (both only against the shim), an NTSC display, the flicker itself by eye (screenshots are single frames),
and the sample is in the hardware test pack (`tools/hwpack`, run on the emulator only).

## Gaps found while porting a game (classes, static initializers, text speed, screen modes, arguments)

A port of a bigger game hit these limits; each is now an engine feature with original tests (`tests/ESharp.Compiler.Tests/ClassFeatureTests.cs`,
compiled with the host gcc and run) and the new `samples/objects` (21 checks), `samples/textspeed` and more checks in `samples/screen`.

- **Arrays as fields of a class:** `public Seat[] Seats = new Seat[4]; public int[] Scores = new int[13]; public byte[] Order = { 3, 1, 2 };` are
  C arrays inside the object (a constant length, as for locals and statics); `Length`, indexing, `foreach` and passing as a span work. Arrays in
  struct fields are still not supported (use `fixed` buffers there).
- **Instance field and auto-property initializers in classes** (were ES0099): each class with initializers gets a `NAME_finit` function that runs in
  C# order: the class's own initializers, then the base constructor (or the base's initializers when it has no constructor), then the body; a
  `this(...)` constructor leaves them to the other one. A class without a constructor runs them in `new`. Also fixed on the way: `: base()` naming
  a base class without an explicit constructor skipped the base's initializers (and its base's constructor).
- **`Vec<T>` holds class references** (the `unmanaged` constraint is gone): `Vec<Player>` is an array of pointers in C. New `IndexOf` and `Remove`
  (the same object for a class, the same bytes for a value). The list does not own the objects: free them before `Destroy`. On the dotnet host a list
  of references keeps them in a managed array (the GC must see them); values still use the fake `Mem`.
- **Struct members on temporaries** (were ES0099): `Make().Prop`, `Make().Method()`, `list[i].Prop`, `readOnlySpan[i].Prop` work on a copy, held in a
  C compound literal (as C# copies too); `span[i].Method()` works on the element itself (a `Span` indexer is a reference, so a mutating method
  changes the element, as in C#). `list.AsSpan().Slice(a, b)` and a call through an interface on a call result (`Pick().Area()`) evaluate the
  receiver once in a statement expression.
- **Nested classes and interfaces** inside classes (were ES0099): `Outer.Inner` is the C type `Es_..._Outer_Inner`.
- **`ref`, `out` and `in` parameters in interface methods** (and in virtual methods through the class table): the slot takes pointers, as an ordinary
  function does.
- **Interface methods with spans of structs** produced C that did not compile (the function-table struct came before the `ES_ROSpan_...` typedef).
  The tables of interfaces and virtual methods are now emitted after the span, tuple and delegate typedefs.
- **Static field initializers that run code** (`static Rng r = Rng.FromSeed(7);`, `new Game(...)`, a lambda, an array whose elements call methods,
  a static auto-property) were pasted into a C file-scope initializer and gcc failed. Initializers that are not C constants now run at start-up,
  before the static constructors, in declaration order except that a field another initializer reads is set first. Constant initializers stay C
  initializers.
- **`Span<T>.Empty`, `ReadOnlySpan<T>.Empty` and `IsEmpty`** (were calls of functions that do not exist).
- **C keywords and predefined macros as names:** a parameter, local, field or property called `auto`, `register`, `signed`, `unsigned`, `inline`,
  `@long`, `amiga`, `mc68000`, `TRUE`, ... is renamed in the C (`es_auto`) after the C# checks; types, methods, statics and enum members already
  have prefixed C names and keep theirs (so an enum member still prints as written).
- **`string.Substring(start[, count])`:** copies into the same ring of 8 static 128-byte buffers as interpolation (valid until 8 more strings are
  made, cut at 127 bytes). Out-of-range arguments are clamped on the Amiga, where .NET throws: do not rely on them (the host run throws). And
  generally: **a call or property of a .NET type that E# has no code for is now ES0099** (it used to pass and fail at link time).
- **`string.IndexOf`, `LastIndexOf`, `Contains`, `StartsWith`, `EndsWith` (issue #72):** with a `char` or `string` value, ordinal and byte by byte,
  on every target (small C helpers `es_str_*`, no buffer). `IndexOf` also takes a `startIndex`; every method takes a trailing constant
  `StringComparison.Ordinal`, also together with the `startIndex` (`IndexOf(value, startIndex, StringComparison.Ordinal)`).
  **Host difference:** the .NET host's string overloads without a `StringComparison` are culture-sensitive (a leading soft hyphen is ignored by
  `"\u00ADabc".StartsWith("abc")`, true on .NET, false from the C helper), so `esharp test` can differ from the Amiga: pass
  `StringComparison.Ordinal` explicitly (no analyzer suggestion yet). Not found is -1; the empty string is found at `startIndex` (`LastIndexOf("")` is the length); null text counts as
  "" and a null value finds nothing. Like `Substring`, nothing throws on the Amiga: a `startIndex` past the end gives -1 and a negative one is
  0, where .NET throws (do not rely on it; the host run throws). Culture and ignore-case comparisons, `IndexOf` with a count and
  `LastIndexOf` with a start are still ES0099. Test (`ClassFeatureTests.String_search_methods_are_ordinal_and_never_throw`): generated C built
  and run with the host gcc, 14 checks (empty strings, not found, at the start and end, start index past the end, startIndex with Ordinal).
- **`is`, `as` and type patterns from an interface value to a class** (were ES0099): the interface value's function table names the class it came
  from; when that is a base class of the target, the object's own type id decides. `switch` patterns, property patterns and bindings work.
  Interface-to-interface tests and struct targets are still ES0099.
- **Text speed:** `Font.Draw` drew one `RectFill` per run of glyph pixels, about a second for a screen of text on the A1200. A line is now one
  graphics.library `BltTemplate` from a one-plane strip in chip RAM that the CPU fills (new bindings `BltTemplate`, `SetDrMd`, `WaitBlit`; `Video.Template` calls `WaitBlit` after each `BltTemplate` so the CPU does not refill the strip while the blitter still reads it; the emulator cannot show that race, so this is not verified on hardware); `DrawChar` and
  `DrawInt` use it too, scales above 4 and text left of or above the screen fall back to rectangles; the host and SDL draw the same pixels as before.
  Measured with `samples/textspeed` on the emulated A1200 (Kickstart 3.1, chip RAM only): 30 lines of 30 characters take 171-173 ms with
  `Font.Draw`, 2737-2896 ms drawn the old way. The pixels are checked: `samples/screen` compares every printable character at scale 1 and five at
  scale 2 with the glyph table pixel for pixel (new `Font.Pixel`), passing on host, A1200 / Kickstart 3.1, SDL and hosted AROS. The
  offscreen bitmap / double buffer for whole-screen redraws is now done: see "Double buffer" above.
- **Screen modes:** `Video.Open` picks the classic mode from the size (m68k only): wider than 400 pixels is hires, wider than 800 super-hires,
  taller than 300 lines interlaced, so `Video.Open(640, 256, 4)` is a hires PAL screen (it used to be a lores screen twice as wide as the display).
  New `Video.IsHires()` (the view port's mode ID on the chipset). Without AGA a depth above 5 now opens an 8-bit RTG screen when there is a card
  (hosted AROS: `Open(320, 256, 6)` works), instead of returning false; `samples/aga`'s fallback check now says so. Checked on A1200 / Kickstart 3.1
  (`samples/screen`: 640 x 256 opens, is hires, pixels and text at the right edge) and on hosted AROS (`aga`: an 8-bit screen opens without AGA).
  Not checked: `samples/asl` opens a 640 x 256 screen and is now hires there, so the click positions found for its requester may have moved.
- **Program arguments:** `Args.Count`, `Args.Get(i)`, `Args.Has("AUTO")` (ASCII case-insensitive), `Args.Value("SEED")` (`SEED 42` or `SEED=42`),
  `Args.Int("SEED", fallback)`; `main` keeps `argc`/`argv` only when a program uses them. A Workbench start reads the program icon's tool types as the arguments (issue #84, see "Workbench icons"; "(NAME)" disabled ones skipped). `esharp run --args "SEED=42 auto"` passes a line to the program in FS-UAE (after the output redirection), on SDL and (issue #72) in the os4
  VM (the line follows the program name in the harness script, so AmigaDOS splits it like a Shell; newlines, `<`, `>`, `;`, `|` and `$` are refused).
  Host (dotnet): `Args` is now the real command line (issue #72, same words as `Main(string[] args)`); `Args.HostSet(...)` overrides it for tests
  (tests call it for repeatability) and `Args.HostReset()` goes back to the command line. Seen on A1200 / Kickstart 3.1 and SDL with `samples/objects`
  (`2 arguments, first SEED=42, SEED 42, AUTO on`). 
- **`Main(string[] args)` (issue #66, second part):** the `[EntryPoint]` method may be `void Main()`, `int Main()`, `void Main(string[] args)` or
  `int Main(string[] args)` (anything else is ES0022). `args` is built at the top of the entry function as a variable-length C array from the same
  argv words `Args` reads (so `args.Length`, indexing, `foreach` and the Span conversion work; no arguments gives a zero-length array), and
  ES0011 (array parameters) lets this one parameter through. An `int` result is the return value of C `main`, so it is the AmigaDOS RC on Amiga
  targets (the C startup code of the toolchain turns it into the process result; not read back, see below) and the process exit code on SDL (seen: code 5). Quoting: the Amiga startup splits at spaces and keeps
  "quoted text" together (quotes removed); `esharp run --args` on SDL now splits the same way (before it split at every space, so quoted
  words were broken up; `SdlRun.SplitCommandLine`, unit-tested). Host (dotnet): the program is an ordinary C# `Main(string[] args)` and gets the
  real command line from the runtime, and `Args` reads the same line (it stayed empty before issue #72). New `samples/mainargs`
  (prints the words and returns `RC=n` as the return code). Unit tests (`MainArgsTests`): generated C built with the host gcc and run with no
  arguments, `one "two words" ""` and `RC` (exit code 7), void and int, plain `Main()` unchanged, `Args` and `args` agreeing, the ES0022
  signatures. Run on A1200 / Kickstart 3.1 in FS-UAE with `--args 'ONE "two words" RC=5'`: 3 arguments, `two words` kept whole, `rc 5`;
  with no `--args`: `no arguments`. SDL (esharp-sdl, headless): the same output, `esharp: the program exited with code 5`. Not verified:
  the numeric RC seen by the Amiga Shell (FS-UAE `run` shows output only), os4/MorphOS/AROS runs, a zero-length VLA on the PowerPC gcc.
- **Tools and the host:** `esharp run --input "rel DX,DY"` spreads the shorter axis over the steps and the steps add up exactly (it sent
  `dx / steps` per step, so `rel 76,-320` did not move sideways at all); unit-tested, not re-run in the emulator. The analyzer no longer reports
  `Heap.Free` (or other generic methods of E#'s own library) as ES0008 in a host project made by `esharp new`, where the library is an assembly.
  The host `Mem` fake is thread-safe (one lock), so xunit tests that use `Vec` in parallel no longer corrupt its block table.
- **Verified:** 406 unit tests pass (2 skipped: OS4 VM and an SDK path). `esharp test` on A1200 / Kickstart 3.1 (40.069) in FS-UAE: `objects` 21 ok
  and `screen` 23 ok, host and Amiga agree; the regression of 29 other samples gives 461 `ok` and no `FAIL` on the Amiga; the samples where host and
  Amiga differ are the known ones (scripted input checks in `cards` and `rummy`, `rtg`'s host card, and `asl`, `iff`, `controls`, `fonts` without
  their libraries in the run directory). SDL (`esharp test --target sdl --docker esharp-sdl`): `objects` 21, `screen` 23, `language` 39, `inherit`,
  `generics` 24, `lambdas` 16, `linq` 37, `cards` 20 ok, host and SDL agree. Hosted AROS x86-64 (nightly 20261106): `objects` 21, `screen` 23,
  `language` 39, `inherit` 24, `lambdas` 16, `linq` 37, `generics` 24 ok, `aga` fallback 3 ok, `cards` 16 ok with a click, no `FAIL`; `textspeed` and
  `cards` screenshots looked right. Built for os4 (`screen`, `objects`), not run in the VM. `--target morphos` fails to compile `screen` with pointer-type errors in the timer code (`es_TimerBase`, `IORequest`), which this change does not touch (not investigated, as before). Not verified: OS4 and MorphOS runs, real hardware, the `--input rel` change
  in an emulator run.

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
- Typed struct pointers and a `TagItem` type: the generator writes `void*`, as the hand-written bindings do, because E# has no C# declarations of the system structs (their fields come from Layout.g.cs). A pointer to a struct pointer is `void**` in C#, which C compilers accept with an incompatible-pointer warning (an error with gcc 14 and later); that is the same as the existing `GadTools.CreateContext`. Generating struct declarations is a design question for the maintainers.
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
Apple clang 21.0.0 (269, run by the maintainer; this is where the warnings were found); CI repeats the run with clang; samples/language agrees on the
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

`esharp test <dir>` builds the project's `.cs` files into a temporary dotnet project (`obj/host`, `HOST` defined, referencing `ESharp.Target.Host`), runs it, and counts output lines starting `ok ` and `FAIL `. A FAIL line, a non-zero exit or no ok/FAIL lines at all fails the run. With `--target m68k --docker <image> [--kickstart <rom>] [--model A1200]` it also builds for m68k, runs headless in FS-UAE (the `run` code) and lists every check that is ok on one side and FAIL or missing on the other. It builds against the host assemblies shipped next to the CLI (`host/`), or the E# source tree named by `ESHARP_ROOT` (`ESHARP_REPO` still works as a deprecated alias); see "NuGet packages". Passing `--docker`, `--kickstart`, `--model` or `--fsuae` without `--target m68k` prints a warning that they are ignored.

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
- **Verified on macOS (the maintainer, 2026-11-10; macOS 27, Apple Silicon, .NET 10 SDK, VS Code with the C# extension):** `dotnet test` (269 passed after the clang fix), `esharp new mygame` from a checkout, the project loads in VS Code, the analyzer shows ES0001 live while typing (squiggle and hover), the tasks "check subset and emit C" (error in the Problems panel; then `obj/m68k/mygame.c`), "run on the host" (`Hello from E#!`), "build for the host", F5 "debug on the host (.NET)" (breakpoint hit, step over), and "build (m68k)" through Docker Desktop (pulled the pinned m68k image under emulation, `Built .../obj/m68k/mygame`). Clicking a task error in Problems to jump to the line was not reported back.
- Not verified: the editor on Windows and Linux. (A CLI installed away from a checkout now works through the AmigaESharp packages; see "NuGet packages" above.)

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

Still `ES0099`: an extension `Deconstruct`, a positional pattern on `ITuple` or an interface, a `Deconstruct` with ref/in parameters, `var (a, b)` on a type with two `Deconstruct` overloads of the same arity, type tests to an interface or a struct type (from an interface value to a class works now), list patterns on interfaces or on types whose indexer takes `Index` or returns by ref, a slice with a subpattern over anything but a span, an extended property pattern through a property with a getter in the middle (`{ Getter.X: 1 }`; write `{ Getter: { X: 1 } }`), and records (all of them).

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

Not in this milestone, by decision: menus, BOOPSI, MUI, GadTools/Intuition callback hooks, typed tags, keyboard shortcuts (an underscore in a label is shown as is), other gadget kinds, window resizing or a layout other than one column. Gadgets can be added only before `Open()`. Subclassing `GtWindow` came later (issue #42, see "Classes derived from platform classes").

Bindings added: gadtools `CreateGadgetA`, `FreeGadgets`, `GT_SetGadgetAttrsA`, `GT_GetIMsg`, `GT_ReplyIMsg`, `GT_RefreshWindow`, `GT_BeginRefresh`, `GT_EndRefresh`, `CreateContext`, `GetVisualInfoA`, `FreeVisualInfo`, `GT_GetGadgetAttrsA` (v39; in the fd file it sits after six reserved slots, vector -174); intuition `LockPubScreen`, `UnlockPubScreen`; exec `WaitPort`. Vectors are from the fd files (computed from the `##bias` and `##private` lines); `bindgen check` against the AROS include dir did not cover gadtools (that include dir has no gadtools headers, so it checked 39 declarations and only the old `alib.BeginIO` note remains); `bindgen compare ... gadtools` shows only the two v47 functions (`SetDesignFontA`, `ScaleGadgetRectA`) that AROS lacks. `NewGadget`, `Screen` border and font, `Gadget.GadgetID` offsets, the `*_KIND`, `GT*`, `WA_*` and `IDCMP_*` values are in `structs.txt`, measured into Layout.g.cs for m68k and AROS x86-64 (`NewGadget` is packed on m68k: 30 bytes, `ng_Flags` at 18).

`LockPubScreen(NULL)` in the `esharp run` setup (Kickstart 3.1 ROM, no Workbench disk): it returns a screen titled "Workbench Screen", so no Workbench-disk setup is needed.

Verified:
- 211 unit tests pass (6 new): the sample on the host fake, the fake's queue and `IGuiHandler` dispatch, and the m68k and AROS C output (private `GadToolsBase`, `GT_GetIMsg`/`GT_ReplyIMsg`, close order CloseWindow, FreeGadgets, FreeVisualInfo, UnlockPubScreen), and `win.Run(handler)` emitted for m68k and AROS (the sample itself uses the pull loop, so `Run` is checked as generated C, not run on a target).
- A1200, Kickstart 3.1 (40.069) in FS-UAE, no Workbench disk: the window opens on the "Workbench Screen" (screenshots looked at: three rows, Name / Hello / Reply, topaz 8). `--input "click 400,300;rel -96,-100;press;rel -72,-63;rel -42,-20;press"` (Hello, then the close gadget; these moves were recorded before the exact `--input rel` fix in f76a167, which no longer drops the remainder of each step, so today the close gadget needs `rel -28,-10`, see "Classes derived from platform classes") printed `ok` for each step (window opens, default name read back, Hello clicked, "reply: Hello, Amiga!", a click arrived, the reply is Hello, Amiga!, the close gadget arrived, the window is freed) and exited cleanly. A screenshot after the Hello click shows "Hello, Amiga!" in the Reply field.
- Hosted AROS x86-64 (nightly 20261106, `--target aros`): the same output with `click 208,74;click 47,37`, 0 traps, and a screenshot after the click shows the reply. So the 64-bit `NewGadget` offsets are right.

Not verified:
- Typing into the string gadget (keys do not reach hosted AROS; on FS-UAE no key script was run for this sample). The default name is what proves the round trip.
- Real hardware, a Workbench disk setup (other fonts than topaz 8, a taller screen font, a different screen than Workbench), AmigaOS 4 and MorphOS (only the compiler targets exist; the sample was not built for them), several windows at once, a window that is covered and refreshed (`GT_BeginRefresh` path not exercised), `Run(handler)` on a real target (host only).
- Emitter limits seen on the way (string `==` compared addresses, `x!` was copied into the C, base/derived compares warned in gcc) are fixed (issue #49, see its section above); the sample now uses `==` on strings and builds without gcc warnings for m68k. The fixed sample was built for m68k but not re-run in FS-UAE.

## Classes derived from platform classes (samples/guiclass, issue #42)

A class of the program may derive from a platform class that is marked `[Extensible]` (new attribute in `ESharp`, src/ESharp.Runtime/Attributes.cs) and override its virtual members: `public sealed class Greeter : GtWindow { public Greeter() : base() { Title = ...; Add(...); } protected override void OnClicked(GtGadget g) { ... } }`. `GtWindow` is the only class marked so far. Every other library class (`GtGadget`, `GtButton`, `GtString`, `GtText`, and .NET's) is still rejected as a base class with ES0007, whose message now says the class is not `[Extensible]`. The rule is the same in `esharp build`, where the platform is parsed as source together with the program, and in the IDE, where `ESharp.Platform` is a referenced assembly (metadata). The analyzer treats a type as E# library code if it comes from the ESharp.Platform / ESharp.Runtime assemblies or is declared in the `ESharp` / `ESharp.Platform` namespaces, and reads the attribute from source and metadata alike.

`GtWindow` gained hooks and a loop for them, and the existing API is unchanged (`Wait()`, `Run(IGuiHandler)`): `protected virtual void OnClicked(GtGadget)`, `protected virtual void OnChanged(GtGadget)` (both do nothing by default), `protected virtual bool OnCloseRequest()` (true by default; false keeps the window open), `public void Run()` (waits and dispatches until a close request is accepted; it also returns on a window that is not open, and on the host when the fake queue is empty, so a refusing hook cannot hang it), and `public bool Dispatch(GuiEvent e)` (hands one event to the hooks, so a pull loop can use them too; false for an accepted close; the event should come from this window's own `Wait()`, it is not checked). Since `GtWindow` now has virtual members, its objects carry a class-table pointer (one word) like any other polymorphic class. Constructors chain into the platform constructor (`: base()` or implicitly, also through a derived class with no constructor of its own). `is`/`as` work from `GtWindow` to the derived class. Ownership is unchanged: `Close()` releases the system objects and frees the added gadgets; the program frees the window object (`Heap.Free`, also through a `GtWindow` variable). Anything else the derived class owns, it frees itself before that, because `Heap.Free` does not call a destructor.

Emitter fix needed for this: a class constructor's chained constructor (`: base(...)`, `: this(...)` or the implicit parameterless base one) is now marked reachable. Before, library code was emitted only when an expression named it, so the derived constructor called a `GtWindow_new_init` that was never emitted (a link error, never a miscompile).

Verified:
- `dotnet test`: 518 tests, 516 passed, 2 skipped (the SDK-backed BindGen ones), 0 failed. 13 of them are new (tests/ESharp.Compiler.Tests/PlatformSubclassTests.cs). A program with a two-level hierarchy over `GtWindow` (overrides called from platform code through `Dispatch`, `base.` calls, a refused and an accepted close, `is`/`as` across the library boundary, a class with no constructor, freeing through the base type) is compiled to C for the Amiga build of the platform and run with host gcc under ASan/UBSan/LSan. The same source, compiled by dotnet against the host platform assembly, gives the same `all ok`. The sample is built for m68k, AROS and os4 with the overrides in the class table, and runs on the host fake. Analyzer tests cover a library base without `[Extensible]` (ES0007, in the compiler and with ESharp.Platform loaded as metadata the way the IDE does), `GtWindow` as a metadata base (accepted), a .NET base (still ES0007), `class W : GtWindow` on sdl (ES0102), a program declared in the `ESharp` or `ESharp.Platform` namespace with its own hierarchy (accepted and run; library classes are recognised by their source path, `<esharp-lib>/Runtime/` and `<esharp-lib>/Platform/`, which the compiler passes to the analyzer as an option, or by their assembly in the IDE, never by namespace), and an implicit base constructor whose parameters are all optional (ES0099: the emitter would not run it).
- A1200, Kickstart 3.1 (40.069) in FS-UAE: `esharp run samples/guiclass --headless --model A1200 --kickstart <rom> --input "click 400,300;rel -96,-100;press;rel -72,-63;rel -28,-10;press"` printed 8 `ok` lines and no `FAIL`, including "reply: Hello, Amiga!" from the overridden `OnClicked` and the close through `OnCloseRequest`. The close-gadget move documented for samples/gui (`rel -42,-20`) was found by screenshot before f76a167 (2026-11-13), which made `--input rel` move exactly instead of dropping the remainder of each step; since then the same chain lands 23 pixels left of and 9 above the close gadget, so `samples/gui` on main also stopped after the Hello click. Nothing in the GUI code changed. With `rel -28,-10`, found by screenshot, `samples/gui` gives its 7 `ok` lines again; AGENTS.md has the new moves. The cards and rummy moves in AGENTS.md (`rel -80,50`, `rel 16,-80`) were re-run after that fix and still hit: cards clicked (80,145), the Higher button, and made a guess; rummy clicked (128,80), the stock (28 `ok`, no `FAIL`).
- AmigaOS 4.1 in the QEMU VM (`--target os4`, `--input "click 208,101;click 57,45"`, the samples/gui coordinates): the same 8 `ok` lines, no `FAIL`.
- No regressions on the A1200 / 3.1 (`esharp test --target m68k`): inherit 24, objects 21, closures 20 `ok` on both host and Amiga, and they agree.

Hardware pack: rebuilt with this change (`tools/hwpack/build.sh esharp-amiga-toolchain`, 37 programs plus Summary, guiclass not added): `obj/hwtest.adf` 802 KB used, 78 KB free, so it still fits one floppy; `gui` grew from 17200 to 17256 bytes (the class table and the three default hooks).

Not verified: hosted AROS and MorphOS runs of guiclass (AROS only as generated C), real hardware (not added to the hardware pack), sdl (Gui is Amiga-only there), typing into the string gadget on a target (`OnChanged` runs only on the host and in the C test), and `new` hiding of platform members in a derived class.

## BOOPSI core and hooks (samples/boopsi, issue #40 part 1)

The BOOPSI layer under the MUI wrappers (decisions of #19: GadTools, then this core, then MUI; pull loop plus handlers; the owner frees the tree). MUI itself is part 2, see "MUI wrappers".

**Bindings** (facts only: names, vectors from the classic fd files, `bindgen gen ... intuition utility --check src/ESharp.Platform` reports 0 problems): intuition `NewObjectA`, `DisposeObject`, `SetAttrsA`, `GetAttr`, `SetGadgetAttrsA`, `AddGList`, `RemoveGList`, `RefreshGList`, `DrawImage`, `GetScreenDrawInfo`, `FreeScreenDrawInfo`; utility `CallHookPkt`. `DoMethodA` is no library vector, so it is an emitter-defined `[Native]` (`es_domethod`): amiga.lib on m68k, `IDoMethodA` on AmigaOS 4, the SDK macro on MorphOS, and on AROS a call of the class dispatcher whose class pointer is found once at run time (see below). Tag values, method numbers and the `struct Hook` and `Window` border offsets are measured into Layout.g.cs (`tools/BindGen/structs.txt`).

**Typed attributes.** `IntAttr`, `BoolAttr`, `StrAttr`, `PtrAttr`, `ObjAttr`, `HookAttr` (a tag number with its value type) and catalogues `Ga`, `Pga`, `Stringa`, `Ica`, `Ia`, `Om` for the ROM classes, so `obj.Set(Pga.Top, 30)` takes an int and `obj.Set(Ga.Text, "x")` a string; a wrong value type does not compile. `SetRaw`/`TryGetRaw`/`InitRaw` take any tag.

**`BoopsiObject`** (a public class by name): `Init(attr, value)` collects up to 13 starting attributes (strings are copied and owned by the object; a window adds `GA_DrawInfo` and, if missing, `GA_ID`; a refused `Init`, also a hook without memory, makes `Create` fail rather than make the object without it), `Create()` (NewObjectA; false for an unknown class), `Set`/`Get` (SetGadgetAttrsA plus a refresh when the gadget is in an open window, else SetAttrsA; GetAttr), `DoMethod(id, a, b)` / `DoMethodMsg(msg)` (message slots are pointer sized, as AROS x86-64 wants), `Address`, `BoopsiObject.Live` (system objects made and not yet disposed). **Ownership:** one owner per object, recorded in the object (`HasOwner`). A gadget or image given to a `BoopsiWindow` belongs to the window, which disposes and frees it in `Close()`. `Keep(child)` / `Keep(hook)` make an object the owner of another object (the frame image of a button: a gadget does not dispose its `GA_Image`) or a hook, disposed right after it; a second owner (another object or a window) and a cycle (`a.Keep(b)` when `b` owns `a`, also further up) are refused. Everything else: `Dispose()` then `Heap.Free`. `Dispose` of a gadget still in an open window takes it out first, and `Dispose` of a kept object or hook by the program takes it off its owner's list (the program frees it then). A `Set` of a string attribute frees the copies made for that attribute's earlier values only when the attribute reads back (GetAttr) as the new copy or as the object's own memory (strgclass copies its text); a class that ignores the new value or cannot report it keeps every copy until `Dispose` (`StringCopies` shows the count). A refused `Init` of any kind (too many, an object attribute that is not created yet, a string that could not be copied, a hook without memory) makes `Create` fail. A `Notify` event (`Action<BoopsiEvent>`, a heap-closure event from #83) is raised for the object's window events; `Dispose` drops its handlers.

**`BoopsiWindow`** (`[Extensible]`, a sibling of `GtWindow`): `Open()` on the default public screen (WA_NoCareRefresh, smart refresh), `InnerLeft`/`InnerTop` (border sizes for gadget positions), `DrawInfo`, `Add(gadget)` (created on the spot with `GA_DrawInfo` if not yet, then AddGList and RefreshGList; or when the window opens), `AddImage(image)` (DrawImage; images are drawn before gadgets when the window opens, since a frame fills its inside), `Wait()` (IDCMP_GADGETUP by gadget address, IDCMP_IDCMPUPDATE by `GA_ID` with the first changed attribute and its value copied out of the tag list before the reply; a gadget created by the window without a `Ga.Id` gets a free one from 1000 up that no other gadget of the window has or asked for; `Add` refuses a gadget whose id (asked for with `Init`, or of a created gadget) another gadget has or asked for, before anything is created, and the caller still owns a refused gadget; if a gadget cannot be created when the window opens, `Open` fails and nothing stays open; only gadgets that were put into the window are taken out again), `Dispatch(e)` (the gadget's `Notify`, then the virtual `OnGadgetUp`, `OnUpdate`, `OnCloseRequest`), `Run()`, `Close()` (RemoveGList for each gadget, CloseWindow, DisposeObject and Heap.Free of each gadget and image, then FreeScreenDrawInfo and UnlockPubScreen, since gadgets made with `GA_DrawInfo` keep that pointer). `Close()` called from a handler (a Quit button's `Notify`, `OnGadgetUp`) only marks the window; the teardown runs when `Dispatch` returns (false, so `Run` stops), because the gadget whose event is handled is still in use until then. While a close is pending every `Dispatch` returns false, so a `Run` inside a handler stops too. A second `Close()` does nothing. Never free a gadget the window owns from a handler: close the window. Host: `HostRelease`, `HostUpdate`, `HostClose` queue events; the fake object stores what was set (no class behaviour) and knows the 3.1 ROM class names.

**Hooks.** New attribute `[HookEntry]` on a static method `M(void* hook, void* obj, void* msg)` (pointers or nint/nuint; result void, int, uint, nint, nuint or a pointer): `&M` is the h_Entry address. The emitter writes the C function `M_hook(hook, obj, msg)` and per target: m68k an assembly entry at the end of the file (`move.l a1,-(sp); move.l a2,-(sp); move.l a0,-(sp); jsr; lea 12(sp),sp; rts`: the registers become C arguments, d0 is the result, d2-d7/a2-a6 are kept by the C function); MorphOS a `const struct EmulLibEntry` gate (TRAP_LIB) whose function reads REG_A0/A2/A1; AmigaOS 4, AROS x86-64 and the host gcc the C function itself. sdl, wrong parameters or results, instance methods, `[Interrupt]` or `[InlineAsm]` together with it, and no body are `ES0099`. **`Hook`** (platform class) puts a C# delegate behind a `struct Hook`: `new Hook((obj, msg) => ...)` (`HookFunc`, an `[Owned]` parameter, so a capturing lambda becomes a heap closure the hook owns), `Address` for the system, `Call(obj, msg)` (through CallHookPkt, i.e. through the target's entry), `Dispose()` (frees the struct and the closure). One `[HookEntry]` method serves every hook: h_Data holds the Hook object (`Cast.ObjectToPtr` / `Cast.PtrToObject<T>`, new platform-internal emitter intrinsics, plain unchecked casts in C (the host keeps a table and throws on a wrong type); `ES0099` unless the type is a class, not an interface, delegate, array or string). `IsValid` is false for a hook whose struct could not be allocated; `Init`/`Set` refuse such a hook. A small-data m68k build (`-fbaserel`, `-resident`) stops with `#error` in the hook entries, which call C with the caller's a4.

**Rules for code a hook runs** (an edit hook runs on input.device's task: a plain Task with a small stack; the same goes for any `[HookEntry]` method the system calls from another task; written in the `Hook` and `[HookEntry]` docs, nothing checks them yet): no allocation (no `new`, no string interpolation or concatenation, no capturing lambda that becomes a heap closure: the C heap is not safe from two tasks at once, and a failed allocation ends the program on the wrong task); no dos.library (no `Dos.PutStr`, no files: input.device is not a Process); nothing that takes Intuition's locks or waits (no `Set` on a gadget, no refresh, no window or requester); little stack (no LINQ sorts, no big local arrays); nothing that can stop the program (an index out of range, `exit`). Reading and writing fields and captured locals (shared ones `volatile`) and arithmetic are fine; the sample's edit hook only counts keys. On AmigaOS 4 the hook reads the Hook object and its closure frame, which live in the C (newlib) heap, from input.device's task; that works because that memory is shared, and would need a look if the allocator ever moved to MEMF_PRIVATE memory. An analyzer warning for allocation or dos calls inside hook code is a possible follow-up. The design memo of #19 had h_Data point at an interface value "never a closure"; #83 (heap closures with owners) came in between, so the hook owns a delegate instead, as #40 asks.

**Emitter fixes on the way:** an overloaded method with a `string?` parameter had a `?` in its C name (gcc error; now mangled like `string`, also in the monomorphiser); the AROS DoMethod problem: the crosstools AROS SDK reads the class pointer 8 bytes before an object, but the nightly (20261106) aligns `struct _Object` for SMP and keeps it 16 bytes before, so the SDK's `DoMethodA` macro crashed; `es_domethod` now makes one object of a private rootclass subclass at the first call, sees where its known class pointer is (8, 16, 24 or 32 bytes before the object), and calls `CALLHOOKPKT` on the class found there. If the class pointer is in none of those places, DoMethod says so once on the output and returns 0 from then on (it never falls back to the SDK's offset); if the probe could not allocate, it tries again later. The probe runs at the first `BoopsiObject.Create` (on the program's own task), not inside a hook. `esharp run --input` gained `down` and `up` (hold and release the mouse button, for a drag; FS-UAE/XTEST only). `tools/aros-hosted/aros-hosted.sh` gained `--args`.

Second review round: string copies freed only when the object provably no longer points at them, ids checked at `Add` (explicit ids win over automatic ones before `Open`), `Open` fails cleanly, no RemoveGList of a gadget that was never added, nested `Run` stops on a pending close, the last two silent `Init` refusals, the AROS probe moved to `Create`. New test `Set_keeps_string_copies_...`: a fake class that ignores a set of `GA_Text` (the old code freed the copy it still pointed at: a read of freed memory under ASan), strgclass copies freed, `Init(ObjAttr)` with an uncreated object, the reviewer's id probe (both gadgets shown, ids 1001 and 1000, the duplicate refused before Create), a nested `Dispatch` returning false during a pending close.

Review of PR #89 (ownership, Close from a handler, the AROS probe, Cast types, refused starting attributes, string copies, gadget ids, hook rules): fixed as described above; `samples/boopsi` gained a Quit button (closes the window from its own handler), ownership checks (second owner, cycles, dispose down a chain), a refused-attribute check and a string-copy check.

Verified (after the review fixes):
- `dotnet test`: 547 tests, 545 passed, 2 skipped (the SDK-backed BindGen ones), 0 failed (after the second review round too). `BoopsiTests` (29): the m68k entry and MorphOS gate text; the `[HookEntry]` C functions called through their h_Entry addresses as AROS calls them, with void and negative int results; 10 `ES0099` forms; ES0102 on sdl; `string?` overloads; `Cast` with an interface, a delegate or a string (ES0099/ES0014); a `Hook` with a capturing lambda called through CallHookPkt and freed, under ASan/UBSan/LSan; `Keep` with a second owner, a cycle, a window as second owner, a hook with two owners, and kept objects and hooks disposed by the program (each object disposed once, no leak, under the sanitizers); the AROS `es_domethod` probe with host gcc against a shim that keeps the class 8 or 16 bytes before the object (OM_GET answered) and 48 bytes (not found: one message, DoMethod returns 0, no crash); `samples/boopsi` compiled for the Amiga build of the platform and run with host gcc under the sanitizers against a fake intuition.library: all checks ok, no leak, the Quit handler's Close put off until Dispatch returns (no use after free), and the order remove gadgets, close window, dispose every object (a button before its frame), free DrawInfo, unlock screen; the sample on dotnet with the host fake (WAIT); the sample compiles for m68k, AROS, os4, morphos.
- A1200, Kickstart 3.1 (40.069) in FS-UAE: `esharp test samples/boopsi --target m68k` host 37 ok, Amiga 37 ok, they agree (the lambda hook runs through CallHookPkt and the m68k entry; disassembly checked). Interactive, `run --args WAIT --input "click 400,300;rel -140,-115;press;rel 120,46;press;rel -51,0;down;rel -60,0;up;rel 33,44;press;key 65;key 10;key 11;key 19;key 19;key 36;rel -92,-144;press"`: button click, a page of the slider (45 to 54), a knob drag (13 IDCMPUPDATE events down to 24, matching `Get(Pga.Top)`), " 1200" typed and Return (`text accepted: Amiga 1200`), the edit hook saw 6 keys (it runs on input.device's task), the close gadget; 38 ok, no FAIL. `--input "click 400,300;rel -140,-115;press;rel 95,0;press"`: Hello, then Quit closes the window from its handler; every object disposed, no closure left, all ok. Screenshots looked at: framed Hello and Quit buttons, slider, string gadget in a ridge frame, topaz 8.
- Hosted AROS x86-64 (nightly 20261106, in esharp-aros-hosted): 37 ok, 0 traps; with `--args WAIT --input "click 75,57;click 220,84;click 47,37"`: the click, a slider page (45 to 55) and the close, 38 ok; `--input "click 75,57;click 192,57"`: Hello, then Quit, all ok, 0 traps (keys do not reach hosted AROS, so no typing there).
- AmigaOS 4.1 in the QEMU VM: `esharp test samples/boopsi --target os4` 37 ok, agrees with the host; `run --args WAIT --input "click 74,80;click 220,100;click 200,127;key 64;key 1;key 2;key 10;key 10;key 68;click 57,45"`: click, slider page, "Amiga 1200" accepted, the edit hook saw 6 keys (OS4 calls the hook as a PPC C function), 38 ok.
- MorphOS: builds (with the gate); not run. (samples/gui still does not build for MorphOS, a known older problem.)
- No regressions on the A1200 / 3.1 (before the review fixes, which touch only Boopsi.cs, the Cast intrinsics and the hook entries): closures 20 and inherit 24 (`esharp test`, host and Amiga agree), gui 7 and guiclass 8 ok with the AGENTS.md input lines.
- Hardware pack: `boopsi` in `RunAll` (37 ok expected) and the README (with the by-eye WAIT steps); rebuilt: 38 programs plus Summary, `obj/hwtest.adf` 840 KB used, 39 KB free, so it still fits a floppy (`boopsi` is 34412 bytes). The pack as a whole was not re-run.

Not verified / not done: real hardware; MorphOS runs; custom BOOPSI classes (MakeClass with a C# dispatcher: the `[HookEntry]` entry is what a dispatcher needs, but no class is built here); `groupgclass`/`modelclass`/`icclass` interconnection (ICA_TARGET to another object) and ICA_MAP; images other than frameiclass; gadget refresh after the window is covered (smart refresh, no refresh events asked for); more than 13 starting attributes; several windows; hooks called while the program is in the middle of something that is not reentrant (the hook's closure runs on the caller's task: on input.device for an edit hook).

## MUI wrappers (samples/mui, issue #40 part 2)

MUI on the BOOPSI core. MUI is third-party shareware, so all MUI facts here (class names, attribute and method numbers, special values, message layouts) come from the AROS Zune headers (AROS Public License), and the muimaster.library vectors come from the AROS fd file. Nothing is taken from the MUI distribution, and no MUI header is included on any target.

**Library.** `Mui.Open()` opens muimaster.library version 19 (MUI 3.8) or newer as an optional base. It returns false when MUI is missing, so a program says so and stops (the sample prints `skip: MUI not installed` and exits 0). `Mui.Close()` closes the library only when no MUI object is left. `MuiMaster` (`[LibraryBase("muimaster", ...)]`) declares `MUI_NewObjectA` (-30), `MUI_DisposeObject` (-36), `MUI_RequestA` (-42), `MUI_Error` (-66) and `MUI_MakeObjectA` (-120). `bindgen check <AROS fd dir> src/ESharp.Platform muimaster` gives 5 declarations, 0 problems. The emitter does not include `proto/muimaster.h` for these. It writes the calls itself, by vector, per target:
- m68k: `jsr -N(a6)` with the fd registers.
- AmigaOS 4: the 68k jump table through `IExec->EmulateTags` (the OS4 interface layout of MUI is not in the AROS sources, so it is not guessed).
- MorphOS: the emulation registers and `EmulCallDirectOS`.
- AROS: AROS's own `AROS_LC` macros.

`Mui.Request` wraps MUI_RequestA. Custom classes came later (issue #93): see "MUI custom classes".

**Classes.** `MuiObject : BoopsiObject` takes any MUI class by name. `Init`, `Set` and `Get` work as on the core, with the typed catalogue `Muia` (MUIA_* with their value types) and `MuiFrame`. The wrappers are:
- `MuiApplication` (title and base name; commodities and ARexx off) and `MuiWindow` (title, `Content`, `Id`, `Open`/`Close`/`IsOpen`, `OnCloseRequest`).
- `MuiGroup` (`Horizontal`, `Columns`, `Frame`, `Title`, `Add`).
- `MuiText`, `MuiLabel`, `MuiButton` (MUI_MakeObject button, `OnPressed`), `MuiCheckMark` (MUI_MakeObject checkmark, `Selected`, `OnToggled`).
- `MuiString` (`Contents`, `OnChanged`, `OnAcknowledged`), `MuiCycle` (`Active`, `OnActiveChanged`; the entry labels are copies the object keeps), `MuiSlider` (`Value`, `OnValueChanged`).
- `MuiGauge` (`Current`, `InfoText`), `MuiRectangle` (space, or `Gap(n)` through MUI_MakeObject HSpace).
- `MuiListview`: a Listview with a List of strings that MUI copies: `Add`, `Clear`, `Count`, `Active`, `Entry`, `OnActiveChanged`.

The generic notifications are `WhenChanged(attr, handler)` for int, bool and string attributes, and `WhenEquals(attr, value, handler)`.

**Notifications.** Each one is a `MUIM_Notify` on the object itself with `MUIM_CallHook`, a struct Hook of its own and `MUIV_TriggerValue`. The hook entry is a `[HookEntry]` method, so it uses the core's per-target entries. h_Data points at the notification object, which holds the C# delegate (an `[Owned]` parameter, so a capturing lambda becomes a heap closure the notification owns). MUI calls the hooks on the program's own task: inside `MUIM_Application_NewInput` (`Run`, `Step`), or inside the `Set` that changed the attribute. That is how the self-test drives them. So a handler may allocate, print and set other objects. The rules from the core still apply:
- `MuiApplication.Quit()` is the way out of a handler.
- A `Dispose` of a MUI object inside a handler is refused with a one-time message, and the object stays usable. MUI is still inside that object's method when the hook returns.
- A window's close gadget quits the application unless the window has its own `OnCloseRequest` handler.
- `Run` is NewInput plus `Exec.Wait` (new binding, LVO -318) on the returned signals and Ctrl-C.

**Ownership.** Ownership follows MUI:
- An object added to a group (OM_ADDMEMBER; MUIM_Group_InitChange/ExitChange around it only while its window is open) belongs to the group. A window's content (MUIA_Window_RootObject) belongs to the window, a listview's list (MUIA_Listview_List) to the listview, and a window added to the application (OM_ADDMEMBER) to the application.
- `Create()` on the application makes the whole tree: a starting-attribute child first, then the parent, then the members.
- The program disposes only the root: `app.Dispose(); Heap.Free(ref app);` runs one MUI_DisposeObject. MUI disposes the children, then the C# side frees every wrapper, its string copies and its notifications (hooks and closures) without disposing anything a second time.
- An object that is in no tree is disposed by itself, with MUI_DisposeObject.
- A group member or a window that the program disposes is first taken out of its parent (OM_REMMEMBER; a window is closed first).
- A window's content and a listview's list cannot be disposed on their own (refused with a message).
- If a parent cannot be made, the children it was given as starting attributes count as gone. That is a leak at worst, never a second dispose, because whether MUI disposed them is not known.
- `HasOwner`, `Keep` and `BoopsiWindow.Add` see the MUI parent, so an object has one owner.
- `Mui.Live` counts the MUI objects that are made and not yet disposed.

**Core changes.** `BoopsiObject` gained internal virtual steps that the MUI classes override: how the system object is made and disposed, the tree in `Create`, what else the object owns, a refused `Dispose`, and the host's notification. `KeepString` is now internal. The host fake: `Mui.HostAvailable`, `HostPress`, `HostType`, `HostCloseRequest`. Notifications fire on every `Set`, and only the classes the wrappers use are known.

**Negative trigger values.** `WhenEquals(IntAttr, value)` keeps the trigger as a pointer-sized value, sign-extended as `Set` widens an int. It was a 32-bit value, so on 64-bit targets (AROS x86-64, the dotnet host) a notification on -1 or any other negative value never fired. samples/mui checks it with a lone slider from -10 to 10.

**Emitter fixes on the way:**
1. An override in a library class was left out of the class's function table when nothing named it. `BoopsiObject.Create` calls the virtual `CreateAll` that `MuiObject` overrides, so the slot was null, and on AmigaOS 4 the program jumped to address 0. Now the overrides of reached members are reached in every class that is constructed. A BOOPSI-only program still does not pull in MUI.
2. An object initializer can set properties that have a setter body (`new MuiText("x") { Frame = MuiFrame.Text }`), also after constructor arguments. The setters run in order.

Verified:
- `dotnet test`: 559 tests, 557 passed, 2 skipped (the SDK-backed BindGen ones), 0 failed. `MuiTests` has 12:
  - samples/mui as generated C, run with host gcc under ASan/UBSan/LSan against a fake muimaster.library: all ok, 23 objects made and 23 disposed (each once, children before parents, never by intuition's DisposeObject), 4 group changes, no leak, the library closed.
  - The skip line when muimaster.library does not open.
  - samples/mui on dotnet, default and WAIT.
  - The sample compiles for m68k, AROS, os4 and morphos with the per-target calls and no MUI header.
  - The vectors in the C# declarations, the m68k `jsr` offsets, the AROS call numbers and the MorphOS calls agree with the fd bias.
  - ES0102 on sdl.
  - A library override in the function table, and none of MUI in a BOOPSI-only program.
  - Setter properties in object initializers, run with gcc.
- AmigaOS 4.1 in the QEMU VM (MUI 5):
  - `esharp test samples/mui --target os4`: host 32 ok, OS4 32 ok, and they agree on every check (re-run after the negative-trigger fix).
  - `run --screenshot`: the screenshot was looked at. It shows the window with the text, Hello and Quit, the framed "Settings" group with slider (42), string, cycle (Three) and check mark, the gauge at "99 %" and the list with its second line selected.
  - `run --args WAIT --input "click 562,360;click 655,406;click 566,591;click 653,360"`: Hello pressed (handler ran), the slider paged to 43, list line 2 picked, Quit from its handler. 31 ok, no FAIL.
- Hosted AROS x86-64 (Zune, in esharp-aros-hosted):
  - `tools/aros-hosted/aros-hosted.sh mui --docker esharp-aros-hosted --wait 0 --shot shot.png`: 30 ok, 0 traps. After the negative-trigger fix, `--wait 0` again: 32 ok, 0 traps, all ok. The screenshot was looked at and shows the same window in Zune's look. With the default 2 s wait the shot came after the window had closed again.
  - `--args WAIT --input "click 300,163;click 400,189;click 297,298;click 393,163"`: the same events as on OS4 (Hello, slider 43, list line 2, Quit). All ok.
- A1200, Kickstart 3.1 (40.069) in FS-UAE, without MUI: `esharp run samples/mui --model A1200 --kickstart <rom>` prints `skip: MUI not installed` and exits 0. No regressions on the same machine: `esharp test samples/boopsi --target m68k` (37 ok on host and Amiga, they agree), and `samples/gui` with the AGENTS.md input line (7 ok).
- A1200 in FS-UAE with classic MUI (`esharp test samples/mui --target m68k --model A1200 --kickstart <rom> --mui V --workbench <Workbench disk 2 .adf>`, issue #95), re-run after the startup fix below:
  - Kickstart 3.1 (40.069), Workbench 3.1 disk 2, MUI 3.8 (muimaster 19.35): host 32 ok, Amiga 32 ok, they agree on every check.
  - Kickstart 3.1, MUI 5.0-20210831 (muimaster 21.227): host 32 ok, Amiga 32 ok, they agree.
  - Kickstart 3.0 (39.106), Workbench 3.0 disk 2, MUI 3.8: host 32 ok, Amiga 32 ok, they agree.
  - Kickstart 3.0, MUI 5: host 32 ok, Amiga 32 ok, they agree. (Kickstart 3.0 is not in the test matrix; this is what happened, not a support promise.)
  - `run --screenshot` (Kickstart 3.1, MUI 3.8 and MUI 5): the screenshots were looked at. Both show the window on the Workbench screen with the text, Hello and Quit, the "Settings" group (slider 42, string, cycle Three, check mark), the gauge at "99 %" and the list with its second line selected. Under MUI 5 the window came out narrow, with a scroll bar on the right (no MUI settings saved in ENV:).
  - `dotnet test` after the fix: 577 tests, 575 passed, 2 skipped (the SDK-backed BindGen ones), 0 failed (MuiEmulatorTests checks the LIBS: order and the `Stack` line, and reads the stack from a written icon).
  - So the m68k register calls into muimaster.library, the m68k `[HookEntry]` hooks, the 32-bit tag and message layouts and MUIM_Notify/CallHook work against a real MUI. The wrappers needed no change.
  - Cause of the earlier failures, both in the `--mui` startup of the emulator run, not in the wrappers:
    1. MUI 3.8, 9 FAIL (the application could not be made, so "the window opens" and the checks that need the window failed): the boot volume has no Libs drawer, so the boot assigns LIBS: to DH0:, where the program lies, and the startup only added the Workbench disk's and MUI's Libs to it. MUI 3.8 loads its external classes as `LIBS:mui/<class>` (Gauge.mui is one). In samples/mui the program file is named `mui`, so `LIBS:mui/Gauge.mui` met a file in the first LIBS: directory and the search stopped there: the gauge, so the root group, so the window and the application could not be made. The same sample built under another name passed, and so did the same name run from a drawer. Now the startup replaces LIBS: with the Workbench disk's Libs, then adds MUI:Libs.
    2. MUI 5, no end within 120 s: a "MUI warning" requester (seen on a screenshot) said the stack of 4096 bytes is below the recommended 32000 and waited for "Continue nevertheless" or "Exit immediately". The boot shell runs the program with its 4096 bytes. Now the startup sets `Stack` to the stack the program's icon asks for (EsStackSize, default 65536), as a start from Workbench would.
  - Re-run on the other MUI targets after the fix: `esharp test samples/mui --target os4` host 32 ok, OS4 32 ok, they agree; hosted AROS (`aros-hosted.sh mui --wait 0`) 32 ok, 0 traps, all ok.
  - A program started from a shell with the default stack of 4096 bytes would show that MUI 5 requester on a real Amiga too: start MUI programs from their icon or raise the shell's stack.

Not verified:
- MorphOS runs.
- Real hardware. samples/mui is not in the hardware pack.
- `Mui.Request` (a requester waits for the user: not in the self-test).
- A button press driven by the program. MUIA_Pressed cannot be set, so buttons were only pressed by scripted clicks.
- Typing into the string on a target (`OnAcknowledged` ran on the host only).
- Several windows.
- Disposing a window while the application runs.
- `MuiRectangle()` without `Gap`.
- MUI custom classes: see "MUI custom classes" (issue #93).

## Classic MUI in the emulator (`esharp run|test --mui 3.8|5`, samples/muicheck, issue #40)

MUI is shareware whose licence allows only verbatim copies of the whole archive, so nothing of it is in the repo or the packages. `tools/mui/fetch-mui.sh 3.8|5` downloads the official archive from the MUI project's GitHub releases (`amiga-mui/muidev`: tag `3.8`, `mui38usr.lha`; tag `MUI-5.0-20210831`, `MUI-5.0-20210831-os3.lha`), checks a pinned sha256, and unpacks it with the toolchain image's `lha` (no network) into `${ESHARP_MUI_CACHE:-${XDG_CACHE_HOME:-~/.cache}/esharp/mui}/<version>`. `esharp run` and `esharp test` (m68k, FS-UAE) take `--mui 3.8|5` (or `ESHARP_MUI`) and `--workbench <adf>` (or `ESHARP_WORKBENCH`): the unpacked drawer is mounted read-only as the volume `MUI:`, the user's own Workbench 3.x disk 2 read-only as `DF0:`, and the generated startup assigns `C:`, `T:`, `LIBS:` (Workbench disk, then `MUI:Libs ADD`), `ENV:` (RAM:Env), `LOCALE:` and `IMAGES:` before the program. A bare Kickstart does not work: `muimaster.library` (3.8: v19.35, 5: v21.227) opens asl, commodities, icon, locale, iffparse and others from the Workbench disk, and a bare boot has no `C:Assign`; with MUI's Libs only copied into the boot volume, OpenLibrary failed. `--target os4`, `aros` and `sdl` reject `--mui` (OS4 has MUI 5 installed, AROS has Zune). `samples/muicheck` opens `muimaster.library` and prints its version (offset of `lib_Version` chosen by pointer size). The fetch script ships in the tool package (`mui/` next to `esharp.dll`; the error message prints that path), the MUI archive never does. tools/mui/README.md has the details.

Verified (A1200, FS-UAE 3.2.35, headless; `esharp run samples/muicheck ... --kickstart <rom> --mui V --workbench <disk 2 .adf>`): Kickstart 3.1 (40.069) with Workbench 3.1 (40.42) disk 2: `--mui 3.8` prints `muimaster.library 19.35`, `--mui 5` prints `muimaster.library 21.227`; Kickstart 3.0 (39.106) with Workbench 3.0 (39.29) disk 2: the same two lines. Without `--mui`, or with `--mui` and no Workbench disk, the library is not found. `esharp test samples/phase2 --target m68k --mui 3.8 --workbench <disk>`: 32 ok, host and Amiga agree. 560 unit tests pass (15 new in MuiTests: version parsing, cache lookup, startup lines, FS-UAE config, the pinned hashes).

Also verified: `samples/muicheck` on the AmigaOS 4.1 VM (`--target os4`, MUI 5 installed) prints `muimaster.library 21.210`, and on hosted AROS x86-64 (`--target aros`, Zune, `tools/aros-hosted/aros-hosted.sh muicheck --docker esharp-aros-hosted`) prints `muimaster.library 19.81`. The built tool folder contains `mui/fetch-mui.sh` (checked in a Release build; the packed `.nupkg` was not inspected here, CI's packages job covers the pack).

The startup replaces LIBS: with the Workbench disk's Libs before it adds MUI:Libs (with ADD, the boot's LIBS: on DH0: stayed first, and a program named like a LIBS: drawer, `mui`, hid MUI 3.8's external classes) and ends with `Stack <EsStackSize>` (the boot shell's 4096 bytes made MUI 5 stop on a "raise the stack size" requester). samples/mui opens its window with MUI 3.8 and 5 on Kickstart 3.1 and 3.0, see "MUI wrappers".

Not verified: MUI preferences and patterns (none are saved in ENV:), registered MUI, MUI 4, a Workbench disk other than 3.0/3.1 disk 2, Windows or macOS (the fetch script needs bash, curl, sha256sum and docker), `--mui` with `--input` or screenshots.

## MUI custom classes (samples/muiclass, issue #93)

A program can write a MUI class in C#: a class derived from `MuiArea` (a subclass of Area.mui) or `MuiCustomGroup` (a subclass of Group.mui, with
`MuiGroup`'s `Add`, `Horizontal`, `Frame`, `Title`). Both are `[Extensible]`. The program overrides the members MUI calls, which are protected
virtual members of `MuiObject` (a program cannot derive from any other MUI class, so only these two expose them):
- `OnAskMinMax(ref MuiMinMax size)` (MUIM_AskMinMax): the contents' sizes, added to what the superclass asks for (frame and spacing; a group's
  children). Minimum <= default <= maximum is kept, and `MuiMinMax.Unlimited` (MUI_MAXMAX, 10000) as a maximum lets the object grow.
- `OnDraw(MuiRender r)` (MUIM_Draw with MADF_DRAWOBJECT or MADF_DRAWUPDATE, after the superclass drew frame and background, or a group its
  children). `MuiRender` has the contents box (`Left`, `Top`, `Width`, `Height`), `Update`, `RastPort`, `Pen(MuiPen.X)` (MUI's pens:
  Shine ... Mark, MPEN_*) and `Fill`, `Line` and `Text` with coordinates relative to the contents. `Fill` and `Line` are clipped to the
  object's whole box. `Graphics.Draw` (graphics.library -246) is a new binding for `Line`.
- `OnSetup`/`OnCleanup` (MUIM_Setup/Cleanup; false from `OnSetup` stops the window opening), `OnSet(attr, value)` (OM_SET, and the starting
  attributes at OM_NEW; the superclass sees every attribute too, so MUI's notifications work on the class's own attributes), `OnGet(attr,
  out value)` (OM_GET: true answers it, false lets the superclass answer), `OnMethod(id, msg, out result)` (any other method: the class's own,
  sent with `DoMethod`).
- `Redraw()` (protected) calls MUI_Redraw with MADF_DRAWOBJECT while the object is shown (between MUIM_Show and MUIM_Hide) and does nothing
  otherwise; `IsShown` says which.

**How it works.**
- One MUI class per MUI superclass, not per C# class: `MUI_CreateCustomClass(NULL, "Area.mui" or "Group.mui", NULL, one pointer of
  instance data, dispatcher)` when the first object of it is made. The C# virtual calls choose each class's behaviour, so a second MUI class
  per C# class would add nothing. A class is deleted with `MUI_DeleteCustomClass` once MUI has disposed its last object (after the
  outermost MUI_DisposeObject returns, so never inside its own dispatcher, and again at `Mui.Close`). A later object makes it again.
- New muimaster.library calls, by vector from the AROS fd as before: `MUI_Redraw` (-102), `MUI_CreateCustomClass` (-108),
  `MUI_DeleteCustomClass` (-114), written per target like the others (m68k registers, OS4 through EmulateTags on the 68k jump table,
  MorphOS emulation registers, AROS `AROS_LC`). `bindgen check <AROS fd dir> src/ESharp.Platform muimaster`: 8 declarations, 0 problems.
- The objects are made with `Intuition.NewObjectA(mcc_Class, NULL, tags)`. `mcc_Class` is the seventh pointer of struct MUI_CustomClass
  (Zune header). The dispatcher is one `[HookEntry]` method for both classes, so its entry per target is the existing hook machinery: the
  m68k register entry (a0 class, a2 object, a1 message), the MorphOS gate, plain C on AmigaOS 4 and AROS.
- OM_NEW: the superclass first; then the instance data (one pointer) gets the C# object that is being made, and `OnSet` sees the starting
  attributes. OM_DISPOSE clears it before the superclass runs. A method for an object whose pointer is null goes straight to the superclass.
- Two new emitter natives, `es_dosupermethod` and `es_inst_data` (`BoopsiNative`): DoSuperMethodA is amiga.lib on m68k and MorphOS and
  `IDoSuperMethodA` on AmigaOS 4. On AROS the superclass's dispatcher is called directly through `cl_Super`, so the object layout that
  `es_domethod` probes for is not needed. The instance data is `obj + cl_InstOffset`.
- Message layouts from the Zune headers, all pointer-sized slots (STACKED on AROS): MUIP_AskMinMax (a struct MUI_MinMax of six WORDs),
  MUIP_Draw (flags), MUIP_Setup (the render info), opSet, opGet.
- `MuiRender` reads struct MUI_RenderInfo (pens: the fourth pointer, a UWORD array; rast port: the sixth) and struct MUI_AreaData (font,
  box, addleft/addtop/subwidth/subheight) from the Zune headers. Where MUI_AreaData starts in an object is not fixed: on m68k and OS4 it is
  after the seven fields of MUI_NotifyData (28 bytes), but the AROS headers of 2026 align class instance data (`CLASS_INSTANCE_ALIGN`), so
  the x86-64 header of the toolchain image says 56 while the hosted nightly has it at 64 (seen on a run, see below), and the i386 SDK says
  32 instead of 28. So at the first MUIM_Setup the dispatcher looks for the render info pointer (MUIP_Setup's) in pointer-sized slots 7
  to 16 of the object, the start of MUI_AreaData, and remembers the offset. If it is not found, it says so once and `OnDraw` is not
  called.
- Callbacks run on the program's own task. `Mui.HandlerDepth` counts them, so disposing a MUI object in them is refused, as in a
  notification handler.
- `Mui.CustomClasses` (made and not deleted), `Mui.CustomClassesMade`, `Mui.CustomObjects` (OM_NEW minus OM_DISPOSE as the dispatcher
  saw them) are there for checks.
- Host: the fake runs the same C#. A custom object sees its starting attributes in `OnSet`, `Set` goes to `OnSet` before the
  notifications, `Get` asks `OnGet` first, `DoMethod` asks `OnMethod`. Opening a window runs setup, size, show and draw for each custom
  object in it; closing runs hide and cleanup, and so does disposing one that is shown. `MuiRender` counts its calls (`HostFills`,
  `HostLines`, `HostTexts`). `BoopsiObject` gained three internal host hooks for this (`HostCreated`, `HostGet`, `HostMethod`).
- Input events: see "Input events in custom classes (issue #114)" below. MUIM_HandleInput (the older input method) is not wrapped.

**samples/muiclass.** `BarGraph : MuiArea` (C# `OnAskMinMax` and `OnDraw`: eight bars, a base line and the level as text; its own
attribute `Level` with `OnSet`/`OnGet` and a redraw; its own method `Reset`) and `Panel : MuiCustomGroup` (a horizontal framed group with
both graphs in it; its own attribute `Mark` drawn as a stripe under the children). A slider's notification sets the first graph's level
and the group's mark. The checks (25 with #93, 26 since #114; the same on every system):
- The class is made once for both graphs (2 classes: Area and Group). The dispatcher saw 3 objects made (OM_NEW).
- The starting levels went through OnSet and read back through OnGet.
- Setup reached each object once, AskMinMax asked the graphs, Draw drew the graphs and the group.
- The own attribute's round trip, its redraw, and a MUI notification on it.
- The slider's notification set the level and the mark, and both redrew.
- The own method ran. MUI's attributes still go to the superclass.
- Cleanup reached each object at window close.
- Disposing the application disposed the custom objects (OM_DISPOSE), and both classes were deleted after their last object.
- A lone graph makes the Area class again; nothing is drawn while it is not shown; the class is deleted with it.
- Every MUI object was disposed; no closure is left.
WAIT runs until the window is closed or Quit is pressed and prints the level and the number of draws (since #114: the levels, clicks
and moves; see below).

Verified (#93):
- `dotnet test`: 627 passed, 2 skipped (the SDK-backed BindGen test and the OS4 VM test, which need an SDK or a VM), 0 failed. New in MuiTests:
  - samples/muiclass as generated C, run with host gcc under ASan/UBSan against the fake muimaster.library. The fake grew custom classes:
    MUI_CreateCustomClass/DeleteCustomClass/Redraw, NewObjectA of a private class with OM_NEW through the dispatcher, DoSuperMethodA as
    Area/Group, objects laid out like MUI_NotifyData and MUI_AreaData, a window that opens with setup, size, show and draw for each custom
    object, and graphics calls that refuse an unclipped rectangle. Result: all ok; the Area class made and deleted twice and the Group class
    once, each deletion after the objects; 10 objects made and 10 disposed; nothing drawn while hidden.
  - samples/muiclass on dotnet, default and WAIT.
  - The sample compiles for m68k, aros, aros-i386, os4 and morphos with the per-target DoSuperMethodA and no MUI header.
  - The vector test covers the three new calls.
  - A program class may derive from `MuiArea` and `MuiCustomGroup` but not from `MuiText` (ES0007).
  - The existing samples/mui fake run is unchanged (23 made, 23 disposed, 4 group changes).
- Host: `esharp test samples/muiclass`: 25 ok; with `--args WAIT`: `events: level 90, 4 draw(s)`, 25 ok.
- A1200, Kickstart 3.1 (40.069), Workbench 3.1 disk 2, FS-UAE (`esharp test samples/muiclass --target m68k --docker <m68k image>
  --model A1200 --kickstart <rom> --mui V --workbench <wb>`):
  - MUI 3.8 (muimaster 19.35): host 25 ok, Amiga 25 ok, they agree on every check.
  - MUI 5 (muimaster 21.227): host 25 ok, Amiga 25 ok, they agree.
  - `run --headless --screenshot` with MUI 3.8 and with MUI 5: the screenshots were looked at. Each shows the window "E# MUI custom class"
    on the Workbench screen with the text "Level 70", the framed "Custom group" with the two graphs side by side (bars rising to 70% and
    20%, alternating fill and shadow pens, the level as text in the top left corner, a base line), the slider at 70 and Quit. Under both,
    the group's stripe under the graphs is blue for 70% of its width and black for the rest.
  - No regression: `esharp test samples/mui --target m68k ... --mui 3.8 --workbench <wb>`: host 32 ok, Amiga 32 ok, they agree.
  - All of the above, and the OS4 and hosted AROS x86-64 runs below, were re-run on the final code: same counts, screenshots looked at again.
- AmigaOS 4.1 in the QEMU VM (MUI 5): `esharp test samples/muiclass --target os4`: host 25 ok, OS4 25 ok, they agree. `run --screenshot`: the
  screendump was looked at and shows the same window in the OS4 look (graphs 70% and 20%, level texts, the stripe, slider at 70). So
  MUI_CreateCustomClass through the 68k jump table takes the native PowerPC dispatcher on OS4.
- Hosted AROS x86-64 (Zune, nightly 20261106, esharp-aros-hosted): `tools/aros-hosted/aros-hosted.sh muiclass --docker esharp-aros-hosted
  --wait 0 --shot shot.png`: 25 ok, 0 traps, all ok. The screenshot was looked at and shows the same window in Zune's look. With
  `--args WAIT --input "click 560,341;sleep 1;click 560,341;sleep 1;click 400,360"`: two slider pages by mouse (level 72, the graph redrawn
  from the slider's notification, 8 draws), then Quit; all ok, 0 traps. A debug run printed the area data offset: 64 on this nightly
  (the 2020 header of the toolchain image says 56), and the instance data 448 bytes into an Area object, 640 into a Group object.

Not verified (#93):
- Hosted AROS i386 on this branch: 1 run of 7 passed (the output stopped after `SHOT` and the shell trapped in `GetVar`, or a debug build
  trapped at its first `Create` with `IntuitionBase` overwritten). Fixed since: an AROS bug, see "MUI on hosted AROS: the muiscreen.library
  handler stack (issue #105)"; samples/muiclass now gives 25 ok in 10 of 10 runs on hosted i386.
- MorphOS (the C compiles; not built or run).
- Real hardware.
- `OnSetup` returning false, and `MuiRender.Update` true: MUI_Redraw is always called with MADF_DRAWOBJECT, and no system sent DRAWUPDATE
  in these runs.
- A custom class inside a virtual group or a scrolled area, and several windows.
- MUI preferences other than the defaults. The pens are whatever MUI's defaults gave on each system.

### Input events in custom classes (issue #114)

A class derived from `MuiArea` or `MuiCustomGroup` gets mouse and key input:
- `WantEvents(uint idcmpClasses)` (protected) names the IDCMP classes it wants: `MuiEvent.MouseButtons`, `MuiEvent.MouseMove`,
  `MuiEvent.RawKey` (IDCMP_MOUSEBUTTONS, IDCMP_MOUSEMOVE, IDCMP_RAWKEY), or'ed. Called in the constructor or `OnSetup`, and again at any
  time (also from `OnEvent`, the usual way to ask for mouse moves only while dragging); `WantedEvents` reads it back.
- At MUIM_Setup (after `OnSetup` said yes) the dispatcher allocates one struct MUI_EventHandlerNode per object (`ehn_Object` the object,
  `ehn_Class` the custom class, `ehn_Events` the wanted classes, priority and flags 0) and, if the set is not 0, sends
  MUIM_Window_AddEventHandler to the window object (the first field of the render info). `WantEvents` with another set while the object
  is set up sends MUIM_Window_RemEventHandler (if the node is in the list), changes `ehn_Events` in place and sends
  MUIM_Window_AddEventHandler with the same node (unless the set is 0); this works from inside any object's `OnEvent`. The node is freed
  only at MUIM_Cleanup (before the superclass's, after MUIM_Window_RemEventHandler), so no node is freed while MUI may be going through
  the window's handler list (review of #115: the first version freed and replaced it on every change).
- MUIM_HandleEvent (`{ imsg, muikey }`) with an event of a wanted class calls `OnEvent(MuiEvent e)` while the object is shown (between
  MUIM_Show and MUIM_Hide; hidden, it answers 0). `MuiEvent` has the IntuiMessage's `Class`, `Code`, `Qualifier`, the mouse position
  relative to the contents box (`MouseX = imsg->MouseX - _mleft`, `MouseY = imsg->MouseY - _mtop`, from struct MUI_AreaData as `MuiRender`
  reads it), the contents' `Width`/`Height`, `Inside`, and MUI's key code `MuiKey` (MUIKEY_*, -1 for none). `MuiEvent.SelectDown` /
  `SelectUp` are the left button's codes. `OnEvent` returning true answers MUI_EventHandlerRC_Eat. Events of other classes go to the
  superclass.
- MUI facts from the AROS Zune headers only: MUIM_HandleEvent 0x80426d66, MUIM_Window_AddEventHandler 0x804203b7,
  MUIM_Window_RemEventHandler 0x8042679e, MUI_EventHandlerRC_Eat 1, MUIP_HandleEvent, the node's fields. The node's layout is measured per
  target by `tools/BindGen/layouts.sh`: a new `zune NAME` line in `tools/BindGen/structs.txt` makes `bindgen offsets` copy struct NAME from
  the AROS Zune `libraries/mui.h` (read from the AROS image at measuring time, `--zune`, never stored) into the measuring C file, compiled
  with each target's compiler and its own exec/intuition headers; no target's MUI header is used. Results (`Layout.g.cs`): size 48,
  `ehn_Priority` 17, `ehn_Flags` 18, `ehn_Object` 24, `ehn_Class` 32, `ehn_Events` 40 on AROS x86-64; 24, 9, 10, 12, 16, 20 on AROS i386 and
  m68k. The same spec measured with the os4 and morphos compilers gave the m68k numbers (that run is not stored; the PowerPC targets use the
  m68k layout as before). The IDCMP classes and SELECTDOWN/SELECTUP are measured header constants too (the host fake uses the same numbers).
- Host: `HostEvent(class, code, qualifier, x, y)` (public, host only) sends an event to a shown custom object that wants its class, with x
  and y relative to the contents box; true if `OnEvent` ate it.
- Review nits of #113 fixed: OM_NEW counts an object only when its instance pointer is set, and OM_DISPOSE counts it off only then, so a
  stray OM_DISPOSE cannot take `Mui.CustomObjects` below the objects that live. GUIDE says which `MuiRender` calls clip (`Fill`), clamp
  (`Line`) or neither (`Text`, which also leaves the font and JAM1 set), and that `OnSet` at OM_NEW runs before the wrapper has its object.
- `esharp run --target os4 --input` takes `down` and `up` (hold and release the left button) like FS-UAE, for drags.

samples/muiclass: `BarGraph` wants MouseButtons, and MouseMove only while dragging (`WantEvents` from inside `OnEvent`). The left button
down inside a graph sets its level from the mouse's x (left 0, right 100) and starts a drag; moves while dragging set it again; the
button up ends it; each of these is eaten. The level goes
through `Set` (OM_SET), so the notification on it moves the slider (and the slider's moves the group's mark and the status text); each
side sets the other only when the value differs, so the notifications stop. New check (26 in all): the slider followed a `Set` of the
graph's level. With `WAIT` the window runs until Quit and prints `events: level L, slider S, N click(s), M move(s); other graph L2, N2
click(s)` and checks that the slider follows the graph; `WAIT CLICKS` (scripted runs) also checks that a click reached a graph. On the
host, `WAIT` sends a click at the middle of the first graph, a move to its right edge and the button up (the second graph is sent the
click and the move outside its box and leaves them), then Quit.

Verified (#114):
- `dotnet test`: 638 passed, 2 skipped (the SDK-backed BindGen test and the OS4 VM test), 0 failed (also after the review change of
  `WantEvents`, with the sample asking for moves only while dragging; the muiclass `test` and scripted `WAIT CLICKS` runs below on m68k
  with MUI 3.8 and 5, OS4 and hosted AROS were repeated with it and gave the same lines; the screenshots and the samples/mui run were not
  repeated). New or changed:
  - `Muiclass_sample_takes_clicks_and_drags_through_MUIM_HandleEvent_against_the_fake_muimaster`: samples/muiclass with `WAIT CLICKS` as
    generated C under ASan/UBSan. The fake window object keeps the handler list (MUIM_Window_AddEventHandler/RemEventHandler, refused on any
    other object), defines struct MUI_EventHandlerNode as the Zune header does (compiled by host gcc, so the generated C's Layout offsets
    must agree), and at the first MUIM_Application_NewInput sends MUIM_HandleEvent to each node that wants the class, in order, until one
    eats it: a click in the graphs' contents, a move, the button up, a click and button up outside, a raw key (wanted by none), then quit.
    The fake also notes when an object adds a node other than its first one (a node freed and replaced). Result: 4 adds and 4 removes
    (each graph's at setup and cleanup, and the first graph's node out and back in at the button down and up, from inside `OnEvent`),
    always the same node per object, 8 events sent (the move only to the dragging graph), 3 eaten (all by the first graph),
    `events: level 100, slider 100, 1 click(s), 1 move(s); other graph 20, 0 click(s)`, all ok.
  - samples/muiclass on dotnet with WAIT: the same events line. The os4 input translation test covers `down`/`up`.
- Host: `esharp test samples/muiclass`: 26 ok; with `--args WAIT`: 28 ok and the events line above.
- A1200, Kickstart 3.1 (40.069), Workbench 3.1 disk 2, FS-UAE (m68k image):
  - `esharp test samples/muiclass --target m68k ... --mui 3.8` and `--mui 5`: host 26 ok, Amiga 26 ok, they agree (each).
  - `esharp run samples/muiclass --headless ... --args "WAIT CLICKS" --input "click 400,300;down;rel -40,0;rel -40,0;up;rel 200,0;press;rel
    -52,128;press"` (MUI 3.8; MUI 5 lays the window out taller: last move `rel -40,155`): a click on the first graph, a drag to the left,
    a click on the second graph, Quit. MUI 3.8: `events: level 20, slider 20, 2 click(s), 9 move(s); other graph 17, 1 click(s)`, 28 ok,
    all ok. MUI 5: `events: level 19, slider 19, 2 click(s), 8 move(s); other graph 17, 1 click(s)`, 28 ok, all ok.
  - The same input without the Quit and with `--screenshot`, MUI 3.8 and MUI 5, looked at: the first graph's bars low with "20%" (MUI 5:
    "19%"), the status text "Level 20" (19), the slider's knob at 20 (19) near its left end, the group's stripe short; the second graph at
    "17%", the pointer over it. A single click (`click 400,300` only) showed "Level 62" (MUI 5: 61) with the graph, slider and status agreeing.
  - No regression: `esharp test samples/mui --target m68k ... --mui 3.8`: host 32 ok, Amiga 32 ok, they agree.
- AmigaOS 4.1 in the QEMU VM (MUI 5): `esharp test samples/muiclass --target os4`: host 26 ok, OS4 26 ok, they agree. `run --args "WAIT
  CLICKS" --input "click 590,505;down;rel -50,0;rel -50,0;up;click 740,505;click 640,590"`: `events: level 24, slider 24, 2 click(s), 6
  move(s); other graph 49, 1 click(s)`, 28 ok, all ok. The screendump of the same input without Quit was looked at: "Level 24", the first
  graph at 24%, the slider at 24, the second graph at 49% with the pointer over it.
- Hosted AROS x86-64 (Zune, esharp-aros-hosted): `aros-hosted.sh muiclass --wait 0`: all ok. `--args "WAIT CLICKS" --input "drag 350,295
  230,295;click 500,295;click 400,359"`: `events: level 14, slider 14, 1 click(s), 10 move(s); other graph 49, 1 click(s)`, 28 ok, all ok,
  0 traps. The screenshot without Quit was looked at: "Level 14", the first graph at 14%, the slider at 14, the second graph at 49%.

Not verified (#114):
- `MuiEvent.RawKey` and `Qualifier` on any system (only the fake sent a raw key, to a class that did not want it), `MuiKey` values.
- `WantEvents` called from one object's `OnEvent` for another object (only an object's own change from its `OnEvent` ran on the systems
  above); a custom group (`MuiCustomGroup`) with events.
- Events while the object is set up but hidden (a page group), several windows, MUI_EHF_* flags and node priorities (always 0 here).
- Hosted AROS i386 (see above), MorphOS (the C compiles), Kickstart 3.0, real hardware.

## Hardware test pack (tools/hwpack, samples/hwinfo; issue #7)

`tools/hwpack/build.sh <m68k image>` builds every current sample for the A1200 (68020) plus `samples/hwinfo` and a small counting helper (`tools/hwpack/summary`, built like a sample: AmigaDOS cannot count lines) into one drawer (`obj/hwtest`) that can be copied to a real A1200 or THEA1200 and run with `Execute RunAll`. The build fails if `RunAll` and the sample list drift apart. `RunAll` first runs `hwinfo` (Kickstart, library and Workbench versions, CPU and FPU flags, mains frequency, free chip and fast RAM, AGA), then 35 runs of 34 programs through `RunOne`: hello, phase2, memory, generics, properties, operators, language, collections, inherit, lambdas, linq, refs, patterns, objects (also with the AmigaDOS arguments `SEED=42 auto`), mainargs (with `ONE "two words" RC=3`), names, dates, interrupts, trace (the serial note is in the README), timing, fonts, iff, controls, keys (waits ten seconds without a key), asl (allocation checks, no requester), screen, aga, assets, rtg, sound, dbuffer, textspeed, cards, rummy. `gui` is in the drawer but not in `RunAll` (it waits for clicks); the README lists gui, the asl requester and the keys key press with by-eye steps, what to look at in the picture and sound programs, and what to send back. Per program: `results/NAME.txt` (output), `results/NAME.sum` (ok / FAIL / skip counts, the FAIL lines, and the emulator's count for comparison), `results/PROGRESS.txt` (the program started last, to pin a crash on), a line in `SUMMARY.txt` (which also holds the hwinfo report, start and finish times and the list of programs to look at). A program that returns a failure code is noted and the run continues; after a crash and reset `Execute RunAll RESUME` skips the programs that finished and the one that was running (it is flagged, with the line to run it by hand; marker `results/NAME.run`), and removes its resume marker at the end so `RunOne` by hand works afterwards. `tools/hwpack/run-emulated.sh <pack> --rom <kickstart> --wb <Workbench 3.1 disk>` runs the whole pack in FS-UAE (the Workbench disk is read in place as DF0:, only for the `C:` commands and `LIBS:`).

Stricter profile: `run-emulated.sh` uses FS-UAE's most accurate setting (`accuracy = 1`) with 2 MB chip and no fast RAM (`--profile stock`, the default) or with 8 MB fast RAM (`--profile fast8`). FS-UAE does not model the THEA1200's FPGA, a real display or a real disk, so this is the closest an emulator gets, not a stand-in for the run.

Verified (pack built from this change with `amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859`, run in FS-UAE 3.2.35, A1200, Kickstart 3.1 40.069, Workbench 3.1 40.42 disk read-only as DF0:): `RunAll` (and then `RunAll RESUME`, which skipped all 35) ran to the end with the `stock` profile: hwinfo reported exec 40.10, 68020 and no 68030/040/060, no FPU, PAL 50 Hz, 1.90 MB chip and no fast RAM free, AGA yes, graphics 40.24, intuition 40.85, dos 40.3, workbench 40.5; 32 programs gave exactly the emulator counts this pack lists (574 `ok` lines in all, no `FAIL`, no `skip`), hello, mainargs and trace printed text only (as designed), nothing was flagged; the emulated clock says 173 seconds from start to finish of `RunAll` (09:13:37 to 09:16:30), 225 seconds wall time with boot and the `RESUME` pass. Per program: phase2 32, memory 19, generics 24, properties 18, operators 20, language 39, collections 20, inherit 24, lambdas 16, linq 37, refs 18, patterns 23, objects 21 (21 again with `SEED=42 auto`), names 12, dates 19, interrupts 14, timing 10, fonts 8, iff 10, controls 12, keys 8, asl 3, screen 23, aga 6, assets 17, rtg 5, sound 21, dbuffer 30, textspeed 1, cards 15, rummy 28. The `fast8` profile (8 MB fast RAM, 8.26 MB free) gave the same counts and a clean `SUMMARY.txt`, 159 seconds. Checked by hand in the emulator: a program that returns code 7 is noted and the run continues, a program whose `.sum` exists is skipped under the resume flag, arguments with `=` and quotes arrive intact only when passed as one quoted string to `RunOne` (a bare `SEED=42` argument to `Execute` is split in two by AmigaDOS, which the first version of the script did), a FAIL line and a missing result file are reported by the counting helper. Two emulator-only details: the bare emulated hard disk has no `C:` (hence the Workbench disk), and `fonts` / `asl` raise an "insert volume" requester unless `ENV:` and `FONTS:` are assigned to RAM: (the script does that). One full run in the same session stopped at `generics` (`PROGRESS.txt` named it, no result file) and had to be killed by the script timeout; the same pack, script and options ran through every time after that. The cause was not found, so a hang of that kind is possible and not explained (a sporadic emulator stall is the guess, nothing more).

Not verified: any real hardware (this is the pack for it, issue #7), a different CPU or an accelerator, the pictures and sound by eye (the emulator runs show that the programs finish and their pixel checks pass; the README asks the tester to look), `Protect` being needed after a copy from a PC (written from knowledge of AmigaDOS), `RunAll RESUME` after a real guru (the skip was tried, the crash itself was not), `gui` and the asl requester and a key press (by-eye steps, not run in this pack: they were run earlier by scripted input, see their sections), and the `fast8` profile unless listed above.

### Floppy image for machines without a hard disk (issue #7)

`tools/hwpack/mkadf.py` (python3 standard library only, written for E# from the documented AmigaDOS layout; tests in `tools/hwpack/test_mkadf.py`, 8 pass: FFS and OFS write and read back, nested directories, a 100 KB file with extension blocks, protection bits (decoded against the `dos/dos.h` values: archive 4, pure 5, script 6, hold 7), the bitmap check, hash chains, disk full) writes and reads 880 KB DD images. `build.sh` now also writes `obj/hwtest.adf` (FFS, volume HWTEST, our files only, not bootable): 660 KB used, 220 KB free for results. `build.sh <image> --boot-wb <Workbench disk 2 .adf>` also writes `obj/hwtest-boot.adf` (own use only, never shared; `build.sh` refuses to write it anywhere git does not ignore): the pack plus C:Execute, Date, Delete, Type, Assign, MakeDir, SetPatch and asl, diskfont, iffparse and lowlevel libraries read from the user's own disk, the boot block (taken from the user's own Workbench disk: the repo holds no boot code), C:SetPatch and our own startup-sequence (`tools/hwpack/boot-startup`); 771 KB used, 108 KB free. `run-emulated.sh --floppy obj/hwtest.adf` (DF0 a throwaway copy of the Workbench disk with an added S:User-Startup, DF1 a copy of the pack disk) and `--boot-floppy obj/hwtest-boot.adf` (DF0 alone) run a stock A1200 with Kickstart 3.1, 2 MB chip RAM, no fast RAM and no hard disk, drive speed turbo (`--floppy-speed 100` is real speed), then read the results back from the image.

Run on 2026-11-16 (FS-UAE 3.2.35, accuracy 1, the 3.1 A1200 ROM and Workbench 3.1 disk 2; after the review fixes), all three modes: boot disk alone (`--boot-floppy`, 274 s), Workbench copy in DF0 plus pack disk in DF1 (`--floppy`, 334 s) and the hard-disk mode (207 s): in each, 35 runs, every program printed the emulator's ok count (dates 19) and no FAIL, image checks clean (0 problems, bitmap compared with the referenced blocks). `dates` prints `info: ToUpper(0xF7) = 0xD7` with the Workbench disk booted (locale rule) and `0xF7` in the other two modes. `List DF1:` on the Amiga shows `-s--rwed` for RunAll and RunOne (script bit, not archive). The boot-disk run uses SetPatch from the user's disk. An earlier attempt hung in `fonts` on an "insert volume FONTS:" requester (the Workbench disk has no Fonts drawer); the floppy way now assigns `FONTS:` to `RAM:Fonts` first (README says so).

Not verified: the one-drive Workbench-disk way in the README (Resident commands, libraries copied to RAM:Libs; only the two-drive way and the bootable disk were run), a real A1200 with a floppy drive, writing the image to a disk, a Gotek, the THEA500 Mini (the exact menu and file-name convention are not checked), a real-speed floppy run in FS-UAE (turbo only), a floppy running out of space (it did not here).

### Kickstart 3.0 (issue #7)

An original A1200 often has Kickstart 3.0 (39.106) and Workbench 3.0. Run on 2026-11-16 in FS-UAE (stock A1200, accuracy 1) with the A1200 Kickstart 3.0 ROM and Workbench 3.0 (39.29) disk 2, from the hard disk and from the bootable floppy (`--boot-floppy`): every program gives the same ok count as on 3.1, no FAIL, except `controls`, which gives 9 ok instead of 12 because Workbench 3.0 has no `lowlevel.library` (it came with 3.1), so it takes its no-lowlevel path (as with no `LIBS:` on 3.1). Two fixes came from this run: `Keys.Open` asked for keymap.library v39, but the ROM copy on Kickstart 3.0 is older, so `keys` gave 6 FAIL; it now asks for v37 (every call it makes is v36), and `keys` gives 8 ok on 3.0 and 3.1. `tools/hwpack/build.sh --boot-wb` stopped because the Workbench 3.0 disk has no `Libs/lowlevel.library`; a library missing from the user's disk is now left out with a note. Library versions on 3.0: graphics, intuition, utility and gadtools are v39 in the 3.0 ROM, asl, diskfont and iffparse v39 on the Workbench 3.0 disk (the run used those copies). Not verified on 3.0: real hardware, the Workbench-disk two-drive floppy mode, gadtools (`gui` is in the pack but RunAll does not run it), icon.library (opened only on a Workbench start; the pack runs from the Shell; if it fails the tool types are skipped).

## Lambdas that capture, and LINQ over arrays (samples/lambdas, samples/linq)

**Delegates carry an environment.** A delegate (`Func<>`, `Action<>`, `Predicate<>`, a user `delegate` type) is now a C struct of a function pointer and an environment pointer. A static method or a lambda that captures nothing has a null environment. A lambda that uses a local, a parameter or `this` gets an environment struct of pointers to those variables, built on the stack of the method that made it, so it shares the variables with the method (a write inside the lambda is seen outside, and the other way round), as in C#. Lambdas inside lambdas capture through both levels. `d == null`, `d != null` and `null` work on delegates, and `Pick()(x)` (calling a returned delegate) works.

- `long`, `ulong` and enums in interpolated strings: checked by a unit test against host gcc output (an enum member prints its name, an unknown value its number, `D` the number) and by a scratch program on Kickstart 2.04 (A500+) and 3.1 (A1200) (not kept as a sample). 135 unit tests pass.
- **Limit (until issue #41; see "Heap closures and events" below):** a capturing lambda that is only passed as an argument (also down several calls) or is the initializer of a local that is only called and passed on still lives on the stack of the method that created it, as described here. One that is stored, returned or added to an event is now a heap closure. A capturing lambda assigned to a local declared earlier is still `ES0099` unless that local is itself stored or freed. A lambda that captures an array is `ES0099` (pass it as a `Span<T>`). Local functions still may not capture.
- **LINQ** is turned into loops, for arrays and `Enumerable.Range` only. Steps: `Where`, `Select` (also the `(x, i)` index forms), `Take`, `Skip`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` (also `orderby` in a query). Terminals: `Count`, `Sum`, `Min`, `Max`, `Any`, `All`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Contains`, `Aggregate` (with and without a seed). A chain, or a query with one `from`, `where`, `let` and `select`, is one loop with no sequence in memory, so it must be consumed on the spot: by a terminal call, or by `foreach`. The lambdas are pasted into the loop, so they use the surrounding locals directly (no closure) and must be expression lambdas, static method groups or delegate variables. **A sort** copies the elements and their keys into stack arrays as large as the source, sorts them with a stable insertion sort (O(n²): fine for hands of cards, slow for thousands) and goes on from the sorted copy, so keep the source small: the stack on a 68000 is only a few KB by default. Keys must be numbers, `char`, `bool` or enums (not strings or structs); a `let` before an `orderby` in a query is `ES0099` (put it after). `ToArray` and `ToList` cannot exist: arrays have a fixed compile-time size and there is no growable list. Everything else (`ToArray`, `ToList`, `Distinct`, `GroupBy`, `Join`, `SelectMany`, `Zip`, `Reverse`, `Average`, joins and ordering in queries, `from` twice, `group`, `into`, a chain stored in a variable, LINQ over a `Span` or `Vec`) is `ES0002` (the analyzer) or `ES0099` (the emitter). `Min`, `Max`, `First` and `Last` on an empty source give 0 or `default` instead of throwing (E# has no exceptions). `Take(n)` stops before it pulls the next element.
- Sorting added afterwards: `samples/linq` is now 37 checks (8 for `OrderBy`/`ThenBy`/`orderby`: two keys, stability, structs, `Range`, with `First`/`Skip`/`Take` after) and gives the same output on dotnet, host gcc and Kickstart 2.04 (A500+) and 3.1 (A1200); 136 unit tests pass. Not verified: PowerPC, speed of the insertion sort on a real 68000, a sort of a large source (stack use).
- Verified (before sorting): `samples/lambdas` (16 checks: capture of one and two locals, writes through a capture, a captured local changed after the lambda was made, a user delegate type, method groups, a lambda in a lambda, closures made inside a loop, `null` delegates, `this`) and `samples/linq` (29 checks: every step and terminal above, index forms, structs, `Enumerable.Range`, `foreach` over a chain, queries with `let`, a chain inside a lambda) match dotnet and host gcc, and run on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE. 134 unit tests pass. Full regression with both samples added: 17 samples on both Kickstarts, 579 checks ok, no failures.
- Not verified: PowerPC; speed on a real 68000 (a delegate call is two loads and an indirect call; a fused LINQ loop is the loop you would write by hand); the generated C compiled with `--debug`; a delegate as a struct field or a static field initializer (the typedefs may come in the wrong order); a block lambda with more than one statement inside a LINQ chain (`ES0099`: use an expression lambda or a loop).

## Heap closures and events (samples/closures, issue #41)

Capturing lambdas can now be stored and outlive the method that made them, and classes can declare field-like events. There is still no garbage collector: a stored closure has an owner who frees it, as with objects.

- **Two kinds of closure, chosen by the compiler from where the lambda goes.** A *borrowed* closure (the lambda is an argument, or the initializer of a local that is only called and passed on as an argument) is unchanged: an environment of pointers on the creating method's stack, no allocation. A *stored* closure is a lambda that is assigned to a field, a static, an auto-property, a property setter or an array element, added to an event with `+=`, returned (also from an expression-bodied member or as a lambda's result), passed to a parameter marked `[Owned]`, or held in a local that is one of these, is captured by a stored closure, or is freed with `Heap.Free`. The compiler follows a delegate through parentheses, casts, `!`, `?:`, `??`, `??=` and switch expressions. A delegate local used anywhere else that it does not recognise is made a heap closure (safe, at worst never freed), never left on the stack. Only lambdas that capture something are closures; a lambda without captures and a static method group have a null environment and nothing to free.
- **Where the captured variables live.** Each C# scope that declares a variable captured by a stored closure (a method's parameters and top-level locals, or a nested block such as a loop body) gets one heap *frame* (calloc), allocated when the scope is entered, so each loop iteration gets its own variable, as in C#. The method, borrowed closures and stored closures all use the variable in the frame, so writes are shared both ways. A frame holds a reference to the frame of the enclosing scope (and to `this`, kept in a method's outermost frame), so a closure reaches outer variables and closures inside stored closures work. The delegate value stays `{ fn, env }`; `env` is the innermost frame that holds something the lambda captures.
- **Ownership and freeing.** Frames are reference counted. The scope that made a frame holds one reference and drops it when the scope ends (gcc's `cleanup` attribute, so `return`, `break` and `continue` are covered); each stored closure value holds one more. `Heap.Free(ref d)` on a delegate drops its reference and sets it to null (a delegate without a heap environment: just null). Copies of a delegate are aliases, like object references: free once, through one owner. A frame is freed when its last reference goes. `Heap.LiveClosures()` returns the number of closure references not yet freed (0 on the dotnet host, where the GC owns closures), so a program can check for leaks; `samples/closures` ends with that check.
- **`[Owned]` (new attribute in `ESharp`).** Delegate parameters are borrowed by default: the caller's closure may be on its stack, so the callee may call it and pass it on as an argument, but storing it, adding it to an event, returning it, freeing it, putting it in a tuple, returning it from a lambda (also an expression-bodied one, such as `q => q`) or capturing it in a stored lambda is error **ES0012** (analyzer), also when it is first copied into a local (the analyzer follows locals; the emitter has a backstop that gives `ES0099`). Mark the parameter `[Owned] Action a` to keep it; a capturing lambda passed there is then a stored closure owned by the callee. A property setter's `value` counts as owned (the call site is an assignment). **An `[Owned]` parameter or a property setter takes a reference to the value it is given** (issue #110), whatever the callee does with it (stores it, or, since #85, only captures it in a stored lambda whose frame releases it). The caller's reference is handed over, with no new one, only when the caller gives it up: a new lambda, a call that always returns new closures, `null`, a method group, or a local (also an `[Owned]` parameter or a setter's `value` passed on) that owns its closure (only ever given new lambdas, `null` or such calls) and for which this is the last use: nothing reads it after the call (also not later in the same call), the call is not in a loop that does not declare the local, not inside a nested lambda, and the local is not freed, stored, returned, copied, captured by a stored closure or given away anywhere else on a path that reaches the call (a use on another branch of the same `if`/`else`, `?:`, switch statement or switch expression does not count, so `if (x) Heap.Free(ref c); else Keep(c);` hands `c` over; a lambda on the stack, passed as an argument in an earlier statement, may call it). Every other value gets a reference of its own at the call (`es_closure_retain`, as `E += x` does): a field, a static, a property, an array element, a call that may return a kept delegate (with the same `ES0013` warning as for events), and a local that is used again, freed, stored, captured or given away more than once. So every owner frees its own reference: a caller may `Heap.Free` a field or a local after passing it, and a frame that holds a captured local also releases the local's own. When such a local owns a closure that nothing else frees on the call's path (it is only given away more than once, used after the call, given away in a loop, or freed or stored only on another branch; a capture by a lambda on the stack is only a use), the emitter warns with **ES0013** and asks for `Heap.Free(ref x)` after its last use. Not covered: the warning is a check of the source, not of every path, so a `Heap.Free`, store or return that runs on the call's path only under a condition of its own (`Keep(c); c(); if (y) Heap.Free(ref c);`) silences it, and when `y` is false that closure leaks with no ES0013 (`Heap.LiveClosures()` and LeakSanitizer show it). Every case left to the program is a leak, never a double free. Setters means set accessors with a body (also virtual and abstract properties); an auto-property is a field store as before (the field takes the value as it is, like `F = G` between fields: the program frees one of them). Before #110 the caller's reference was always handed over, so passing a field to a setter that only captures `value` and then freeing the field was a use after free since #85, and a storing callee fed a value the caller then freed was one already before.
- **Events.** `public event Action Clicked;` (also `Action<T...>` and user delegate types returning void, static events, events of a base class used through a derived class) is a list of handlers in the object. `+=` of a new value hands its reference to the event: a lambda, or a call to a method of the program (not virtual or an interface member) whose every return is a new lambda, `null` or such a call, or `?:`/`??`/switch of those. Any other call (one that may return a delegate it keeps) is treated as shared, so the event takes its own reference; that is never a use after free, but if the call did make a new closure its reference leaks, and ES0013 warns there. `+=` of a variable, field, property or element makes the event take its own reference, so that owner still frees it. Mixing the two kinds in one `?:` is `ES0099` (split it). `-=` removes the last matching handler (same function and same environment, as C#'s delegate equality) and drops the event's reference. `Clicked?.Invoke(...)`, `Clicked(...)` and `Clicked.Invoke(...)` call every handler in order; arguments are evaluated once. A raise works on a snapshot, as in C#: handlers added during a raise are not called by it, handlers removed during a raise still are, and the removed entries are released once no raise is running (so a handler may remove itself). `Clicked == null` is true when there are no handlers. `Heap.Free(ref Clicked)` inside the class drops every handler; an object that owns events should do that (in its own `Dispose`) before `Heap.Free` of the object, which does not look inside.
- **ES0014 (error).** E#'s own generics (user generic structs and methods, `Vec<T>`, `Pool<T>`, ...) instantiated with a delegate type, such as `Vec<Action>` or `Put<T>(ref T slot, T x)` called with a lambda: the monomorphised copy would store or pass on closures with no tracked owner. Spans and tuples of delegates are `ES0099`. Use an array of delegates or a class with delegate fields.
- **Frames own the delegates they hold (issue #85).** When a stored closure captures a delegate variable, the frame that holds that variable releases the variable's closure when the frame itself is freed, if the variable owns it: an `[Owned]` parameter, a setter's `value`, or a local that is only ever given new lambdas, `null` or calls that always return new closures, and that is otherwise only called, compared, passed to borrowed parameters, removed from or added to an event (the event takes its own reference) or `Heap.Free`d (which nulls it, so the frame then releases nothing). A variable whose value is copied anywhere else (into a field, another local, a return, or handed over to an `[Owned]` parameter or a setter without a reference of its own, see `[Owned]` above) or that starts as an alias (a local copied from a field) is not owned and is never released by the frame: its other owner frees it, as before. A local given a lambda over the variables of its own scope (or over `this` in the same method) would keep its own frame alive; such a value takes no reference to the frame at all and the frame releases nothing for it, so the two are freed together. Not covered (leaks as before, shown by `Heap.LiveClosures()`): an owned delegate that is overwritten (the old closure is not released; free it first), a local given a lambda over a scope nested inside its own (a cycle through the inner frame) unless the program frees it, and an `[Owned]` parameter that is neither stored nor captured (nothing frees it at the end of the method).
- **ES0015 (warning).** `E -= <lambda>` never removes anything: each lambda is a function of its own, so it equals no handler that was added (in C# too). Keep the handler in a variable or field, add that and remove the same variable. A capturing lambda there is also `ES0099`.
- **ES0013 (warning).** The analyzer warns when a field, auto-property, static or event is given a capturing lambda but no `Heap.Free(ref X)` exists anywhere in the program, and when a local holding a capturing lambda is added to an event (the event takes its own reference) and the local is never freed, stored or returned. The emitter also gives it for a local that owns a closure and is given to an `[Owned]` parameter or a setter that takes its own reference (see `[Owned]` above) while nothing frees the local's own.
- **Still `ES0099`:** a stored lambda that captures a `for`/`foreach` variable, a pattern or `out` variable, a `using` or switch-section local (copy it into a local declared in a block), an array, or a borrowed delegate parameter; a stored lambda inside a borrowed lambda that captures the outer method's variables or `this`; a borrowed capturing lambda assigned to a local declared earlier that is not itself stored; events with `add`/`remove` accessors, assigned with `=`, whose delegate returns a value or has `ref`/`out` parameters, events in structs and interfaces; `?.` other than `x?.Invoke(...)`, and `?.Invoke` on a delegate that returns a value. Instance-method groups (`b.Clicked += OnClick;`) are still `ES0099` (wrap them in a lambda).
- **Behaviour that differs from C#.** Raising an event with no handlers with `Clicked()` does nothing (C# throws a NullReferenceException; use `?.Invoke` for code that runs under both). A lambda that captures only `this` gets a frame per call of its method, so `-=` with a new lambda that captures only `this` matches only within the same method call (in C# it matches by object). `Heap.Free(ref obj)` on an object whose events still have handlers leaks those handlers (free the events first, in the object's own `Dispose`), and a handler that frees the object owning the event while that event is being raised is undefined (the raise still touches the event afterwards). Leaks and use after `Heap.Free` are the programmer's to avoid, as with objects.
- **Cost.** A stored closure is one calloc per entered scope that holds captured variables (not per lambda) and a reference count. The frame is allocated every time the scope is entered, even on paths that never make the closure (a getter that returns a closure only sometimes still pays one calloc and free per call); an event is a growable array (calloc, 4 handlers at first, doubling). A borrowed closure costs what it did before (nothing on the heap).
- Lambdas in instance field and auto-property initializers (`public Action F = () => { };`) now compile (their functions were not emitted before). C# does not let such a lambda capture anything, so it has no closure to free.
- The class structs of the generated C now come after the span, tuple and delegate typedefs, so a class can hold delegate fields and events (before, a delegate field in a class did not compile in gcc; not seen in any sample).
- **Verified (2026-11-16):** `dotnet test`: 467 passed, 2 skipped (the OS4 VM and SDK-backed BindGen tests), 0 failed; 28 of them are new in `HeapClosureTests`: `samples/closures` under dotnet and as host-gcc C (same 20 ok lines), the same C under AddressSanitizer, LeakSanitizer and UBSan (no use after free, no leak at exit), host-gcc runs of returned and static closures, `return`/`break`/`continue` out of scopes with frames, nested stored lambdas, events (order, arguments evaluated once, the same handler twice, base-class events), constructors, setters, auto-properties and `this`, `?.Invoke`, a deliberate leak caught by both `Heap.LiveClosures()` and LeakSanitizer, 8 `ES0099` forms, 6 `ES0012` forms and 3 allowed uses, and `ES0013` both ways. `esharp test samples/closures`: host 20 ok; `--target m68k` on A1200 / Kickstart 3.1 (40.069) in FS-UAE 20 ok, no FAIL, no gcc warnings; `--target sdl --docker esharp-sdl` 20 ok; `--target os4` in the AmigaOS 4.1 QEMU VM 20 ok; host and target agree in all three. Regression on m68k (A1200 / Kickstart 3.1): lambdas 16, linq 37, language 39, collections 20, objects 21 ok, no FAIL, host and Amiga agree; every sample builds for m68k (`esharp build --target m68k`). `samples/closures` is in the hardware test pack (`RunAll`, 20 ok expected), but the pack itself was not re-run. **After the review fixes (PR #83):** `dotnet test` 484 passed, 2 skipped, 0 failed (45 in `HeapClosureTests`, now including each review probe: delegate locals kept through `??`, `??=` and a switch expression run clean under ASan/LSan; `+=` of a call result, a conditional of calls or lambdas, and a switch expression leave `Heap.LiveClosures()` at 0; a borrowed parameter copied into a local, a lambda passed as a generic `T`, `Vec<Action>` and tuples holding delegates do not compile (ES0012, ES0014, ES0099)). `samples/closures` on m68k (A1200 / Kickstart 3.1) 20 ok and on sdl 20 ok, host agrees; lambdas 16 and linq 37 ok on m68k; every sample still builds for m68k. The os4 run was not repeated (the maintainer's VM was running). **Second review round:** `dotnet test` 490 passed, 2 skipped, 0 failed (51 in `HeapClosureTests`: a call returning a kept delegate added to an event runs clean under ASan/LSan and gets ES0013, a call that always returns new lambdas hands its reference over with no warning, borrowed delegates returned from expression-bodied lambdas are ES0012, lambdas in instance field and property initializers run). `samples/closures` 20 ok on m68k (A1200 / Kickstart 3.1), sdl and os4 (AmigaOS 4.1 QEMU VM), host agrees on each; every sample builds for m68k with no ES0012/ES0013/ES0014/ES0099. Not verified: hosted AROS and MorphOS, real hardware, speed of a raise or of a frame allocation on a 68020, and programs that keep many closures alive for a long time (fragmentation of the C heap). **Frames owning their delegates (issue #85, 2026-11-19):** `dotnet test` 605 passed, 2 skipped, 0 failed, with 6 new `HeapClosureTests` run as host-gcc C under AddressSanitizer, LeakSanitizer and UBSan: an `[Owned]` parameter captured by a returned and by a stored block lambda (`Heap.LiveClosures()` back to the start, no leak), a setter's `value` captured by a stored lambda, three aliases (a local copied from a field, an owned parameter also stored in a field, a local copied into a field: each freed once, by its other owner, no double free), nested frames (a loop body's local over the method's `[Owned]` parameter, a local over its own scope, a local given a fresh call result), the generated fini, and ES0015 both ways. With the release switched off the four run tests fail (leaks); with every captured delegate released the alias and nested tests fail (double free, cycle). `samples/closures` has two new checks (an `[Owned]` parameter and a setter's value captured by stored lambdas, covered by the final leak check): `esharp test samples/closures` 22 ok on the host, on m68k (A1200 / Kickstart 3.1 (40.069) in FS-UAE, no gcc warnings) and on os4 (AmigaOS 4.1 QEMU VM), host and target agree; `samples/lambdas` 16 ok on m68k, host agrees. **Review of #109:** the hand-over contract for `[Owned]` parameters and setters is documented above, and a new host-gcc test (AddressSanitizer, LeakSanitizer, UBSan) passes a delegate local to an `[Owned]` parameter and a field (then cleared) to a setter that only capture them: each closure is released once, `Heap.LiveClosures()` returns to its start; `dotnet test` 606 passed, 2 skipped, 0 failed (58 in `HeapClosureTests`). Not verified for this change: sdl and hosted AROS runs of the sample, the hardware test pack (now expects closures 22). **Shared values given to `[Owned]` parameters and setters (issue #110, 2026-11-19):** `dotnet test` 625 passed, 2 skipped, 0 failed (65 in `HeapClosureTests`, 7 of them new or rewritten, run as host-gcc C under AddressSanitizer, LeakSanitizer and UBSan): both repros of the issue (a field given to a setter that only captures `value`, then freed; a local given to `Twice([Owned] f)` and also freed) now run with no use after free and no leak; a field given to a storing setter, to an `[Owned]` constructor parameter and to an object-initializer setter, and an array element given to the constructor, all freed by the caller; fresh lambdas, a call that returns a new lambda, a plain local, an `[Owned]` parameter and a setter's `value` passed on, all handed over with no `es_closure_retain` in the C and no leak; a local given twice, given in a loop and called after the call, each freed once by the caller; a captured local also given away, released by its frame; ES0013 for a local given twice and never freed, none when it is freed or given once; `Heap.LiveClosures()` back to its start in each. With the retain switched off the six run tests fail (use after free); with a retain on every value the fresh-value test fails (leak). `samples/closures` has one new check (a local given to a setter, used and freed by the caller): `esharp test samples/closures` 23 ok on the host, on m68k (A1200 / Kickstart 3.1 (40.069) in FS-UAE, no gcc warnings) and on os4 (AmigaOS 4.1 QEMU VM), host and target agree; `samples/lambdas` 16 ok on m68k, host agrees; every sample emits C with no new diagnostics, and no platform code (MUI, BOOPSI hooks) gets a retain. Not verified for this change: sdl and hosted AROS runs of the sample (sdl runs in CI), the hardware test pack (now expects closures 23). **Review of #112:** a local captured only by lambdas on the stack and then given to an `[Owned]` parameter is handed over when the borrowing call ends before (before, it was retained and leaked with no warning), and a `Heap.Free`, store or second hand-over on another branch of the same `if`/`?:`/switch no longer forces a retain; ES0013 now also covers a borrowed capture and a free that is only on another branch. `dotnet test` 628 passed, 2 skipped, 0 failed; 3 new `HeapClosureTests` (host-gcc C under AddressSanitizer, LeakSanitizer and UBSan: the borrowed capture then `Keep(a)`, a stack local still calling a value given to a callee that frees it, `Heap.Free` on one branch and a hand-over on the other in an `if` and a switch, a setter storing `value` on one branch and passing it on on the other, `Heap.LiveClosures()` back to its start; ES0013 for the two new cases, none for a free on both paths) fail without the change. The emitted C of every sample is byte-for-byte the same as before the change, so the m68k and os4 runs above stand; they were not repeated.

## Inheritance, virtual members, `is` / `as` (samples/inherit)

A class can derive from one other plain class of the program: fields, methods and properties are inherited, `abstract` classes and members, `virtual` and `override` methods and properties (get/set bodies required; an automatic property cannot be virtual), `sealed override`, `base.Method()` and `base.Property`, constructors with `: base(...)` and `: this(...)` (a constructor without an initializer runs the nearest explicit parameterless base constructor first), and a derived class used where its base class is expected (assignment, arguments, returns). `x is Derived`, `x is Derived d`, `x is not Derived`, `x is Derived { Field: ... }`, `x as Derived` and `(Derived)x` work between classes of a hierarchy. A class that implements an interface passes that to its derived classes; an interface call reaches the most derived override. How it is built in C: a derived class's struct starts with its base class's struct (`es_base`), so a pointer to the derived object is a pointer to the base object. A hierarchy with virtual members or subclasses has one const table of function pointers per class (`X_cvt`, whose first member is the base's table) and the root object starts with a pointer to its class's table; a virtual call is one load and one indirect call. `is`/`as` compare the class's id (its table's address) along the base chain, so they cost a short loop. Classes outside any hierarchy compile to exactly what they did before. Class constructors are now two functions (`X_new` allocates, `X_new_init` runs the body on an existing object), which also fixes a bare `return;` in a class constructor.

- Verified: `samples/inherit` (24 checks: inherited fields, constructor order through `this(...)` and `base(...)`, two levels, virtual calls through the base type, a base method calling virtual ones, virtual property, base calls, `is`/`as`/pattern forms, downcast, interfaces on derived classes including an interface call that must reach an override, a derived class without a constructor over a base with one, a class outside any hierarchy, `Heap.Free` through a base variable) matches dotnet and host gcc, and runs on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, also with `--debug`. 123 unit tests pass. Full regression after the change (constructors of every class were reshaped): 15 samples on both Kickstarts, 497 checks ok, no failures.
- Not verified: PowerPC, speed of virtual calls and type tests on a real 68000, inheritance combined with generics (a generic base class is rejected with ES0007), classes from the library as base classes (rejected unless marked `[Extensible]`; see "Classes derived from platform classes"), `is`/`as` to an interface or a struct type (ES0099; from an interface value to a class works, see the game port section), a cast `(Derived)x` that is wrong is not checked (there are no exceptions: it is a plain pointer cast, as `as` is the safe form), `new` hiding of virtual members.

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

`Font` (`src/ESharp.Platform/Font.cs`) is a built-in 5x7 font for printable ASCII (`Draw`, `DrawChar`, `DrawInt`, `Width`, integer `scale`), drawn in one colour on a transparent background (on the Amiga now one `BltTemplate` per line, see the game port section; first it was filled rectangles), so it needs no assets and works at any screen depth. Strings now support `s.Length` (bytes) and `s[i]` (a byte). Verified by 5 pixel-exact checks in `samples/screen` on the host and on Kickstart 2.04 (A500+) and 3.1 (A1200). The previously unrun samples (hello, phase2, memory, generics, properties, operators, language, screen, cards with scripted input) were all re-run on both Kickstarts after the language batch: 296 checks ok, no failures. Not verified: speed of long texts on a 68000 (one `RectFill` per pixel run), non-ASCII text.

## Phase 1: Hello world (emulator gate met; real hardware not yet run)

> Historical record: the runs below on Kickstart 2.04 / A500+ were done before the minimum system became the A1200 (see "Minimum system" at the top). They were true when run and are kept as dated evidence; the current test matrix is Kickstart 3.1 (40.069) on `--model A1200`.


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
| `esharp run` launches FS-UAE with the output mounted as DH0:; `--headless` captures output | Done: hello prints `Hello from E#!` on an emulated A500 (historical; FS-UAE 3.1.66, built-in AROS ROM replacement) |
| Same run on the Linux dev host (Ubuntu, dotnet 10.0.402, FS-UAE 3.2.35, AROS ROM replacement) | Done: prints `Hello from E#!`, exit 0 |
| Run in FS-UAE with a real Kickstart | Done on the Linux dev host (FS-UAE 3.2.35, headless): Kickstart 2.04 (37.175) on A500+ (historical, no longer in the matrix) and Kickstart 3.1 (40.069) on A1200 both print `Hello from E#!`, exit 0, 5-8 s. Kickstart 1.x was out of scope (historical: the floor was then 2.04; it is now 3.1 on an A1200, see "Minimum system") |
| Run on real hardware | **Not done** |
| Run the PowerPC builds (os4, morphos) | os4: **done in a QEMU pegasos2 VM** (AmigaOS 4.1 FE Update 3; `esharp run/test --target os4`, see "AmigaOS 4.1 VM runs"). MorphOS: **not done**. PowerPC real hardware: **not verified** |

## Phase 2: data types (done on host and both Kickstarts; PPC not run)

> Historical record: the runs below on Kickstart 2.04 / A500+ were done before the minimum system became the A1200 (see "Minimum system" at the top). They were true when run and are kept as dated evidence; the current test matrix is Kickstart 3.1 (40.069) on `--model A1200`.


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

> Historical record: the runs below on Kickstart 2.04 / A500+ were done before the minimum system became the A1200 (see "Minimum system" at the top). They were true when run and are kept as dated evidence; the current test matrix is Kickstart 3.1 (40.069) on `--model A1200`.


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
| Patterns: constant, relational, `and`/`or`/`not`, `var`, declaration, property (fields and properties, `A.B:`), positional, list, `when`; `is`, `switch` expressions and `switch` statements | Done (GNU statement expressions in the C; gcc only, which is all we target). No match in a switch expression traps instead of throwing. Positional and list patterns and pattern `case` labels since issue #12 (see "Positional and list patterns"). Type tests are to classes only, from a class or an interface value (interface or struct targets are `ES0099`) |
| Local functions (no capture) | Done: a local function that uses a local, parameter or `this` of the method around it is `ES0099` |
| Static constructors, `: this(...)` constructor initializers (structs) | Done. **Static constructors run once at start-up in type order**, not lazily like in C#. Before this fix a static constructor was silently ignored |
| `Vec<T>` (`ESharp.Platform`, named so as not to clash with `System.Collections.Generic.List<T>`): growable list of values or class references | Done: `Add`, `Insert`, `RemoveAt`, `SwapRemoveAt`, `IndexOf`, `Remove`, `Clear`, indexer, `Count`, `Capacity`, `AsSpan`, `Destroy`; no exceptions (false / zero value). Counted in the chip budget by its initial capacity only |
| `Dos.PutInt` | Done |
| String interpolation and concatenation (`$"score {n,5:D3}"`, `"a" + n`) | Done: the text is formatted into one of 8 static 128-byte buffers and returned as a `string`, so it is **valid only until 8 more strings have been formatted** (use it at once: draw it, print it; copy it if it must stay) and is cut at 127 bytes. Holes: integers including `long`/`ulong` (the 64-bit ones use software division on a 68000, so they are slow; alignment `,N`/`,-N`, formats `D<n>`, `X<n>`, `x<n>`), `char`, `bool` (`True`/`False` like C#), `string`, enums (the member's name, or the number for a value that is none of them, like `ToString`; `D`/`X` give the number); `{{` and `}}` work; constant strings are folded. Not done (ES0099): floats and fixed-point, `[Flags]` combinations (printed as a number, not `A, B`), other format strings, `string.Format`, `StringBuilder`. Parity with dotnet is checked in `samples/language` |
| Heap classes (non-static `class`) | Done: fields, auto-properties, methods, constructors, static members, `new` (also with an object initializer), `null`, reference equality, arrays of references, `this`. A class variable is a C pointer (`Foo *`), `new` is `calloc` (stops with exit 20 when memory runs out), and objects are freed with `Heap.Free(ref obj)` (frees and sets the variable to null); objects never freed are released when the program ends. There is no garbage collector: keep one owner per object. **Single inheritance with `virtual`/`abstract`/`override` (see the inheritance section above), no generic classes (ES0008); instance field initializers, fixed-size array fields and nested classes work (see the game port section)**. Every E# type gets a prefixed, namespaced C name (`Es_<namespace>_<type>`, see the C names section), so a class may be called `Node`, `List` or `Window` like an NDK struct, and two types with the same name in different namespaces are apart |
| Interfaces | Plain-method interfaces (no properties, events, generic methods or default implementations). Two ways to use them: as generic constraints (`where T : IScorer`, monomorphised, any struct or class, static dispatch), and as **variable types** (`IRules rules = new FlatRules();`, fields, parameters, returns, statics): an interface value is a C struct of the object pointer and a table of functions, so the call is dynamic. Only **classes** can be converted to an interface value (a struct would need boxing: ES0099). `x == null`, `x is null`, `default`, equality (same object) and `Heap.Free(ref x)` (frees the object) work. `ref`/`out`/`in` parameters, and `is`/`as`/type patterns back to a class work. Not done: interface-to-interface conversion and tests, inheritance between interfaces |
| Lambdas, method groups and delegates (`Func<>`, `Action<>`, `Predicate<>`, user `delegate` types) | Done: a delegate is a function pointer plus an environment; lambdas may capture locals, parameters and `this` (by reference: on the stack when only borrowed, in a heap closure when stored, returned or added to an event; see "Heap closures and events"). Calls `f(x)`, `f.Invoke(x)` and `f?.Invoke(x)` (void delegates), `null` and `== null` work; field-like events (`event Action E;`, `+=`, `-=`, raise) on classes. Not done: delegates as struct fields, `delegate { }` anonymous methods, instance-method groups, combining plain delegates (`+=` outside events), local functions that capture |
| LINQ | Over arrays and `Enumerable.Range`, as fused loops: Where, Select, Take, Skip, Count, Sum, Min, Max, Any, All, First, Last, Contains, Aggregate, OrderBy/ThenBy (stack copy, stable insertion sort) and simple queries (see the top of this file). No `ToArray`/`ToList`, no sequences stored in variables |
| Still missing (needed by bigger games) | `OrderBy`/`ToArray`/`ToList`, float values in strings, `is`/`as` to interface types, inheritance from generic or library classes (except the `[Extensible]` platform classes: `GtWindow`) |

## Phase 4: card demo (done on the emulator)

> Historical record: the runs below on Kickstart 2.04 / A500+ were done before the minimum system became the A1200 (see "Minimum system" at the top). They were true when run and are kept as dated evidence; the current test matrix is Kickstart 3.1 (40.069) on `--model A1200`.


`samples/cards` is a playable higher-or-lower game on the Amiga screen. Together with `samples/rummy` it meets the phase 4 gate (a card game playable on an Amiga) on the emulator; real hardware is not verified.
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

Open work as of 2026-11-14 (the GitHub issues are the live list; the v1 tracker is #6 and the v2 epic is #5):

- **Real hardware (#7):** nothing has run on a real Amiga yet. The hardware test pack (`tools/hwpack`) is ready; reports from an original A1200, an accelerated A1200 or an FPGA machine are the most valuable thing anyone can add. PowerPC real hardware and MorphOS runs are also not done (MorphOS only builds; QEMU `pegasos2` could boot it, not tried).
- **Debugging (#14):** the host build can be stepped through under .NET and the Amiga build has serial `Trace` and `esharp addr`; a source-level debugger for the Amiga build is open.
- **Windows (#11):** CI now runs a `windows` job (windows-latest, PR head): `dotnet test` (439 passed, 2 skipped, 0 failed; the runner has a MinGW gcc, so the generated-C tests run there and only the Docker/VM/SDK-dependent tests skip), emit-only builds of `samples/hello` and `samples/cards`, `esharp test samples/linq`, a clean `git status` afterwards, and a package round trip against the PR's own packages (`dotnet pack`, install the tool from the folder into a tool path, a nuget.config with the local feed, version and PackageReference checked, NU1603 fails the step, `esharp new`, `dotnet run`, `esharp test`, `esharp build --emit-only`). A `.gitattributes` keeps the working tree LF (the runner has `core.autocrlf=true`), and the native test output is read CRLF-tolerant. Still needs a person on a real Windows machine: Docker Desktop with `--docker` builds, FS-UAE for Windows, VS Code, and Windows with a different (or no) C compiler. `tools/aros-hosted` is Linux-only. macOS was checked, see "VS Code support".
- **Bindings (#19, #40):** GadTools wrappers (v1), the BOOPSI core with hooks (#40 part 1, see "BOOPSI core and hooks") and the MUI wrappers on it (#40 part 2, see "MUI wrappers") are done. MUI custom classes (#93, see "MUI custom classes") are done too. Open: ReAction (last, if at all).
- **Display design (not implemented):** [design/DISPLAY.md](design/DISPLAY.md) proposes logical coordinates with an integer scale, mode choice that follows the Workbench's monitor family (MODE option, asl screen-mode requester), 256 colours with a shared palette, a gradient pen (copper on AGA), window mode on the Workbench and multi-scale art. Nothing in it is built or verified yet; it waits for the maintainer's approval.
- **v2:** AROS i386 (#4: toolchain, target and hosted runs done, see "AROS i386"; the MUI crashes on hosted i386 were an AROS bug, worked around (#105); open: native pc-i386), E# library source packages on NuGet (#31), user classes deriving from platform classes (#42: done for `GtWindow`, see "Classes derived from platform classes"). Capturing lambdas stored in fields and events (#41): done (PR #83), see "Heap closures and events".
- Smaller follow-ups: `esharp test` for morphos and aros and the Amiga program's return code (today only the ok/FAIL lines are compared; the os4 pipe hides it); OS4 sound on real hardware; the chip budget report only covers constant sizes ([MEMORY.md](MEMORY.md)).

Done and no longer "next": the emulator side of the phase 1 gate (AROS replacement and Kickstart 3.1; 2.04 was also run, historically), the NuGet packages (on nuget.org since 0.1.1), the card demo and rummy, os4 runs in the VM, the `sdl` target, hosted AROS runs, double buffering.

## AmigaOS 4.1 VM runs (wave 7: `esharp run/test --target os4`)

The os4 target now runs unattended in a QEMU Pegasos II VM with AmigaOS 4.1 Final Edition (Updates 1-3 installed; kernel
54.57, exec 54.57, dos 54.136), which the maintainer owns; no AmigaOS file is in the repo. `ESHARP_OS4_VM` names the VM directory
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

Reported as `ES0099` rather than miscompiled: the closure forms listed under "Heap closures and events" (a stored lambda that captures a `for`/`foreach`/pattern/`out` variable, a borrowed delegate parameter, or a variable from outside a borrowed lambda; events with accessors, assigned with `=`, returning a value or in a struct), delegates in struct fields, `is`/`as` to an interface or struct type, ref returns from properties, indexers, delegates and virtual or interface members, `foreach` over anything but arrays, spans and struct/class GetEnumerator types (interface enumerators, `Dispose`, ref `Current`), the pattern forms listed under "Positional and list patterns" (extension `Deconstruct`, `ITuple`, a slice capture that would copy), records, `==` on tuples, `??` on nullable value types; `[InlineAsm]` and `[Interrupt]` (done, see their section) still give it for: operands that are not 32-bit-or-smaller integers or pointers, instance methods, `[Interrupt]` on PowerPC and sdl, and `&Method` of anything but an `[Interrupt]` or `[HookEntry]` method; `[HookEntry]` on sdl or with another signature than three pointer-sized parameters.

Two types whose C names meet after joining with `_` (namespace `A_B` class `C` and namespace `A` class `B_C`) are reported
(ES0099); rename one.

## Targets

CPU and OS are separate axes. `AMIGA` is defined for every Amiga-family target; narrow with `M68K` / `PPC` and `AMIGAOS3` / `AMIGAOS4` / `MORPHOS`.

| `--target` | Compiler | Symbols |
|---|---|---|
| `m68k` (default) | `m68k-amigaos-gcc` 6.5.0b | AMIGA M68K AMIGAOS3 |
| `os4` | `ppc-amigaos-gcc` 11.5.0 | AMIGA PPC AMIGAOS4 |
| `morphos` | `ppc-morphos-gcc` 15.2.0 | AMIGA PPC MORPHOS |
| `aros` | `x86_64-aros-gcc` 9.1.0 | AMIGA X86_64 AROS |
| `aros-i386` | `i386-aros-gcc` 6.5.0 (esharp-aros-i386) | AMIGA I386 AROS |

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
- The `assets` entry of the hwpack has been run in FS-UAE (A1200, Kickstart 3.1) through `tools/hwpack/run-emulated.sh`: every program gave its expected ok count (PR #79), 17 ok for `assets`. Real hardware: not verified.

Not verified: sound was not audible (no OpenAL device in the emulator; only Busy/Stop was checked), a malformed-ILBM size above 4096x4096 pixels is rejected (unit-tested only), ILBM files written by real paint programs (only files written by our own writer in tests, so odd real-world files such as 4-bit CMAPs are untested), dithered output of photographic images beyond the generated gradient, 8SVX with several octaves, real hardware.

## Emitter and SDL follow-ups (issue #12)

- A constant string compare (`const string K = "x"; static bool B = K == "x";`) is folded to `true`/`false` in C, so it is valid in a static initializer (it emitted an `es_streq` call).
- The `??` ES0099 names the case: "nullable value type" only for `T?`; other left types say what is supported.
- `a.Equals(b)`, `object.Equals(a, b)` and `object.ReferenceEquals(a, b)` on class references are reference equality, in a program where no class declares `Equals(object)`. ES0099 (with the reason) for: `Equals` on a struct or enum (`ValueType.Equals`), a user `Equals(object)` called or declared in the program (E# has no `object` type, so a virtual call of it is not supported), and boxed arguments. Not done: a virtual call of a user override.
- Member-level C name collisions (class `A_B` member `C` against class `A` member `B_C`: static fields, static properties, enum members, methods, constructors) are reported as ES0099 on the later declaration. They are not mangled apart.
- SDL: `es_sdl_blit` gets the data lengths and stops the program with a message when the planar data (depth planes) or the mask is too short, like the host fake's exception; ES0102 also explains CS0117 for a member that exists only in the Amiga build of a class sdl has (`Video.Window`, `Dos.DateStamp`); the key map has the keypad, Help (0x5F), and F11/F12 as 0x4B/0x6F (no classic Amiga keys; the codes USB keyboard drivers use: not checked against hardware).
- Checked: `dotnet test` (unit tests with gcc), `esharp test samples/language` host vs m68k (A1200, Kickstart 3.1) and host vs sdl (37 ok each), `samples/names` host vs m68k (12 ok), `samples/cards` on sdl headless with input. Not verified: the new keypad/F11/F12/Help keys on a real SDL window or with XTEST; the blit-length stop on SDL was only checked in the generated text, not run with a short buffer; hosted AROS and OS4 were not run (no target-specific change).

## Small gaps from a larger game (issue #72)

- **`esharp test --args "<line>"`:** the same argument line goes to the host run (`dotnet eshosttest.dll words...`, split like the Amiga startup:
  spaces separate, "quoted text" stays together) and to the m68k, sdl or os4 run, then the checks are compared as before. Unit tests:
  `MainArgsTests` (host run with a quoted word, option parsing, refused characters), `Os4VmTests` (script line).
- **ES0011** says, in the message, to declare the parameter as `Span<T>` (also for class references) and pass the array.
- **Seen** (`esharp test samples/objects --args "SEED=42 auto"`, 21 ok, 0 FAIL, host agrees with the target): m68k on A1200 / Kickstart 3.1 in
  FS-UAE, sdl (esharp-sdl, headless) and os4 (AmigaOS 4.1 QEMU VM, `--args` in the harness script). The string search methods: a scratch program
  of 7 checks agrees between host and m68k. Not verified: `--args` with quoted words in the os4 VM, os4/MorphOS string search runs, AROS (no hosted AROS available).

## Workbench icons (issue #84)

`esharp build` for an Amiga target (m68k, os4, morphos, aros; not sdl, not `--emit-only`) writes `<out dir>/<name>.info` next to the program: a Tool icon.
Our own writer (`src/ESharp.Assets/Icon.cs`, `IconArt.cs`, written from the file layout, no SDK text or code copied; the layout is described in the
comments of `DiskObject`): the classic DiskObject (78-byte header, gadget, one 2-bitplane image, tool types) that every Amiga OS reads, then the OS 3.5 colour
image as an IFF `FORM ICON` (`FACE`, one `IMAG`, uncompressed, one byte per pixel) after it. Settings come from the project's csproj (read as XML, no MSBuild
evaluation, `src/ESharp.Cli/IconStep.cs`): `EsStackSize` (bytes, default 65536; 4096 to 16 MB), `EsToolTypes` (`SEED=42;AUTO`), `EsIcon` (a PNG up to
256 x 256: the classic image takes the nearest of the four Workbench colours, the colour image up to 64 colours, transparent pixels stay transparent; `none`
writes no icon). Default picture: a blue plate with "E#" (48 x 32, drawn in `IconArt.Default`). The stack default is a round generous number, not measured per
sample. Drawer, disk and project icons with a drawer record are not written (the writer refuses them); no selected-state image (the classic icon highlights by
complement), no RLE, no ARGB chunk.

**Workbench start and `Args`.** A Workbench start has argc 0 and argv pointing at the WBStartup message (every Amiga C startup does this); the emitted C
(only when a program uses `Args` or `Main(string[] args)`) opens icon.library (v39) only then, reads the program's own icon (`GetDiskObject` with the lock and
name from the startup message), copies the tool types into a static 2 KB buffer as the argument words and at once frees the DiskObject, drops the OS4 interface and closes the
library, so nothing stays allocated or open (the first version leaked them; the review found it). Argument 0 is the program name from the startup message. `KEY=VALUE` and `SWITCH` stay as they are;
quotes around a value (`NAME="two words"`) are removed as a Shell does. Skipped: `(NAME)` (switched off in the icon), empty ones and Workbench's own tool types TOOLPRI, STARTPRI,
DONOTWAIT and TOOLWINDOW (case-insensitive; WINDOW is not skipped, a program may use it as its own switch). At most 64 words and 2 KB of text; the rest is dropped without a message. So `Args.Value("SEED")`, `Args.Has("AUTO")` and
`args` work as from a Shell. No C# bindings were added for icon.library: the C uses the toolchain's own `proto/icon.h`, `workbench/startup.h` and `workbench/workbench.h`, so no
vector number or struct offset is written by us (the usual `IconBase` / `IIcon` names are renamed so the C startup does not open the library itself). A Shell start and
`esharp run --args` are unchanged. Host gcc tests: the tool type conversion, and a whole Workbench start against fake icon.library and WBStartup headers that count open libraries
and loaded DiskObjects (both 0 after the run). csproj values are literal only: `$(...)` is refused with a message; `Condition` and Directory.Build.props are not looked at. In a
converted PNG an opaque black pixel keeps its own palette entry (entry 0 is only for transparent pixels). Prefer icon-sized PNGs (32 to 64 pixels).

**Packaging.** The os4 test CD (`esharp run/test --target os4`) now holds `<name>.info` next to the program. `tools/hwpack/build.sh` copies each program's icon into the
pack drawer and the one-disk `hwtest.adf` (38 icons of about 2 KB; `hwtest.adf` is now 801 KB used, 79 KB free with the closures program, was 660 KB / 220 KB). The bootable
`hwtest-boot.adf` (the pack plus the AmigaOS files from the user's disk) has 88 KB free without icons (with the closures program) and the icons need about 120 KB, so it is written from a
copy of the pack without the `.info` files (it boots into RunAll, not Workbench).

`samples/wbstart` (new; `WbStart.csproj` sets `EsStackSize` 32768 and `EsToolTypes` `SEED=42;AUTO`) opens a window that shows the number of arguments, SEED, AUTO and, on
m68k, the stack size read from the task.

Verified:
- `dotnet test`: 452 passed, 0 failed, 2 skipped (454) after the review fixes. `IconTests` (13 + the Workbench-start ones; the filter run gave 13 passed before the WINDOW change): header offsets and endianness, write then read back (fields, tool types, both images),
  sizes of the classic image and the FORM, odd widths, PNG conversion, an opaque black pixel next to a transparent one, csproj properties, `$(` refused, Amiga-only, tool types reaching `Args`
  in generated C built and run with the host gcc, and a whole Workbench start against fake icon.library / WBStartup headers (quotes removed, TOOLPRI / donotwait / (OFF) / empty skipped,
  open libraries and loaded DiskObjects both 0 afterwards).
- A1200, Kickstart 3.1 (40.069) in FS-UAE 3.2.35, Workbench 3.1 disk 2 booted in DF0, the built program and `.info` in a directory hard disk: the volume window shows the
  E# icon (classic image), a double-click starts the program (scripted mouse through XTEST, screenshots looked at), the window shows `arguments 2, SEED 42, AUTO yes`
  and `Stack 32768` (the icon's stack size against the Workbench default). Re-run after the review fixes (static buffer, DiskObject freed, library closed): same result. The hardware pack's `hwtest.adf` as DF1: its window shows the icons
  (aga, asl, assets, cards, collections ... in a scrolled window); a double-click on one of them was not repeated with the final colour format.
- AmigaOS 4.1 (QEMU pegasos2, `ESHARP_OS4_VM`): the program and icon on a CD (`ESTEST`), the Workbench window shows the icon, a double-click starts the program (scripted mouse
  through the QEMU monitor, screendumps looked at) and the window shows `arguments 2, SEED 42, AUTO yes`; re-run after the review fixes with the same result. What the first try taught: OS4's Workbench dropped the whole icon when the colour image was packed at 3 bits per pixel with Depth 3; one byte per pixel with the depth field 8 is accepted (and the FACE palette field is the largest palette in bytes minus 1).
  The stack size on OS4 was not read back (the sample's probe is m68k only).
- Built with the reworked C (static buffer, release code, `.info` written each time): `samples/wbstart` for m68k, os4 and aros; `samples/mainargs` for morphos and aros. `samples/wbstart` for morphos stops at the same gadtools pointer-type errors as `samples/gui` (Gui is not ported to morphos); none of its errors mention the icon or Workbench-start code.
- Not verified: MorphOS and AROS Workbench start (the C compiles; nothing was run there: hosted AROS has no Workbench in the harness; issue #101 below adds Wanderer to the harness), real hardware, Workbench 3.1 showing the
  colour image (it reads the classic one only; GlowIcons/OS 3.5+ were not available), OS 3.5/3.9, a stack size larger than the Workbench default taking effect on OS4, a drawer
  icon for a program folder, how the colour image looks on MorphOS/AROS.

### NewIcons image; the icon on AROS and OS4 (issue #101)

Every E# icon now also carries a **NewIcons** image (NewIcons is a freeware add-on that gives Workbench 3.0/3.1 colour icons): after the
program's own tool types (`EsToolTypes`) come the marker tool type `*** DON'T EDIT THE FOLLOWING LINES!! ***`, the `IM1=` lines (normal image)
and the `IM2=` lines (selected image: the same pixels at two thirds brightness). The classic image and the OS 3.5 `FORM ICON` are written as
before. Our own encoder and decoder (`src/ESharp.Assets/NewIcons.cs`), written from the format facts (described in the class comment), with
AROS's icon.library NewIcons reader as the reference for the exact encoding (no code or comment text taken): a 5-character header (transparency,
width and height + 0x21, colour count as two 6-bit digits), the palette as RGB bytes, then the pixels at just enough bits per pixel, all as 7-bit
groups in printable characters (0x20-0x6F, 0xA1-0xD0) with 0xD1-0xFF as runs of zero groups. A reader drops the bits left at a line end, so each
line holds a whole number of values and its padding is less than one value. E#'s own limits: lines of at most 127 characters, pictures up to
93 x 93 (a larger PNG gets no NewIcons image), at most 32 colours (the default art has 7; a PNG is reduced again with the same colour reduction
and unused entries dropped). The default icon is 3570 bytes with 15 NewIcons lines (was about 2 KB). `EsNewIcons` `false` in the csproj leaves
the lines out; `DiskObject.Read` takes them out of `ToolTypes` again (into `NewIcons` / `NewIconsSelected`).

**Hardware pack floppy:** with the NewIcons lines the pack no longer fitted on one floppy (`mkadf.py: disk full`), so `tools/hwpack/build.sh`
now writes `hwtest.adf` from a copy whose icons went through `tools/hwpack/stripni.py` (removes the marker and the IM lines, standard library
only; the result is byte-identical to the icon written with `EsNewIcons` false, checked by a unit test). The pack drawer keeps the full icons.
Re-run (`--adf-only --boot-wb <wb>`, 38 programs, 39 icons): `hwtest.adf` 865 KB used, 14 KB free; `hwtest-boot.adf` (no icons) 853 KB used,
26 KB free.

**Args on a Workbench start** skips the marker and every `IM1=`/`IM2=` tool type after it (an `IM1=...` before the marker is the program's
own and is kept); AROS's icon.library, which turns a NewIcons-only icon's marker into an empty tool type and removes the IM lines, gives an
empty word, skipped as before. So the program sees only its real tool types on every OS.

**Hosted AROS harness:** `tools/aros-hosted/aros-hosted.sh` has two new options: `--files <dir>` copies a folder into a drawer `SYS:ESTest`
(with a drawer icon taken from the copied AROS tree, so Wanderer shows it), and `--wanderer` starts no program from S:Startup-Sequence:
the program and its `.info` go into `SYS:ESTest`, AROS boots on to Wanderer, and `--input` / `--shot` run after `--wait` seconds (default 25).
`xinput.py` gained `dbl x,y` (double-click) and `sleep s`.

What each system shows (default E# icon, 48 x 32):

| System | Image shown | Tool types the program gets |
|---|---|---|
| Workbench 3.1 (no NewIcons) | classic 4-colour image | `SEED=42`, `AUTO` (NewIcons lines skipped by E#) |
| Workbench 3.x with NewIcons | NewIcons image (expected; not verified) | the same |
| AmigaOS 4.1 | colour image (FORM ICON); NewIcons-only icon: colour image too | `SEED=42`, `AUTO` |
| AROS (Wanderer) | colour image (FORM ICON); NewIcons-only icon: colour image from the NewIcons lines | `SEED=42`, `AUTO` (since issue #107, see below) |

Verified:
- `dotnet test`: 588 passed, 0 failed, 2 skipped (590). New `NewIconsTests` (13, with `stripni.py` run through python3 when it is there): the default icon written by `IconStep` read back by an independent decoder in the test
  (it walks the characters the way icon.library readers do, so a padding that would make an extra value fails) for both images, pixels and
  palette; random pictures (1 x 1 to 93 x 93, 2 to 32 colours, transparent colour 0, 2, 3 or none) round trip; every NewIcons line at most 127
  characters, starting `IM1=`/`IM2=`, with no zero byte and only the allowed characters; the size and colour limits refused; a 64 x 64 PNG
  with more than 32 colours reduced to at most 32 with transparency kept; `EsNewIcons` false and a bad value; Args on a Workbench start
  (generated C built and run with the host gcc): the real lines of the default icon after `SEED=42`, `IM1=mine`, `AUTO` give exactly those 3
  words, and the AROS form (an empty marker) gives `SEED=42` alone.
- A1200, Kickstart 3.1 (40.069), Workbench 3.1 disk 2 in DF0, FS-UAE 3.2.35, `samples/wbstart` (m68k) with its new icon in a directory hard
  disk: the DH0 window shows the E# icon (classic image), a double-click starts it and the window shows `arguments 2, SEED 42, AUTO yes,
  stack 32768` (screenshots looked at; scripted mouse through XTEST).
- AmigaOS 4.1 (QEMU pegasos2): `samples/wbstart` (os4) and its icon on a CD `ESTEST`: the window shows the colour E# icon, a double-click
  starts it, `arguments 2, SEED 42, AUTO yes` (screendumps looked at). A second icon on the same CD written with only the NewIcons image
  (FORM ICON left out) also shows the colour E# plate, and started from it the program gets `arguments 2, SEED 42, AUTO yes`.
- Hosted AROS x86-64 (image `esharp-aros-hosted`, nightly 20261114, icon.library 44.11): a throwaway C probe built with the toolchain's
  `x86_64-aros-gcc` (not committed) calls `GetDiskObject` and `GetIconTagList` and `IconControl` on three icons of `samples/wbstart`:
  (a) as built (FORM ICON and NewIcons): colour image 48 x 32, palette 7, transparent colour 0, selected image present, first colours
  000000, 0E1C46, 3B72DC and a pixel checksum equal to E#'s own picture; the tool types are all 17 (AROS leaves the NewIcons lines in place
  when the icon has a colour image). (b) NewIcons only: the same width, height, palette 7, colours and checksum, decoded by AROS from our
  NewIcons lines, frameless (as AROS marks NewIcons), tool types `SEED=42`, `AUTO`, `` (3). (c) FORM ICON only (`EsNewIcons` false): the same image,
  tool types 2. Both API calls agree.
- Wanderer (`aros-hosted.sh wbstart --docker esharp-aros-hosted --wanderer`, scripted double-clicks on System, the ESTest drawer):
  the drawer shows the colour E# plate for the icon as built and for the NewIcons-only icon (screenshots looked at). AROS's own Calculator
  started from an E# icon (its program renamed next to our `.info`) opens normally.

Not verified / found:
- **A program built with the toolchain image's AROS compiler crashed when Wanderer started it** (exec `RemHead`, address error, in the
  program's task before any E# code matters), while the same program from the Shell worked and AROS's own Calculator started from
  Wanderer worked. Found and fixed in issue #107: see "Wanderer start on AROS" below.
- Workbench 3.x with NewIcons installed (the colour image on 3.1): not verified. NewIcons was not fetched (no per-user fetch script was
  added; its archive terms were not checked).
- A PNG icon's NewIcons image on a real system, MorphOS, OS 3.5/3.9, real hardware.

### Wanderer start on AROS (issue #107)

**Cause.** The x86-64 toolchain's startup code waited for the WBStartup message on the wrong address. AROS's C startup (`startup.o` and
`libautoinit.a` of the toolchain) runs a `PROGRAM_ENTRIES` function, `__startup_fromwb`, before `main`: when the process has no CLI it calls
`WaitPort` and `GetMsg` on the process's own port, `&pr_MsgPort`, at an offset compiled in from the SDK's `struct Process`. The `x86_64-aros`
toolchain (amigadev/crosstools, libraries dated February 2020) has headers without the SMP field at the end of `struct Task`; the hosted
x86-64 nightly is ABIv1 with `__AROSPLATFORM_SMP__` in its `aros/config.h`, and its `struct Task` ends with a `spinlock_t`. So `pr_MsgPort` is
at 0x100 in the toolchain's `fromwb.o` and at 0x130 in the nightly's own (the nightly's `Developer/lib/libautoinit.a`, compared by
disassembly), and the toolchain's startup waited 48 bytes too early inside the Process. A Shell start does not wait, so it was not affected.
The i386 toolchain image (`esharp-aros-i386`, built from AROS source of 2026-11-15) has headers that match the i386 nightly, and its programs
already started from Wanderer before this change (`samples/wbstart`: `arguments 2, SEED 42, AUTO yes`), so the crash was specific to the
prebuilt x86-64 toolchain. Measured with a throwaway probe started from the Shell (not committed): on the x86-64 nightly the port with node
type `NT_MSGPORT`, signal bit `SIGB_DOS` (8) and this process as its task is 48 bytes after the toolchain's compiled offset, and nothing else
in the 128 bytes after that offset matches; on i386 it is at the compiled offset. The same kind of SMP layout change had already been found
for BOOPSI objects (`es_domethod`, above).

**Fix (generated C, every AROS program).** The C of every Amiga target now has, inside `#if defined(__AROS__)` (the m68k, os4 and MorphOS
compilers never see it; sdl's C does not have it), a Workbench start entry of E#'s own. `int __nowbsupport` and `WBenchMsg` are defined in
the program, so the linker no longer takes the toolchain's `fromwb.o` out of `libautoinit.a` (those two symbols are why it was linked;
checked with `nm`: no `__startup_fromwb` in the x86-64 or i386 program any more), and `es_aros_wbstart` joins the `PROGRAM_ENTRIES` set of
`aros/startup.h` at the same priority (-50, before the console-window and command-line entries, which look at `WBenchMsg`). It asks
dos.library's `Cli()` whether this is a Shell start; if not, it finds the process port at run time (the first `MsgPort` from the compiled
`pr_MsgPort` offset up to 128 bytes after it whose node type is `NT_MSGPORT`, whose signal bit is `SIGB_DOS` and whose task is this process),
waits for the message, sets `__argc` to 0 and `__argv` to the message (what `main` and `Args` already expect), and makes a copy of the
icon's directory lock the current directory. After the program and its exit code it restores the old directory, unlocks the copy and
replies to the message under `Forbid()`, as every Amiga C startup does. No toolchain file was changed. For m68k and os4 the C differs only
inside that `#if`: built with and without it, the m68k program is byte-identical and the os4 program differs only in the source file name
in its symbol table (identical after strip).

**Harness.** `tools/aros-hosted/xinput.py` has `drag x1,y1 x2,y2` (press, move in steps, release), used to move the console window that AROS
opens for a Workbench-started program's output off the program's own window.

Verified (hosted AROS images `esharp-aros-hosted`, nightly 20261114, and `esharp-aros-hosted-i386`, nightly 20261116):
- Before the fix, x86-64: `aros-hosted.sh wbstart --docker esharp-aros-hosted --wanderer --log --input "dbl 45,135;sleep 4;dbl 368,150;sleep 4;dbl 90,128;sleep 6"`
  (System, ESTest, the wbstart icon): 1 trap, `Exec_43_RemHead`, `Hardware bus fault/address error`, task `wbstart`. i386 with the same
  input: 0 traps, `arguments 2, SEED 42, AUTO yes`.
- After the fix, x86-64 and i386, the same input plus `;drag 200,42 560,420;sleep 2`: 0 traps; the program's window shows `Arguments 2`,
  `SEED 42`, `AUTO yes` (the icon's 15 NewIcons lines skipped) and the console shows `arguments 2, SEED 42, AUTO yes, stack (not read)`
  (screenshots looked at on both). With `;click 52,42;sleep 4` added (the window's close gadget) the program ends, its windows are gone,
  Wanderer is back, 0 traps and no alert in the log, on both. `samples/gui` (no `Args`) started from Wanderer on x86-64: its window opens,
  `ok the window opens` and `ok the string gadget holds the default name`, 0 traps.
- Shell start unchanged: `aros-hosted.sh wbstart --args "SEED=7 auto"` prints `arguments 2, SEED 7, AUTO yes` on x86-64 and i386.
- Shell regression with the final C (`aros-hosted.sh <sample>`, x86-64 and i386, the same on both): language 39, objects 21, linq 37,
  lambdas 16, inherit 24, generics 24 ok, no FAIL, 0 traps; `mainargs --args 'ONE "two words" RC=5'`: 3 arguments, `two words` whole,
  `rc 5`. An earlier run of the same set gave i386 `generics` 3 traps with all 24 ok, and 3 re-runs 0 traps: the intermittent i386
  Wanderer-task traps described under "AROS i386".
- A1200, Kickstart 3.1 (40.069), Workbench 3.1 disk 2 read-only in DF0, FS-UAE 3.2.35, `samples/wbstart` (m68k) and its icon on a directory
  hard disk, opened by scripted double-clicks: the window shows `arguments 2, SEED 42, AUTO yes, stack 32768` (screenshot looked at).
- `dotnet test`: 603 passed, 0 failed, 2 skipped (605). New `ArosWorkbenchStartTests` (4): the entry is in the AROS branch of the C of
  every Amiga target and not in sdl's; built with the host gcc against stand-in AROS headers in which the process port is 48 bytes after the
  compiled offset and a port of another task sits at the compiled offset: a Workbench start waits on the right port, `main` gets argc 0,
  `Args` are the icon's tool types, the copied directory lock is current during `main` and is restored and unlocked before `Forbid` and
  `ReplyMsg`; a Shell start waits for nothing and keeps the command line; a program without `Args` replies too. With the scan cut down to
  the compiled offset only, the two Workbench tests fail.

Not verified: native AROS (pc-x86_64 or pc-i386 in QEMU or on hardware), nightlies other than these two, a start with project icons as
extra arguments (`sm_NumArgs` above 1: the message is passed on as before, `Args` reads only the program's own icon), clang building the
entry (no clang on this host; CI builds the host test C with clang -Werror).

## AROS i386 (`--target aros-i386`, issue #4)

**Toolchain image `esharp-aros-i386` (`docker/aros-i386`).** Multi-stage: Ubuntu 24.04 by digest, the AROS sources at commit
`656d2825b63dce8e3acca5e139197864169dc209` (2026-11-15, the last commit before the linux-i386 nightly 20261116 was configured; git checks
the sha), AROS's own `configure --target=linux-i386` and `make crosstools` then `make sdk`. The tarballs the crosstools rules would download
(gcc 6.5.0, binutils 2.32, gmp 6.3.0, mpfr 4.2.2, mpc 1.4.1, isl 0.25: the AROS defaults for i386) are fetched first and checked by sha256.
The final stage keeps `/opt/i386-aros` only: `i386-aros-gcc` 6.5.0 and the SDK in `/opt/i386-aros/sdk` (`--sysroot`). Image 1.3 GB. Build time
on this host (32 cores, shared, 16 jobs), `docker build --no-cache`: 817 s in all, of which crosstools 580 s and the SDK 60 s. Too long and
too big for a CI job, so it is built locally and not in CI. The SDK headers were compared with the hosted nightly's `Developer/include`: the
18164 headers the SDK has are all in the nightly, and after dropping the lines with build paths only 3 differ (`proto/cardres.h`,
`proto/ramdrive.h`, `proto/translator.h`: different generated forms); the nightly has 293 more, from contrib (AHI, ReAction classes,
expat, freetype2 and others). E# includes none of these 296, so for the libraries E# uses the two match.
The nightly's own configure line passes `--with-aros-toolchain=yes`, which means a prebuilt toolchain; with it `make crosstools` builds nothing, so the image leaves it out.

**Target.** `aros-i386` defines `AMIGA`, `AROS`, `I386` (not `X86_64`), 32-bit pointers. The struct offsets are measured, not assumed:
`tools/BindGen/layouts.sh` now measures three tables (AROS x86-64, AROS i386, m68k) and `Layout.g.cs` has an `#elif I386` table. 53 of the
229 values differ from m68k (i386 aligns LONGs and pointers to 4 bytes where m68k uses 2: Window, IntuiMessage, IOAudio, timerequest,
InputEvent, TextFont, NewGadget, Interrupt, RastPort, DBufInfo, GfxBase and ExecBase offsets), so the m68k table could not have been reused.
The rest of the AROS platform code already used `sizeof(void *)`, `nuint` and the AROS SDK macros (`AROS_LC` for MUI, plain C hooks, the
BOOPSI class-pointer probe), which are right for i386 as they are. CLI: `--target aros-i386`, `--docker` without a name means
`esharp-aros-i386`, `--mui` refused as for `aros`; the VS Code template's image prompt names it.

**Hosted i386 runtime.** `docker/aros-hosted` takes `--build-arg AROS_ARCH=i386` (tag `esharp-aros-hosted-i386`, 1.32 GB): the
`AROS-20261116-linux-i386-system` nightly (sha256 `ffeb6e7bf6407911631e793399c5f0c6e62ed2c81fcbc20c943116b86a87dcca`), `libc6:i386` and the
i386 X11/ALSA libraries the hosted kernel loads; `--build-context archive=<folder>` as for x86_64. `tools/aros-hosted/aros-hosted.sh --arch
i386` (or an image name with `i386`) runs `obj/aros-i386/<sample>`.

**A fix found on the way (all AROS targets).** Generated C wrote its out-of-memory and index messages to stdio's `stderr`, so every
program with heap objects opened posixc.library. On hosted i386 AROS a program that has posixc.library open and creates a Zune Application
crashes in the C library's exit code (`__stdc_program_end` / `__callexitfuncs`); reproduced in a 20-line C program, so it is an AROS problem
(later found: memory corruption from muiscreen.library's handler stack, issue #105, gives this kind of crash; whether it was this one is not verified),
not E#. On AROS the messages now go through dos.library (`ErrorOutput()`, else `Output()`), so no E# program opens posixc.library; other
targets keep `stderr`. New test `Aros_error_messages_use_dos_library_not_stderr` (runs the helpers with the host gcc).

**Verified (this branch):**
- `docker/aros-i386/smoke-test.sh`: hello builds as `ELF 32-bit LSB relocatable, Intel 80386` and prints `Hello from E#!` on hosted i386
  (0 traps). `docker/aros-hosted/smoke-test.sh esharp-aros-hosted-i386` passes too.
- All 42 samples built with `--target aros-i386 --docker esharp-aros-i386` and run on hosted i386 AROS in `esharp-aros-hosted-i386`, next to
  the same samples built with `--target aros` on hosted x86-64: the ok and FAIL counts are equal for 41 of them (642 ok on i386, 646 on
  x86-64; the only FAIL on both is rtg's known `the native screen is not RTG`): language 39, boopsi 37, linq 37, phase2 32, rummy 31,
  dbuffer 30, generics 24, inherit 24, patterns 23, screen 23, objects 21, sound 21, closures 20, collections 20, operators 20, dates 19,
  memory 19, properties 18, refs 18, assets 17, cards 16, lambdas 16, interrupts 14, names 12, iff 10, timing 10, controls 9, fonts 8,
  keys 8, aga 3, asl 3, textspeed 1, rtg 6 (+1 known FAIL); hello, hwinfo, mainargs, muicheck (`muimaster.library 19.81`) and trace run to
  the end without ok lines; gui, guiclass and wbstart wait for input on both arches (gui 2 ok, guiclass 3 ok before that).
- With input on hosted i386: `gui --input "click 208,74;click 47,37"` 7 ok (Hello, reply, close); `boopsi --args WAIT --input "click
  75,57;click 220,84;click 47,37"` 38 ok (button, slider page to 55, close). `cards --shot --input "click 400,300"`: the screenshot was
  looked at and shows the 640x400 table with the HD cards, the card back and the sprite.
- mui on hosted i386, when this was written: 28 checks up to `SHOT`, then a crash in the program's own task, 3 of 3 runs; with
  `Trace.Line` in every check, or with mungwall, it passed. Found and worked around later: see "MUI on hosted AROS: the muiscreen.library
  handler stack (issue #105)". Now 32 ok in 10 of 10 runs.
- Intermittent traps in the Workbench task (Wanderer) after a program has ended, on hosted i386 only: also with a program that only waits
  5 s (1 of 14 runs), so it is the nightly, not E# (when no program opened MUI before it, Wanderer is the first to open muimaster.library, so
  the muiscreen.library stack overrun of issue #105 may be the cause; not verified); the program's own output and checks are complete in those runs.
- x86-64 unchanged: every sample still builds with `--target aros`; on hosted x86-64 mui 32 ok and boopsi 37 ok; `esharp-aros-hosted`
  rebuilt from the changed Dockerfile (default `AROS_ARCH`) passes its smoke test.
- m68k on Kickstart 3.1 (A1200, real ROM): `esharp test samples/language` 39 ok and `samples/boopsi` 37 ok, host and Amiga agree.
- `dotnet test`: 586 passed, 2 skipped (the SDK-backed BindGen ones), 0 failed; after merging main (#102, NewIcons) 599 passed, 2 skipped, 0 failed, and on hosted i386 hello, language 39, boopsi 37, cards 16 re-run unchanged.

**Not verified:** native pc-i386 (the boot ISO in QEMU), real i386 hardware, CI building the image (it is not in CI).

## MUI on hosted AROS: the muiscreen.library handler stack (issue #105)

**Symptom.** On hosted i386 AROS (nightly 20261116) MUI programs failed now and then, depending on the binary's layout: samples/mui
crashed after 28 checks (bus error, PC outside every module), samples/muiclass passed 1 of 7 runs (output stopped, then the shell
trapped in `GetVar`; debug builds trapped at the first `Create` with `IntuitionBase` read as 0x32). `Trace.Line` in every check or
mungwall made them pass. Hosted x86-64, m68k and OS4 passed.

**Narrowed down** (each step a build of the generated C with changes, run on hosted i386):
- A copy of samples/muiclass whose `Main` only opens and closes muimaster.library: the shell traps in `FindVar`/`GetVar` after the
  program has ended, every run (the link of a local variable's node reads as the text "Pack"). The local variable list is intact when
  the program ends. A trace of exec's `FreeMem`/`FreeVec`/`FreePooled`/`CloseLibrary` from the program showed nothing that explained it.
- With muimaster.library opened once by another, plain C program earlier in `S:Startup-Sequence`, the same binary passes, and the
  unchanged samples/muiclass passes 10 of 10 runs. With only the libraries muimaster.library's init loads opened earlier (coolimages,
  cybergraphics, rexxsyslib, keymap, commodities, diskfont, asl) it still traps; the rest of the list (workbench, gadtools, layers,
  iffparse, datatypes, icon, locale, muiscreen) opened by another program made that program's boot crash in a task named
  "PUBSCREEN handler".
- That task is muiscreen.library's (AROS `workbench/libs/muiscreen/muiscreen_init.c`, opened by muimaster.library's init): it is
  started with `AddTask` on a stack of `AllocMem(4096, MEMF_PUBLIC)` (`MUIS_STACKSIZE`), on the task that opens the library first.
- `tools/aros-hosted/muiscreen-stack.c` (plain C, ours) measures it: it opens muiscreen.library with exec's `AllocMem` patched for its
  own task so that the stack gets 16 KB of a known pattern below it. Result, 3 of 3 runs each, the same every run: on hosted i386 the
  handler writes all 4096 bytes of its stack and **4244 bytes below it**; on hosted x86-64 (nightly 20261114) **3448 bytes below it**.
  Below the stack is the allocator's header of the stack block and then whatever block lies under it, so what breaks depends on the
  memory layout: an AROS bug, not E#. Probably the hosted kernel's task switches put the host's signal frames on the task's stack (the
  writes below the stack come in blocks of about 2 KB, and kernel code addresses are on the stack); not checked in the AROS source, and a
  native AROS was not measured. Both sizes were measured on one host (one CPU) only. If the host's signal frames are the cause, the size
  depends on the host CPU's xsave area (AVX-512 and similar state make the frame larger), so other machines may overrun by more or less
  than these numbers; the guard's 32 KB is far above both, but that is a margin, not a measurement on other hardware.

**Workaround in E# (all AROS targets).** `Mui.Open` calls `es_mui_guard_begin()` before opening muimaster.library and
`es_mui_guard_end()` after. When muiscreen.library is not loaded yet, the guard opens it itself with exec's `AllocMem` patched (`SetFunction`,
removed again right after; the vector holds a 16-byte stub in public memory that jumps through a slot, see below): only for the program's own task, only the request of 4096 bytes `MEMF_PUBLIC` that directly follows a
`MEMF_PUBLIC | MEMF_CLEAR` request of at least `sizeof(struct Task)` (the system's struct may be larger: 304 bytes on the x86-64 nightly,
176 in the x86-64 toolchain's headers). That request gets 32 KB of its own below it, so the overrun lands there. The memory is never
freed (the library never frees the stack either). When the guard is done the stub's slot is set to the system's own AllocMem and the
old vector is put back; if another task patched AllocMem meanwhile (the vector is no longer the stub) that task's patch is put back and
left alone: it calls the stub, which only passes calls on and references nothing of the program's segment, so it stays valid after the
program exits (the stub is never freed, 16 bytes). The stub is x86 machine code (`jmp` through the slot) for hosted AROS x86-64 and
i386; on another CPU `es_muis_stub` says no and the guard is skipped. A test builds the stub with the host compiler in executable memory
and calls it; the another-task-patched race itself was not exercised. Traced on hosted i386, that pair is the only match during the open. When muiscreen.library
is already loaded (Wanderer or another MUI program opened MUI first), nothing is done: its stack was set up then and the overrun hit
whatever was under it, which E# cannot change. Other targets: the guard is an empty function. The choice of the request is plain C
(`es_muis_pad`) and is run by a test with the host compiler.

**Verified:**
- After the stub change (issue #117): samples/muiclass on hosted AROS x86-64 26 ok, 0 traps, and on hosted i386 26 ok, 0 traps; samples/mui 32 ok, 0 traps on both (one run each, `--wait 0`); m68k Kickstart 3.1 with MUI 3.8 `esharp test samples/muiclass` 26 ok on host and Amiga; OS4 VM `esharp test samples/muiclass --target os4` 26 ok. Not verified: the case of another task patching AllocMem during the open.
- Debug build of samples/muiclass printing the handler task after `Mui.Open`: its `tc_SPLower` is the padded block, on hosted i386 and on
  hosted x86-64.
- Hosted i386 (esharp-aros-hosted-i386, `--target aros-i386`): samples/muiclass 25 ok, 0 traps in **10 of 10** runs; samples/mui 32 ok,
  0 traps in **10 of 10** runs (`tools/aros-hosted/aros-hosted.sh <sample> --docker esharp-aros-hosted-i386 --wait 0`). Before the
  workaround, the same command gave samples/muiclass 25 ok in 3 of 5 runs, and 0 of 8 with `Stack 1000000` before the program in the
  startup sequence; with the workaround and that stack setting 5 of 5. Screenshots (`--shot`) looked at: the custom class window (graphs
  at 70% and 20%, "Level 70", the slider at 70) and the samples/mui window (Volume 42, the list with three lines), in Zune's look.
- Hosted x86-64 (esharp-aros-hosted, `--target aros`): samples/muiclass 25 ok and samples/mui 32 ok, 0 traps, 3 of 3 runs each.
- A1200, Kickstart 3.1 (40.069), MUI 3.8, Workbench 3.1 disk 2 (`esharp test samples/<s> --target m68k --docker <m68k image> --model
  A1200 --kickstart <rom> --mui 3.8 --workbench <wb>`): samples/mui host 32 ok, Amiga 32 ok; samples/muiclass host 25 ok, Amiga 25 ok;
  they agree.
- AmigaOS 4.1 VM (MUI 5): `esharp test samples/muiclass --target os4`: host 25 ok, OS4 25 ok, they agree.
- `dotnet test`: 642 passed, 2 skipped (the SDK-backed BindGen test and the OS4 VM test), 0 failed. New: `Mui_open_guards_the_muiscreen_handler_stack_on_aros` (every Amiga target: the guard around the open; the request choice run with the host gcc).

**Not verified:** native AROS (pc-i386, pc-x86_64): whether the handler overruns its stack there; MUI programs started when muiscreen.library
is already loaded (the usual case with Wanderer running) are not protected by the guard, and nothing in this repository can be; reported
upstream: not yet. A future muiscreen.library with a larger stack would not match the guard's request, so the guard would then do nothing.

## Hosted AROS in a Docker image (issue #75)

`docker/aros-hosted` builds `esharp-aros-hosted`: debian bookworm-slim (by digest) with Xvfb, xwd, python3, libx11 and libXtst, and the AROS
x86-64 hosted nightly 20261114 (sha256 checked at build; build args `AROS_DATE` and `AROS_SHA256`; `--build-context archive=<folder>` builds from a
local archive because SourceForge rotates nightlies; the archive is git-ignored). AHI's void audio mode is the only audio mode. `tools/aros-hosted/aros-hosted.sh
<sample> --docker esharp-aros-hosted [--shot] [--input] [--wait] [--log]` mounts the repo read-only and copies the image's pristine AROS tree for each run, so
`S:Startup-Sequence` is never patched in place; the `AROS_HOSTED=<dir>` path still works. `docker/aros-hosted/smoke-test.sh` builds `samples/hello` for aros
and runs it in the image.

- **Verified (main 31c0dd7 plus this change, image built from the local archive):** smoke test passes (hello prints `Hello from E#!`, 0 traps, image tree
  unpatched). Every sample except gui, asl and keys (33 of them) built with `--target aros --docker esharp-amiga-toolchain` and run in the image: 0 traps, no
  FAIL apart from the known rtg `the native screen is not RTG`. ok counts: language 39, linq 37, phase2 32, rummy 31, dbuffer 30, generics 24, inherit 24,
  patterns 23, screen 23, objects 21, sound 21, collections 20, operators 20, dates 19, memory 19, properties 18, refs 18, assets 17, cards 16, lambdas 16,
  interrupts 14, names 12, iff 10, timing 10, controls 9, fonts 8, rtg 6 (+1 known FAIL), aga 3, textspeed 1; hello, hwinfo, mainargs and trace print no ok
  lines and ran to completion. These equal the counts of the stopgap run on the same nightly outside the image. `cards --shot` with `--input "click 400,300"`:
  the screenshot shows the table, cards and sprite.
- **Default build (no `--build-context`, `docker build --no-cache -t ... docker/aros-hosted`):** built from the SourceForge download (about 4.5 minutes,
  the sha256 check printed OK); the smoke test against that image passes (hello prints `Hello from E#!`, 0 traps, image tree unpatched).
- **Not verified:** gui, asl and keys in the image, a different nightly date.

## Internal library members are not reachable from programs (issue #91)

`esharp build` compiles the library and the program as one assembly, so C# let a program call `internal` library members (for example `Cast.ObjectToPtr`),
also from a program class declared in `namespace ESharp.Platform`; the dotnet host build rejected the same code. New analyzer error **ES0103** ("'X' is
internal to E#'s library"): a file that is not a library file (library files are found by the `<esharp-lib>/Runtime/`, `<esharp-lib>/Platform/` path prefixes (a program file whose path starts with that is renamed `./<esharp-lib>/...`) of the
`ESharpLibrarySources` option, never by namespace) refers to a symbol declared in library source that, or whose containing type, is `internal` or
`private protected`. It is active only where the library is source (the compiler); in the IDE and the host build the library is metadata and C# already errors,
so those paths are unchanged. The platform's own internal use is not affected. The existing `Cast` intrinsic tests now compile their source as a library file.

- Tests (`LibraryInternalTests`): a direct call of `Cast.ObjectToPtr`, a call from a class in `namespace ESharp.Platform`, an internal type (`DosLib`, m68k), the
  library's own internal use and the public surface (accepted), a program member with the same name as a library internal (accepted).
