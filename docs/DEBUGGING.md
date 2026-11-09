# Debugging E# programs

Two tools for the cases where there is no debugger: a serial trace, and a way to turn a crash address into a C# line.

## Serial trace (`Trace`)

```csharp
Trace.Line("loading level");          // text and a line feed
Trace.Write("score: ");               // text only
Trace.Value("lives", lives);          // lives=3
```

- **Build switch.** The calls exist only in a build with `--trace` (defines `TRACE`) or `--debug` (which implies `TRACE`).
  Without them the compiler drops each call statement, arguments included, and the `Trace` code is not emitted: a release
  build pays nothing (one unused flag byte in bss). Under plain dotnet `TRACE` is defined by default, so the host test run traces.
  Arguments of a dropped call are not evaluated, so keep side effects out of them.
- **On the Amiga** the text goes out through exec `RawPutChar` (LVO -516, `RawIOInit` -504 the first time), which writes
  straight to the serial hardware. Kickstart 3.1 has it, no `debug.lib` needed. Line feeds are sent as is (no carriage return).
  On a real machine use a terminal at 9600 baud (what `RawIOInit` sets). The vectors are in no NDK header, so the C prelude calls them
  directly (m68k) or through `proto/exec.h` (AROS). os4 and MorphOS have no serial trace yet (the calls compile to nothing).
- **On the host** the text goes to standard error (`Trace.Out`, which tests can replace).
- **In FS-UAE:** `esharp run samples/trace --trace --headless --serial trace.txt` points the emulator's serial port at a file
  (`serial_port = <file>` in the generated config) and prints the file after the run, on standard error, below the program output.
- **On hosted AROS:** `RawPutChar` reaches the bootstrap's output; `tools/aros-hosted/aros-hosted.sh trace --log` prints it.

## From a crash address to a C# line (`esharp addr`)

```
esharp addr <exe | project dir> <hunk> <hex offset> [--docker [image]]
esharp addr samples/trace 0 e4 --docker <m68k image>
```

```
hunk 0 offset 0xe4
  samples/trace/TraceDemo.cs:14
  C function Es_TraceSample_Program_Square
     14: {
```

How it works, and what was checked:

- An m68k build is a hunk executable with three hunks: 0 code (`.text`), 1 data, 2 bss. The exe is linked with its sections starting at 0,
  so a hunk-0 offset is the same number as the address `m68k-amigaos-objdump -t <exe>` prints. Guru reports, Enforcer, SegTracker
  and MuForce give "hunk N offset X" (hex); that is what `esharp addr` takes (`0 e4`, `0:e4` and `0:0xe4` all work).
- The hunk exe's own DWARF (built with `-g`) is not readable by the toolchain's `addr2line` or `objdump -l`. The compiler's `-g` object
  is. So every m68k `esharp build` also writes `<name>.o` next to the exe: the same C compiled with `-g -c` (same code as in the exe).
  `esharp addr` runs `m68k-amigaos-objdump --dwarf=decodedline` on it, which gives the `.cs` line table because the generated C
  carries `#line` directives. The exe stays as small as before.
- The object's code sits at a fixed distance inside hunk 0; `esharp addr` finds it by comparing where the exe and the object put `_main`.
- The C function comes from the object's symbol table.
- It needs `m68k-amigaos-objdump`: pass `--docker <image>` (the m68k image of AGENTS.md) or have the toolchain on PATH.
- Limits: only user code has `#line`. An address in E# platform/runtime code (`Es_ESharp_*` functions) is reported as such, with no
  source line (look at the caller). Startup and library code, and the generated `main()` wrapper, are reported as not the program's own code.
  Hunk 1 and 2 give the nearest symbol. Optimised code can attribute an address to a neighbouring statement.
  The map is valid for the exe built together with that `.o` (rebuild changes the offsets).
- Checked: a test (`TraceAddrTests`, runs when the pinned m68k image is in the local docker) builds `samples/trace`, takes the addresses of
  `Square` and `Main` from the exe's symbols and asserts `esharp addr` gives lines in the right function. Not checked: an address
  taken from a real crash on a running Amiga (see below).

### What a crash shows on the emulator

A divide by zero (`a / b` with `b == 0`) on Kickstart 3.1 in FS-UAE shows the "Software Failure" requester:
`dh0:crash  Program failed (error #80000005)`. That is all the base OS tells you: the error number (80000005 is the 68k zero-divide
trap) and the program name, not the PC. A null write does not crash on an A1200 (no MMU). To get a hunk and offset you need a tool
that reports the faulting PC: SegTracker (it prints hunk and offset), with Enforcer or MuForce on MMU machines, none of which is part of
AmigaOS (not tried here). Getting the PC out of an emulator debugger was not tried either.
Without a PC the serial trace is the way: trace before the suspect call and see how far it got.
