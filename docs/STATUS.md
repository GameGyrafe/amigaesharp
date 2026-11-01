# Status

## Phase 1: Hello world (in progress)

Gate: runs in an emulator and on the host.

| Item | State |
|---|---|
| Repo scaffold (solution, projects, README crediting Amiga E) | Done |
| Roslyn front end + subset analyzer (`ES0001`-`ES0010`, `ES0100`) | Done |
| C emitter: static classes/methods, ints, structs, enums, statics, control flow, `[AmigaLib]` calls, `#line` | Done |
| `[EntryPoint]` -> `main()` | Done |
| `esharp new` / `esharp build` | Done (build needs `--cc` or amiga-gcc to go past C emission) |
| .NET test host + xUnit | Done (`HostConsole` captures `Dos.PutStr`) |
| Generated C compiled and run natively (host gcc + `proto/dos.h` shim) | Done, in tests |
| Build with real `m68k-amigaos-gcc` | **Not verified**: toolchain not available in the dev container |
| Run in FS-UAE | **Not done**: needs the pinned toolchain container image |

## Next

- Pinned amiga-gcc container image, then verify the hello sample builds and runs in FS-UAE (closes the phase 1 gate).
- `esharp run` (launch FS-UAE/WinUAE with output mounted as a hard drive), `esharp test`.
- Phase 2: arrays, fixed-point types, span views (emitter currently reports `ES0099` for them).

## Known limits of the emitter

Reported as `ES0099` rather than miscompiled: arrays, string concatenation, instance methods,
constructors with arguments, `ref`/`out`, `foreach`, properties, `[InlineAsm]`/`[Interrupt]` bodies.

Namespaces are dropped when naming C symbols, so two types with the same name in different
namespaces would collide.
