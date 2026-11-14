# Memory management (proposal)

**Status: approved by the maintainer on 2026-11-01 "go with the recommendations"; step 1 (Mem + Arena) is implemented and verified; steps 2-4 are not.** It follows the design document
(`docs/design/E_Design_Document.pdf`): no garbage collector, "explicit pools or arenas" for heap
objects, `[ChipRam]` and `Mem.AllocChip` for chip memory, and chip use reported against a budget.

## Principles

1. **No GC, no hidden allocation.** Nothing in the language allocates behind your back; every
   byte of dynamic memory comes from a call you wrote.
2. **Static and stack first.** Fixed-size arrays (phase 2) cover most of a card game. Only reach
   for the heap for data whose size is known at start-up, not compile time.
3. **Allocate big, rarely, up front.** One `AllocVec` per arena or pool at load time; free at exit
   or at a level change. No fragmentation, no per-object system calls.
4. **Memory kind is explicit.** Anything the custom chips read (bitmaps, samples, copper lists)
   is chip RAM on 68k. On PowerPC chip RAM is not a thing, so the kind is accepted and ignored
   (as `[ChipRam]` already is).
5. **Failure is a null return, not an exception.** E# has no exceptions. Callers check once at
   start-up. There is no out-of-memory trap in release builds.
6. **Same API on every target and the host.** Host fakes behave like a small Amiga so tests
   catch overruns under dotnet.

## Step 1: raw memory and arenas (implemented)

Platform layer (`ESharp.Platform`), same pattern as `Dos`: Amiga implementation under `#if AMIGA`,
managed fake otherwise.

```csharp
public static unsafe class Mem
{
    public static byte* AllocChip(uint size);   // chip RAM on 68k, zeroed, null on failure
    public static byte* AllocFast(uint size);   // fast RAM if there is any, else chip; zeroed
    public static void  Free(byte* block);      // either kind
    public static uint  AvailChip();            // bytes free
    public static uint  AvailFast();
}

public unsafe struct Arena
{
    public Arena(uint size, bool chip);         // one up-front allocation
    public byte* Alloc(uint size, uint align);  // bump pointer, null if full
    public uint  Remaining();
    public void  Reset();                       // free everything in the arena at once
    public void  Destroy();                     // return the block to the system
}
```

Amiga binding (all v33 or older, so fine for the Kickstart 3.1 floor):

| E# | exec call | flags |
|---|---|---|
| `AllocChip` | `AllocVec(size, ...)` | `MEMF_PUBLIC \| MEMF_CHIP \| MEMF_CLEAR` |
| `AllocFast` | `AllocVec(size, ...)` | `MEMF_PUBLIC \| MEMF_CLEAR` (exec prefers fast and falls back to chip, so this works on an unexpanded A1200) |
| `Free` | `FreeVec(block)` | the size is stored with the block |
| `AvailChip` / `AvailFast` | `AvailMem(...)` | `MEMF_CHIP` / `MEMF_FAST` |

`AllocFast` is named for intent, not a guarantee. A stock A1200 has no fast RAM, so a `MEMF_FAST`-only
request would simply fail there.

PowerPC: `AllocChip` and `AllocFast` are the same call. The AmigaOS 4 and MorphOS spellings are
**not verified**; AmigaOS 4 prefers `AllocVecTags`, and I would check that `AllocVec` is still
exported before relying on it.

### Compiler work (done)

- `sizeof(T)` for structs and primitives (not supported by the emitter today) -> C `sizeof`.
- `new Span<T>(T* pointer, int length)` -> `{ pointer, length }`, so arena memory can be handed
  around as spans like any other array.
- Pointer casts from `byte*` to `Card*` already work.
- `[AmigaLib]` stubs for `AllocVec`, `FreeVec`, `AvailMem` (returning `byte*`; the generated call
  goes through `proto/exec.h`, which the 68k and PPC toolchains both provide).

### Host fakes

- `Mem` on the host uses `NativeMemory`, zeroed, with a configurable **chip budget** (default
  512 KB) and a **fast budget**. Over budget returns null, exactly as a real machine would.
- `ESharp.Target.Host` gets a `HostMem` helper for tests: set budgets, read the bytes still
  allocated, and assert nothing leaked at the end of a test.

