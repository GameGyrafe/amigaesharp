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

## Phase 2: data types (in progress)

Gate: logic tests pass on host and Amiga. `samples/phase2` prints one `ok NAME` line per check and
must print the same 32 lines everywhere.

| Item | State |
|---|---|
| Fixed-size arrays: `T[] a = new T[N]`, `= { ... }`, `new[] { ... }` as locals and static fields; `Length`, indexing, `foreach` | Done: emitted as C arrays (`T a[N] = {0}`); `[ChipRam]` works on static arrays |
| `Span<T>` / `ReadOnlySpan<T>`: indexing, `Length`, `Slice`, `foreach`, implicit array -> span and span -> read-only span | Done: emitted as `{ ptr, length }` structs; arrays are passed to methods as spans |
| Inline arrays in structs (`unsafe struct` with `fixed byte X[8]`) | Done: C array member; indexed through a pointer, no span conversion |
| Subset rule `ES0011`: no heap, jagged, multi-dimensional, reassigned or non-constant-length arrays; no array parameters or return types | Done |
| Operators on structs (static `operator` methods, explicit conversions) and static methods in structs | Done: become C functions (`Fix_op_Multiply_Fix_Fix`) |
| Instance methods and constructors with arguments on structs | Done: a method is a C function taking `T *_this` first (`c.Bump(2)` -> `Card_Bump(&c, 2)`); a constructor returns the struct (`new Card(5, 1)` -> `Card_new(5, 1)`), with overloads, early `return`, `this.X` and unqualified calls. The receiver must be a variable, field, array/span element or pointer, not a call result |
| `Fix`: 16.16 fixed point (`+ - * /`, comparisons, `FromInt/FromRaw/ToInt/Frac`) in `ESharp.Runtime` | Done. Multiply is four unsigned 16x16 partial products: checked in the m68k-amigaos-gcc 6.5.0b `-m68000 -O2` assembly, it is four `mulu.w` and no `__mulsi3` call (a first signed version did call it), and it matches a 64-bit reference over about 6000 random and edge pairs. Divide by `Fix` uses a 64-bit intermediate (`__divdi3`), so it is slow on 68000. Cycle counts are **not measured** |
| Host: sample runs under dotnet and as generated C with host gcc (`-Wall -Werror -fwrapv`) | Done, in tests |
| m68k: sample built with m68k-amigaos-gcc 6.5.0b and run in FS-UAE 3.2.35 | Done: 32/32 ok on Kickstart 2.04 (37.175, A500+) and Kickstart 3.1 (40.069, A1200) |
| os4 / morphos builds and runs of the phase 2 sample | **Not verified** (the PPC image was not built or run this session) |
| Real hardware | **Not done** |

Notes and limits:
- Bounds checking is off by default: indexing out of range is undefined in a normal build. `esharp build --debug` (or `CompileOptions.BoundsChecks`) checks every array and span index (`ES_IDX`), prints `file(line): index N out of range 0..M` to stderr and exits with 20; `--debug` also defines `DEBUG`, which makes `Arena.Reset` fill with 0xDD. Raw pointer indexing is never checked. Verified: host gcc tests, and the memory and phase2 samples built with `--debug` pass on Kickstart 2.04 (A500+) and 3.1 (A1200). Not verified: where the stderr message appears on a real Amiga (a program started from the Workbench may show nothing), and the cost on a 68000.
- Overflow wraps: all targets now compile with `-fwrapv` to match unchecked C#.
- Arrays have no heap form; a `Span<T>` is the only way to hand one to another method. Spans cannot be stored in fields (C# forbids it for ref structs).
- `Slice`, `foreach` and `Length` on a span or array need a plain name or member access, not a call.
- Only `Fix` (16.16) exists; an 8.8 `short` type and a `Fix` square root/trig table are not written.
- Properties, implicit user conversions, constructor initializers (`: this(...)`), static constructors, calls on temporaries (`Make().Get()`) and a constructor combined with an object initializer are still `ES0099`.

## Memory (step 1 done)

| Item | State |
|---|---|
| `Mem.AllocChip/AllocFast/Free/AvailChip/AvailFast`, `Arena` (design in MEMORY.md) | Done: exec `AllocVec`/`FreeVec`/`AvailMem`; host fake with a 512 KB chip budget and leak tracking |
| `sizeof(T)` and `new Span<T>(pointer, length)` in the emitter | Done |
| `samples/memory`: 19 checks | Done: same output on dotnet (no leaks), host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), including a check that chip blocks are below 2 MB |
| AmigaOS 4 / MorphOS allocation calls, fast-RAM paths | **Not verified** |

## Next

- Run hello on real hardware, the last open item in the phase 1 gate. The emulator side is done: AROS replacement, Kickstart 2.04 and Kickstart 3.1.
- Running the PPC builds needs a PPC-capable emulator (QEMU `amigaone`/`sam460ex` for OS4, `pegasos2` for MorphOS) or real hardware.
- `esharp test` (run host tests, optionally the Amiga build in the emulator) and `esharp assets`.
- `esharp run` (launch FS-UAE/WinUAE with output mounted as a hard drive), `esharp test`.
- Finish phase 2: run the sample on the PPC targets once there is a way to run them; decide on optional bounds checks; properties.
- Memory management ([MEMORY.md](MEMORY.md)): step 1 (`Mem.AllocChip`/`AllocFast`/`Free`, `Arena`) is done; step 3 is partly done (`--debug`: bounds checks, arena poisoning); next are pools (after generics), pool poisoning and double-free checks, and the chip budget report.
- Phase 3: Intuition window, blitter rect, asset pipeline.

## Known limits of the emitter

Reported as `ES0099` rather than miscompiled: string concatenation, `ref`/`out`, `foreach` over anything but arrays and spans, properties, `[InlineAsm]`/`[Interrupt]` bodies.

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
