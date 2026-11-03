# Status

## Lambdas that capture, and LINQ over arrays (samples/lambdas, samples/linq)

**Delegates carry an environment.** A delegate (`Func<>`, `Action<>`, `Predicate<>`, a user `delegate` type) is now a C struct of a function pointer and an environment pointer. A static method or a lambda that captures nothing has a null environment. A lambda that uses a local, a parameter or `this` gets an environment struct of pointers to those variables, built on the stack of the method that made it, so it shares the variables with the method (a write inside the lambda is seen outside, and the other way round), as in C#. Lambdas inside lambdas capture through both levels. `d == null`, `d != null` and `null` work on delegates, and `Pick()(x)` (calling a returned delegate) works.

- **Limit (by design, no heap closures):** a capturing lambda only lives as long as the method that created it. It may be passed as an argument (also down several calls) or be the initializer of a local variable. Returning it, storing it in a field, a static or an array, or assigning it to a variable declared earlier is `ES0099`. A callee that keeps a closure it was given (in a field, say) would leave a dangling pointer after the creating method returns; the compiler cannot see that. A lambda that captures an array is `ES0099` (pass it as a `Span<T>`). Local functions still may not capture.
- **LINQ** is turned into loops, for arrays and `Enumerable.Range` only. Steps: `Where`, `Select` (also the `(x, i)` index forms), `Take`, `Skip`. Terminals: `Count`, `Sum`, `Min`, `Max`, `Any`, `All`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Contains`, `Aggregate` (with and without a seed). A chain, or a query with one `from`, `where`, `let` and `select`, is one loop with no sequence in memory, so it must be consumed on the spot: by a terminal call, or by `foreach`. The lambdas are pasted into the loop, so they use the surrounding locals directly (no closure) and must be expression lambdas, static method groups or delegate variables. Everything else (`OrderBy`, `ToArray`, `ToList`, `Distinct`, `GroupBy`, `Join`, `SelectMany`, `Zip`, `Reverse`, `Average`, joins and ordering in queries, `from` twice, `group`, `into`, a chain stored in a variable, LINQ over a `Span` or `Vec`) is `ES0002` (the analyzer) or `ES0099` (the emitter). `Min`, `Max`, `First` and `Last` on an empty source give 0 or `default` instead of throwing (E# has no exceptions). `Take(n)` stops before it pulls the next element.
- Verified: `samples/lambdas` (16 checks: capture of one and two locals, writes through a capture, a captured local changed after the lambda was made, a user delegate type, method groups, a lambda in a lambda, closures made inside a loop, `null` delegates, `this`) and `samples/linq` (29 checks: every step and terminal above, index forms, structs, `Enumerable.Range`, `foreach` over a chain, queries with `let`, a chain inside a lambda) match dotnet and host gcc, and run on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE. 134 unit tests pass. Full regression with both samples added: 17 samples on both Kickstarts, 579 checks ok, no failures.
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

`Sound` (in `ESharp.Platform`) plays 8-bit samples through audio.device: up to four voices, one Paula channel each, allocated by `Sound.Open()`. Samples are generated in code (`Square`, `Triangle`, `Noise`; chip RAM, freed with `Sound.Free`), so no sound file is involved. `Play(in sample, period, volume, cycles)` uses a free voice (or the oldest), `Busy`, `Stop`, `Close`, and `PeriodFor(hz, length)` convert a pitch. On the host the calls are recorded (`Sound.HostPlays` etc.) and `Dos.Delay` lets voices finish, so game logic that plays sounds can be tested under dotnet.

- Verified: `samples/sound` prints 12 `ok` lines on Kickstart 2.04 (A500+) and 3.1 (A1200) in FS-UAE, and on the host. That shows audio.device opens, accepts writes, reports a voice busy and then free, and that Stop and Close work.
- **Not verified:** how it sounds (the headless emulator gives no audible output), real hardware, volume and period accuracy, PPC. Sample loading from files (IFF 8SVX, WAV) is not implemented.
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
| Pinned toolchain image (`docker/Dockerfile`): m68k, AmigaOS 4 PPC, MorphOS PPC, by digest | Done |
| Hello sample builds with the real compilers for m68k, os4 and morphos (`esharp build --target X --docker`) | Done: m68k yields a valid hunk executable, os4/morphos PPC ELF |
| `esharp run` launches FS-UAE with the output mounted as DH0:; `--headless` captures output | Done: hello prints `Hello from E#!` on an emulated A500 (FS-UAE 3.1.66, built-in AROS ROM replacement) |
| Same run on Dave's Linux dev host (Ubuntu, dotnet 10.0.402, FS-UAE 3.2.35, AROS ROM replacement) | Done: prints `Hello from E#!`, exit 0 |
| Run in FS-UAE with a real Kickstart | Done on the Linux dev host (FS-UAE 3.2.35, headless): Kickstart 2.04 (37.175) on A500+ and Kickstart 3.1 (40.069) on A1200 both print `Hello from E#!`, exit 0, 5-8 s. Kickstart 1.x is out of scope (floor is 2.04) |
| Run on real hardware | **Not done** |
| Run the PowerPC builds (os4, morphos) | **Not done**: needs QEMU or hardware; `run` is m68k only |

