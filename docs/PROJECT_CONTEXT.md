# E# project context (handoff)

Condensed from the design document and the original planning chat. Treat `docs/STATUS.md` as the source of truth for progress.

## Decisions
- Approach: Roslyn parse -> subset analyzer -> lowering -> C emitter (with `#line`) -> target C compiler (`amiga-gcc`; VBCC alternative).
- Rejected for now: C# -> IL -> custom 68k backend; LLVM m68k backend.
- Multi-target (SDL etc.) is a phase 5 concern, but nothing in the front end or platform contracts may assume a single target.
- Repo public on GitHub, private until there is something to show. Licence MIT (Apache-2.0 was the alternative); copyright Gyrafe Software. Avoid GPL/LGPL for anything linked into games.
- Phase 1 front end is a library driven by the CLI (a standalone tool). A source-generator front end stays an open question.

## Targets
Modern "Amigas" are often PowerPC (AmigaOS 4, MorphOS), so PPC is a first-class target, not a "later" one: CPU and OS are separate axes, with `AMIGA` as the umbrella symbol plus `M68K`/`PPC` and `AMIGAOS3`/`AMIGAOS4`/`MORPHOS`. See `docs/STATUS.md` for the target table. The 68000 baseline stays the default and the performance-constrained one.

## Language subset
Supported: static classes/methods, structs (no reference fields), enums, small integer types, fixed-point types, fixed-size arrays and span views, `unsafe` pointers.
Slow/opt-in: long/ulong (software 64-bit), float/double (off by default, no FPU).
Later: heap classes via pools, monomorphised generics, static-dispatch interfaces.
Not supported: exceptions, LINQ, async, reflection, dynamic.
Attributes: `[EntryPoint]`, `[AmigaLib("dos", offset)]`, `[ChipRam]`, `[Interrupt]`, `[InlineAsm("...")]`.

## Platform layer
Static classes `Video`, `Audio`, `Input`, `Storage`, `Timing` with identical signatures on every target and one implementation per target, chosen with `AMIGA` / `SDL` / `HOST` conditional symbols.

## Amiga specifics
512KB chip RAM baseline; `[ChipRam]` / `Mem.AllocChip`. 68000 multiplies 16x16 and divides 32/16 natively, so use fixed-point and warn on 32-bit mul/div in hot loops. `[AmigaLib]` stubs ideally generated from the NDK FD files. Custom registers (`$DFF000`) via a typed unsafe wrapper. v1 is system-friendly (Intuition); `Hardware.TakeOver()` later. Default `-m68000`; 68020+ opt-in. Big-endian assets; SDL loaders byte-swap.

## Asset pipeline
`assets.json` manifest -> quantise/dither (32 colours) -> planar bitplanes, IFF ILBM, 8-bit signed samples -> typed `Assets` class; report chip-RAM use per asset and fail the build over budget. MSBuild step and `esharp assets`.

## Roadmap
1. Hello world: ints, structs, static methods. Gate: runs in emulator and on host.
2. Data types: arrays, enums, fixed-point, spans. Gate: logic tests pass on host and Amiga.
3. Screen: Intuition window, blitter rect, asset pipeline. Gate: PNG shows on an Amiga screen.
4. Card demo: card sprites, Cardzania proof of concept. Gate: Cardzania logic playable on Amiga.
5. SDL target, plus classes and generics. Gate: same game on Amiga and SDL.

## Other context
- Amiga E compiler source is GPL: design reference only, no code reuse. Relatives: PortablE, E-VO, CreativE, ECX.
- AMOSNG may become public; possible overlap (shared platform layer, asset pipeline, AMOS-style commands as an E# library, build/emulator workflow). Unvalidated.
- Card library: shared `Card`/`Deck`/`Hand`/shuffle with suit count as a parameter (four-suit and eight-suit decks share code).

## Open questions
- Cardzania's current stack, and how entangled its rules and UI are.
- Standalone Roslyn tool or source generator for the front end.
- SDL 2 or SDL 3 (phase 5).
- Classes and interfaces in the first release, or strictly later.
