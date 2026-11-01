# Status

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
| Pinned toolchain image (`docker/Dockerfile`): m68k, AmigaOS 4 PPC, MorphOS PPC, by digest | Done |
| Hello sample builds with the real compilers for m68k, os4 and morphos (`esharp build --target X --docker`) | Done: m68k yields a valid hunk executable, os4/morphos PPC ELF |
| `esharp run` launches FS-UAE with the output mounted as DH0:; `--headless` captures output | Done: hello prints `Hello from E#!` on an emulated A500 (FS-UAE 3.1.66, built-in AROS ROM replacement) |
| Same run on Dave's Linux dev host (Ubuntu, dotnet 10.0.402, FS-UAE 3.2.35, AROS ROM replacement) | Done: prints `Hello from E#!`, exit 0 |
| Run in FS-UAE with a real Kickstart | Done on the Linux dev host (FS-UAE 3.2.35, headless): Kickstart 2.04 (37.175) on A500+ and Kickstart 3.1 (40.069) on A1200 both print `Hello from E#!`, exit 0, 5-8 s. Kickstart 1.x is out of scope (floor is 2.04) |
| Run on real hardware | **Not done** |
| Run the PowerPC builds (os4, morphos) | **Not done**: needs QEMU or hardware; `run` is m68k only |

## Next

- Run hello on real hardware, the last open item in the phase 1 gate. The emulator side is done: AROS replacement, Kickstart 2.04 and Kickstart 3.1.
- Running the PPC builds needs a PPC-capable emulator (QEMU `amigaone`/`sam460ex` for OS4, `pegasos2` for MorphOS) or real hardware.
- `esharp test` (run host tests, optionally the Amiga build in the emulator) and `esharp assets`.
- `esharp run` (launch FS-UAE/WinUAE with output mounted as a hard drive), `esharp test`.
- Phase 2: arrays, fixed-point types, span views (emitter currently reports `ES0099` for them).

## Known limits of the emitter

Reported as `ES0099` rather than miscompiled: arrays, string concatenation, instance methods,
constructors with arguments, `ref`/`out`, `foreach`, properties, `[InlineAsm]`/`[Interrupt]` bodies.

Namespaces are dropped when naming C symbols, so two types with the same name in different
namespaces would collide.

## Targets

CPU and OS are separate axes. `AMIGA` is defined for every Amiga-family target; narrow with `M68K` / `PPC` and `AMIGAOS3` / `AMIGAOS4` / `MORPHOS`.

| `--target` | Compiler | Symbols |
|---|---|---|
| `m68k` (default) | `m68k-amigaos-gcc` 6.5.0b | AMIGA M68K AMIGAOS3 |
| `os4` | `ppc-amigaos-gcc` 11.5.0 | AMIGA PPC AMIGAOS4 |
| `morphos` | `ppc-morphos-gcc` 15.2.0 | AMIGA PPC MORPHOS |

PowerPC notes: both PPC targets are big-endian, so asset formats need no change. `[ChipRam]` is a no-op on PPC (chip memory is allocated at run time there). `[InlineAsm]` will be 68k-only, so shared code needs an `#if M68K` fallback. `[AmigaLib]` offsets are 68k library vectors; the PPC targets call through the same `proto/*.h` names, so the emitter output is identical. Not yet covered: WarpOS/PowerUP (no prebuilt toolchain found) and AROS.