### Tests

- Host (xUnit, `samples/memory`): alignment, exhaustion returns null, `Reset` reuses space, double `Free` (the host fake throws; on a real Amiga it would corrupt the free list), chip budget enforced, leak check.
- Generated C against a `proto/exec.h` shim (calloc/free), as the `dos.h` shim does today.
- Amiga: the sample allocates a chip arena and checks the address is in chip RAM (below 2 MB on
  A1200), writes and reads a pattern, then frees and checks `AvailChip` returned to
  its starting value. FS-UAE's default has no fast RAM, so fast-RAM paths need a config option
  (not done); until then `AllocFast` is only verified to fall back to chip.

## Step 2: pools

Fixed-capacity pools for many same-sized objects (cards, sprites, particles).

```csharp
public unsafe struct Pool
{
    public Pool(uint itemSize, uint capacity, bool chip);
    public byte* Alloc();              // O(1), null when full
    public void  Free(byte* item);
    public uint  InUse();
}
```

**Done** as `Pool<T> where T : unmanaged` (generics are monomorphised, see STATUS.md): `new Pool<Card>(capacity, chip)`,
`T* Alloc()` (zeroed, null when full), `bool Free(T*)` (false for a foreign, misaligned or already free pointer; this
check is on in every build), `Reset()`, `Destroy()`, `InUse()`, `Capacity()`. Layout is one block: free list of slot numbers,
items, one in-use byte per slot. With `--debug` freed slots are filled with 0xDD.

## Step 3: debug checks (build with `--debug`; arena and pool poisoning and bounds checks done)

- Freed pool slots are filled with `0xDD`; arena `Reset` fills with `0xDD`.
- A pool `Free` of an item that is not in the pool, or is already free, is an error.
- Bounds checks on arrays and spans (see the open question in `docs/STATUS.md`) share the same
  switch, with a `#line` location in the failure message.
- These cost nothing in release builds.

## Step 4: budgets in the build (done: `esharp build --chip-budget`, constant sizes only, see STATUS.md)

- Compile-time: sum the size of static `[ChipRam]` arrays and report it; fail over a budget given
  on the command line (design document: "a budget overrun fails the build rather than the
  boot").
- Arena and pool sizes that are constants can be added to the same report. Sizes computed at run
  time cannot, and the report will say so.

## Heap classes

Out of scope here. The design document puts heap classes in phase 2 "via explicit pools or arenas".
Once pools and generics exist, a class could be a handle into a pool, or classes may turn out not to
be needed at all. This proposal does not decide that.

## Decisions (the maintainer approved the recommended answers, still revisable)

1. **Names:** `Mem.AllocChip` / `Mem.AllocFast`, matching the design document.
2. **Failure:** null return only. No `OnFail` hook for now.
3. **Alignment:** `Arena.Alloc` takes an explicit alignment (a power of two); no hidden default.
4. **Pools:** typed `Pool<T>` after generics (done).
5. **Budget:** the host fake defaults to 2 MB chip (a stock A1200) and is configurable.

## What step 1 actually does today

- `Mem`, `Arena`, `Exec` live in `src/ESharp.Platform`; `HostMem` in `src/ESharp.Target.Host`.
- The host fake defaults to 2 MB chip and **0 fast** (a stock A1200, so `AllocFast` uses chip);
  `HostMem.Capture(chip, fast)` sets budgets and `LiveBlocks`/`ChipUsed` detect leaks.
- `Mem.Free` on the host throws for an unknown or already-freed block. On a real Amiga that would
  corrupt memory, so the fake is stricter on purpose.
- `Arena` is bump-pointer with explicit alignment; `Reset` does not clear in a normal build; with `DEBUG` (`esharp build --debug`) it fills the used part with 0xDD.
- `samples/memory` has 19 checks and runs identically under dotnet (no leaks), host gcc (against an
  exec shim) and on m68k, where it also asserts the chip block address is below 2 MB.
- **Not verified:** AmigaOS 4 and MorphOS (`AllocVec` availability), fast RAM paths (FS-UAE has no
  fast RAM in the default config), and large allocations near the limit of a real machine.