## Phase 2: data types (done on host and both Kickstarts; PPC not run)

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
| Generics: generic structs and generic static methods, monomorphised | Done as a source-to-source pass before emission (`Monomorphizer`): each use (`Pair<int>`, `Pick<Fix>(...)`, inferred `Pick(true, 1, 2)`, nested `Pair<Pair<int>>`) is renamed (`Pair__int`) and a copy of the declaration with the type arguments substituted is added next to the original; the pass repeats until nothing generic is left (limit 16 rounds, ES0098). Copies carry `#line` so errors and the C `#line` output point at the generic source. Constraints are accepted and ignored. A library generic (such as `Pool<T>`) needs publicly accessible type arguments, otherwise Roslyn reports CS0122 in the library copy. Generic classes (ES0008), generic methods from outside E# source, generics inside generic types' methods called on other generic methods of a generic type, and generic nested types of generic types are not supported. Tested on host, host gcc and m68k, see `samples/generics` |
| `Pool<T>` (typed fixed-capacity pool, `ESharp.Platform`) | Done: O(1) `Alloc` (zeroed) / `Free` (returns false for foreign, misaligned or double-freed pointers, in every build), `Reset`, `Destroy`; freed slots are filled with 0xDD with `--debug`. `samples/generics` has 24 checks, same output on dotnet (no leaks), host gcc and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), with and without `--debug` |
| Properties on structs and static classes | Done. Auto-properties (`{ get; set; }`, get-only set in a constructor) are plain fields (instance) or static variables; properties with accessor bodies or `=>` become functions `T_get_X(&v)` / `T_set_X(&v, value)` (static ones take no receiver). Reads, assignment, compound assignment (including user-defined operators such as `Fix +=`) and `++`/`--` work through variables, pointers, array elements, `this`, and inside generic structs. A write to a property with accessor bodies must be a statement (its value is not kept): `x = (s.P = 3)` is `ES0099`, as is compound assignment on a receiver that is not a plain name/member/element chain. Indexers, object initializers that set a property with accessor bodies, and initializers on instance auto-properties are still `ES0099`. `samples/properties`: 18 checks, same output on dotnet, host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200), with and without `--debug` |
| Chip RAM budget report (`esharp build --chip-budget 256k`) | Done for constant sizes, 68k only: sums static `[ChipRam]` data, `new Arena(n, true)`, `new Pool<T>(n, true)` and `Mem.AllocChip(n)`, prints each with its source line and the total, and fails with `ES0030` over the budget. Struct sizes use m68k-amigaos-gcc layout (alignment capped at 2), checked against the compiler's `sizeof` for two structs. Each call site counts once, however often it runs; sizes computed at run time are listed as such and not summed; reservations inside generic code that are not instantiated with constants are not seen. PowerPC targets report nothing. Not checked against a real run's `AvailMem` |
| Implicit user-defined conversions and indexers on structs | Done. `operator implicit` is applied wherever C# applies it (initializer, assignment, argument, return, operand) by calling the conversion function; `Fix` now converts implicitly from `int` (`Fix f = 3; f + 2`). Indexers (`this[int i]`, several indices, overloads, expression-bodied, in generic structs) are accessor functions like properties: `g[3]` is `Grid_get_Item(&g, 3)`, writes, `+=` and `++` are statements calling the setter. Same limits as properties (a write used as a value, or compound on a call-result receiver, is `ES0099`). `samples/operators`: 20 checks on dotnet, host gcc, and m68k on Kickstart 2.04 (A500+) and 3.1 (A1200) |
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
| Asset pipeline: `assets.json` + PNG -> `Assets.g.cs` (`esharp assets <dir>`) | Done: own PNG reader (no dependencies), rounds to 12-bit colour, exact mapping when it fits the colour budget (2..32) or median cut otherwise, planar chip data (`[ChipRam]`, rows padded to 16 pixels) and `NamePalette`/`NameWidth`/`NameHeight`/`NameDepth`. Transparent pixels become entry 0 (black), and `"mask": true` also writes `NameMask`, a one-plane mask (1 = opaque) for masked blits. **No dithering**, no IFF ILBM or samples yet, and no MSBuild step: run `esharp assets` yourself and commit `Assets.g.cs` (a test fails if it is stale). The chip budget report already counts the planes |
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
| Patterns: constant, relational, `and`/`or`/`not`, `var`, declaration, property patterns on fields, `when`; `is` and `switch` expressions | Done (GNU statement expressions in the C; gcc only, which is all we target). No match in a switch expression traps instead of throwing. Positional/list patterns, type tests and property patterns on real properties are `ES0099`. The `switch` *statement* is unchanged |
| Local functions (no capture) | Done: a local function that uses a local, parameter or `this` of the method around it is `ES0099` |
| Static constructors, `: this(...)` constructor initializers (structs) | Done. **Static constructors run once at start-up in type order**, not lazily like in C#. Before this fix a static constructor was silently ignored |
| `Vec<T>` (`ESharp.Platform`, named so as not to clash with `System.Collections.Generic.List<T>`): growable list of an unmanaged `T` | Done: `Add`, `Insert`, `RemoveAt`, `SwapRemoveAt`, `Clear`, indexer, `Count`, `Capacity`, `AsSpan`, `Destroy`; no exceptions (false / zero value). Counted in the chip budget by its initial capacity only |
| `Dos.PutInt` | Done |
| String interpolation and concatenation (`$"score {n,5:D3}"`, `"a" + n`) | Done: the text is formatted into one of 8 static 128-byte buffers and returned as a `string`, so it is **valid only until 8 more strings have been formatted** (use it at once: draw it, print it; copy it if it must stay) and is cut at 127 bytes. Holes: integers up to 32 bits (alignment `,N`/`,-N`, formats `D<n>`, `X<n>`, `x<n>`), `char`, `bool` (`True`/`False` like C#), `string`; `{{` and `}}` work; constant strings are folded. Not done (ES0099): `long`/`ulong`, floats and fixed-point, enums, other format strings, `string.Format`, `StringBuilder`. Parity with dotnet is checked in `samples/language` |
| Heap classes (non-static `class`) | Done: fields, auto-properties, methods, constructors, static members, `new` (also with an object initializer), `null`, reference equality, arrays of references, `this`. A class variable is a C pointer (`Foo *`), `new` is `calloc` (stops with exit 20 when memory runs out), and objects are freed with `Heap.Free(ref obj)` (frees and sets the variable to null); objects never freed are released when the program ends. There is no garbage collector: keep one owner per object. **Single inheritance with `virtual`/`abstract`/`override` (see the inheritance section above), no generic classes (ES0008), no instance field initializers in classes (ES0099, assign in the constructor)**. Class names live in C's single namespace, so a name that is also in the NDK headers (`Node`, `List`, `Window`, ...) fails in gcc: pick another. Two E# types with the same name in different namespaces are reported (ES0099) |
| Interfaces | Plain-method interfaces (no properties, events, generic methods or default implementations). Two ways to use them: as generic constraints (`where T : IScorer`, monomorphised, any struct or class, static dispatch), and as **variable types** (`IRules rules = new FlatRules();`, fields, parameters, returns, statics): an interface value is a C struct of the object pointer and a table of functions, so the call is dynamic. Only **classes** can be converted to an interface value (a struct would need boxing: ES0099). `x == null`, `x is null`, `default`, equality (same object) and `Heap.Free(ref x)` (frees the object) work. Not done: interface-to-interface conversion, inheritance between interfaces, explicit casts back to the class (`as`/`is Foo`) |
| Lambdas, method groups and delegates (`Func<>`, `Action<>`, `Predicate<>`, user `delegate` types) | Done: a delegate is a function pointer plus an environment; lambdas may capture locals, parameters and `this` (by reference, on the stack: they cannot be returned or stored, see the top of this file). Calls `f(x)` and `f.Invoke(x)`, `null` and `== null` work. Not done: delegates as struct fields, `?.Invoke`, `delegate { }` anonymous methods, instance-method groups, combining delegates (`+=`), local functions that capture |
| LINQ | Over arrays and `Enumerable.Range`, as fused loops: Where, Select, Take, Skip, Count, Sum, Min, Max, Any, All, First, Last, Contains, Aggregate and simple queries (see the top of this file). No sorting, no `ToArray`/`ToList`, no sequences stored in variables |
| Still missing (needed by bigger games) | Closures that outlive their method, `OrderBy`/`ToArray`/`ToList`, `long`/enum/float values in strings, `is`/`as` against interfaces, inheritance from generic or library classes |

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
| Not verified | Keyboard other than Esc, mouse moves (`MOUSEMOVE` is read but only the position is used), real mouse hardware, PPC targets, 1.x-style left-handed button swaps, timing/frame counters beyond `Delay`, dithering, IFF ILBM, how samples/sound sounds |

## Next

- Run hello on real hardware, the last open item in the phase 1 gate. The emulator side is done: AROS replacement, Kickstart 2.04 and Kickstart 3.1.
- Running the PPC builds needs a PPC-capable emulator (QEMU `amigaone`/`sam460ex` for OS4, `pegasos2` for MorphOS) or real hardware.
- `esharp test` (run host tests, optionally the Amiga build in the emulator).
- Finish phase 2: run the samples on the PPC targets once there is a way to run them.
- Memory management ([MEMORY.md](MEMORY.md)): step 1 (`Mem.AllocChip`/`AllocFast`/`Free`, `Arena`) is done; step 2 (`Pool<T>`) and most of step 3 (`--debug`: bounds checks, arena and pool poisoning) are done; the chip budget report (step 4) is done for constant sizes.
- Phase 4 (rest): a bigger card game (rummy-style: melds, bots), a shared card library, timing, dithering and IFF/sample assets, an MSBuild step for `assets.json`.

## Known limits of the emitter

Reported as `ES0099` rather than miscompiled: capturing lambdas that would outlive their method, delegates in struct fields, `is`/`as` against an interface or struct, `ref` locals and returns, `foreach` over anything but arrays and spans, positional/list patterns, `[InlineAsm]`/`[Interrupt]` bodies.

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
