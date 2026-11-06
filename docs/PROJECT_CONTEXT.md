# E# project context (handoff)

The original design document is kept at [design/E_Design_Document.pdf](design/E_Design_Document.pdf) (1 Nov 2026). Where this repo and the PDF disagree, this repo is newer: for example PowerPC targets (AmigaOS 4, MorphOS) are first-class here, not "later".

Condensed from the design document and the original planning chat. Treat `docs/STATUS.md` as the source of truth for progress.

## Decisions
- Approach: Roslyn parse -> subset analyzer -> lowering -> C emitter (with `#line`) -> target C compiler (`amiga-gcc`; VBCC alternative).
- Rejected for now: C# -> IL -> custom 68k backend; LLVM m68k backend.
- Multi-target (SDL etc.) is a phase 5 concern, but nothing in the front end or platform contracts may assume a single target.
- Repo public on GitHub, private until there is something to show. Licence MIT (Apache-2.0 was the alternative); copyright Gyrafe Software. Avoid GPL/LGPL for anything linked into games.
- Phase 1 front end is a library driven by the CLI (a standalone tool). A source-generator front end stays an open question.

## Targets
Modern "Amigas" are often PowerPC (AmigaOS 4, MorphOS), so PPC is a first-class target, not a "later" one: CPU and OS are separate axes, with `AMIGA` as the umbrella symbol plus `M68K`/`PPC` and `AMIGAOS3`/`AMIGAOS4`/`MORPHOS`. See `docs/STATUS.md` for the target table. The classic 68k baseline is the **Amiga 1200 (68020)**; AROS x86-64 (`--target aros`) builds and runs on hosted AROS; planned: PowerPC runs (AmigaOS 4, MorphOS), RTG graphics cards on real hardware and AROS i386 (v2).

## Minimum OS
**Minimum system (Dave's decision): an Amiga 1200** - 68020, AGA chipset, 2 MB chip RAM, Kickstart/AmigaOS 3.1 (v40), installed on a hard disk. Consequences: classic builds use `-m68020`; platform code may assume v39+ libraries (graphics, intuition, cybergraphics detection), AGA modes, 32-bit multiply/divide in hardware, and a hard disk to load data files from; the emulator test matrix is Kickstart 3.1 on `--model A1200`. A500/A600, 68000 and Kickstart 1.x/2.x are not supported (the A500+/2.04 runs done earlier remain historical evidence, not a requirement). Not yet done for this decision: the AGA-only paths (`samples/aga` still falls back on a non-AGA machine), the chip budget default of the host fake (still 512 KB), the planar 8-bit default screens. Planned: PowerPC, RTG and AROS targets.

## Language subset
Supported: static classes/methods, structs (no reference fields), enums, small integer types, fixed-point types, fixed-size arrays and span views, `unsafe` pointers.
Slow/opt-in: long/ulong (software 64-bit), float/double (off by default, no FPU).
Later: heap classes via pools, monomorphised generics, static-dispatch interfaces.
Not supported: exceptions, LINQ, async, reflection, dynamic.
Attributes: `[EntryPoint]`, `[AmigaLib("dos", offset)]`, `[ChipRam]`, `[Interrupt]`, `[InlineAsm("...")]`.

## Platform layer
Static classes `Video`, `Audio`, `Input`, `Storage`, `Timing` with identical signatures on every target and one implementation per target, chosen with `AMIGA` / `SDL` / `HOST` conditional symbols.

## Amiga specifics
2 MB chip RAM baseline (A1200); `[ChipRam]` / `Mem.AllocChip`. The 68020 multiplies and divides 32-bit natively; 64-bit stays software (ES0100). `[AmigaLib]` stubs ideally generated from the NDK FD files. Custom registers (`$DFF000`) via a typed unsafe wrapper. v1 is system-friendly (Intuition); `Hardware.TakeOver()` later. Default `-m68020`. Big-endian assets; SDL loaders byte-swap.

## Asset pipeline
`assets.json` manifest -> quantise/dither (32 colours) -> planar bitplanes, IFF ILBM, 8-bit signed samples -> typed `Assets` class; report chip-RAM use per asset and fail the build over budget. MSBuild step and `esharp assets`.

## Roadmap
1. Hello world: ints, structs, static methods. Gate: runs in emulator and on host.
2. Data types: arrays, enums, fixed-point, spans. Gate: logic tests pass on host and Amiga.
3. Screen: Intuition window, blitter rect, asset pipeline. Gate: PNG shows on an Amiga screen.
4. Card demo: card sprites, Cardzania proof of concept. Gate: Cardzania logic playable on Amiga.
5. SDL target, plus classes and generics. Gate: same game on Amiga and SDL. (Classes and generics are already done; the SDL target is not.)
6. System bindings: write C# against the system libraries the way Xamarin / .NET iOS and Android bind their platform APIs, instead of only plain classes. Today `ESharp.Platform` is a hand-written binding of exec, dos, parts of graphics and intuition, audio.device and cybergraphics (`static extern` methods tagged `[AmigaLib]`, struct offsets measured per target). The stage generates those declarations from the library vectors (`.fd` files) and headers (constants, structs with per-target 32/64-bit layouts, tag lists, XML docs), then adds an object layer over BOOPSI, GadTools and MUI. Order: a feasibility spike on one library from the AROS headers, a licensing decision (the official NDK is restrictively licensed; AROS's headers are open), the generator, then the object wrappers. Never copy code from Amiga E (GPL). Gate: a program uses a generated binding that was never hand-written, on the emulator.
7. Editor tooling: cross-platform editing in VS Code, not a custom IDE. The analyzer is `netstandard2.0`, so the C# extension already shows the subset diagnostics (ES0001-ES0010) live. Add `tasks.json` / `launch.json` (Build and Run call `esharp build` / `esharp run`), an `esharp new` template that sets them up, and a debugging story (the host build under .NET for logic; nothing on the Amiga yet). Gate: a new project builds and runs from VS Code on Windows, macOS and Linux.

Not stages but open work: real hardware, the remaining emitter gaps, assets (dithering, IFF, timing), Windows verification and the tooling items are tracked in the GitHub project (<https://github.com/orgs/GameGyrafe/projects/5>), under the "v1" issue (#6). After the A1200 baseline is solid the "v2" epic (#5) covers AROS i386, PowerPC runs, RTG on real hardware and the language gaps that are reported as ES0099 today. `docs/STATUS.md` stays the source of truth for what is verified.

## Other context
- Amiga E compiler source is GPL: design reference only, no code reuse. Relatives: PortablE, E-VO, CreativE, ECX.
- AMOSNG may become public; possible overlap (shared platform layer, asset pipeline, AMOS-style commands as an E# library, build/emulator workflow). Unvalidated.
- Card library: shared `Card`/`Deck`/`Hand`/shuffle with suit count as a parameter (four-suit and eight-suit decks share code).

## Open questions
- Cardzania's current stack, and how entangled its rules and UI are.
- Standalone Roslyn tool or source generator for the front end.
- SDL 2 or SDL 3 (phase 5).
- Classes and interfaces in the first release, or strictly later.
