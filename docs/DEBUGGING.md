# Debugging E# programs

Three ways to look inside a program on the Amiga side (logic bugs are easiest to debug on the host, in VS Code): a serial trace, a way to turn a crash address (or a `Trace.Where` line) into a C# line, and, for questions about registers and memory, FS-UAE's built-in debugger (a manual recipe, see the last section).

## Serial trace (`Trace`)

```csharp
Trace.Line("loading level");          // text and a line feed
Trace.Write("score: ");               // text only
Trace.Value("lives", lives);          // lives=3
Trace.Where("loading");               // loading: hunk 0 offset 0x912 (see esharp addr below)
```

- **Build switch.** The calls exist only in a build with `--trace` (defines `TRACE`) or `--debug` (which implies `TRACE`).
  Without them the compiler drops each call statement, arguments included, and the `Trace` code is not emitted: a release
  build pays nothing (one unused flag byte in bss). Under plain dotnet `TRACE` is defined by default, so the host test run traces.
  Arguments of a dropped call are not evaluated, so keep side effects out of them.
- **On the Amiga** the text goes out through exec `RawPutChar` (LVO -516, `RawIOInit` -504 the first time), which writes
  straight to the serial hardware. Kickstart 3.1 has it, no `debug.lib` needed. Line feeds are sent as is (no carriage return).
  On a real machine use a terminal at 9600 baud (what `RawIOInit` sets). The vectors are in no NDK header, so the C prelude calls them
  directly (m68k) or through `proto/exec.h` (AROS). AmigaOS 4 sends the text through exec `DebugPrintF` to the kernel debug output
  (the serial port with the kernel option `serial`, as the os4 VM runs use it: `esharp run --target os4 --trace --serial f` shows
  the lines). MorphOS has no serial trace yet (the calls compile to nothing).
- **On the host** the text goes to standard error (`Trace.Out`, which tests can replace).
- **In FS-UAE:** `esharp run samples/trace --trace --headless --serial trace.txt` points the emulator's serial port at a file
  (`serial_port = <file>` in the generated config) and prints the file after the run, on standard error, below the program output.
- **On hosted AROS:** `RawPutChar` reaches the bootstrap's output; `tools/aros-hosted/aros-hosted.sh trace --log` prints it.

## From a crash address to a C# line (`esharp addr`)

```
esharp addr <exe | project dir> [<where>] [--target <t>] [--docker [image] | --toolchain <dir>]
esharp addr samples/crash 0 99a --docker <m68k image>                                   # m68k: hunk and hex offset
esharp addr samples/crash 3 4 --docker <m68k image>                                     # a chip data hunk
esharp addr samples/crash --target aros "Segment 1 .text (0x41A720C0) Offset 0xD15" --docker   # pasted from the AROS alert
esharp addr samples/crash --target os4 "Es_CrashSample_Program_Main()+0x108" --docker           # pasted from the Grim Reaper
esharp addr samples/crash                                                               # no address: list the hunks or sections
```

```
hunk 0 offset 0x99a
  hunk 0: code, 5692 bytes
  samples/crash/Crash.cs:38
  C function Es_CrashSample_Program_Divide (inlined into Es_CrashSample_Program_Main)
     38: Trace.Where("divide");
hunk 3 offset 0x4
  hunk 3: data, chip RAM, 16 bytes
  in the program's chip data: Es_CrashSample_Program_Pattern + 0x4
section 1 .text offset 0xd15
  .text: code, 9698 bytes
  samples/crash/Crash.cs:39
  C function Es_CrashSample_Program_Divide (inlined into Es_CrashSample_Program_Main at samples/crash/Crash.cs:60)
     39: return a / b;
```

`<where>` is what the crash report gives:

| Target | Report | Forms `esharp addr` takes |
|---|---|---|
| m68k | SegTracker, Enforcer, MuForce: "hunk N offset X"; `Trace.Where` | `0 e4`, `0:e4`, `0:0xe4`, `hunk 0 offset 0xe4`, `Symbol+0x10` (global symbols) |
| AROS x86-64, i386 | the "Program failed" alert: `Module crash Segment 1 .text (0x…) Offset 0x…` and `Function X (0x…) Offset 0x…`; `Trace.Where` | the whole line or `1 d15`, `.text d15`, `segment 1 .text offset 0xd15`, `Function+0xa5` |
| AmigaOS 4 | the Grim Reaper / kernel crash log: `ESTEST:crash:Es_..._Main()+0x108 (section 1 @ 0x1114)`; `Trace.Where` | the whole line (the symbol form is used), `Symbol+0x10`, `1 1118` (section and offset), or an address as in the file (`0x100118c`) |

The project directory form picks `obj/<target>/<name>`: m68k when it is there, else the one build found; `--target` picks another.
`--docker` with no image takes the target's image (esharp-amiga-toolchain, or esharp-aros-i386 for aros-i386); `--toolchain` or
`ESHARP_TOOLCHAIN*` or PATH give a local toolchain's binutils instead.

### How it works

**m68k.** The exe is a hunk file. `esharp addr` reads its hunk table itself (hunk types, sizes, memory flags: chip, fast, any) and
the symbols of its HUNK_SYMBOL blocks, so any number of hunks works; a program with chip data has five (code, data, bss, chip data,
chip bss). Every m68k `esharp build` also writes, next to the exe:

- `<name>.map`, the linker map (`-Wl,-Map`, written under a fixed name and then moved, so a comma in the project name is no
  problem): where each input section went. The m68k linker lays the hunks out one after the other
  in its own address space, so a hunk and offset becomes a place in one input file's section: the program's object (`.text`,
  `.data`, `.bss`, `.datachip`, `.bsschip`, common symbols), the start-up code (`ncrt0.o`) or a library member.
- `<name>.o`, the same C compiled with `-g -c` (same code as in the exe). The hunk exe's own DWARF is not readable by the
  toolchain's `addr2line` or `objdump -l`; the object's is. `m68k-amigaos-objdump --dwarf=decodedline` on it gives the `.cs` line
  table (the generated C carries `#line` directives), and its symbol table has the static functions and fields the exe leaves out.
- Both compiles use `-fno-reorder-functions`, so the object has one code section. Without it gcc puts cold functions in a
  `.text.unlikely` section, which in an amiga-format object is a second hunk also named `.text`, and the line table's addresses
  could not be told apart.

A code offset in the program's own code then goes through the object's line table; an offset in data, bss, chip data or chip bss
gives the nearest symbol of the object (the static field: `Es_<Namespace>_<Class>_<Field>`); anything else gives the nearest symbol
of the exe and the file it came from (start-up or library code or data). A build without a map (from an older esharp) still maps
hunk-0 code offsets the old way (the object's code relative to `_main`) and gives the exe's nearest symbol elsewhere.

**ELF targets (AROS x86-64 and i386, AmigaOS 4, MorphOS).** Every build also links `<name>.debug`: the same program linked with
`-g -gdwarf-4` (gcc's code does not change with `-g`; the tests compare the `.text` bytes of both files; DWARF 4 because the os4
binutils read no DWARF 5). `esharp addr` reads the exe's section table and symbols itself and runs the target's `addr2line -f -i`
on the `.debug` file for code, which also says when a function was inlined into another. An AROS exe is a relocatable ELF and AROS
loads each section on its own: its alert's "Segment N" is the ELF section index (`.text` is 1) and "Offset" is the offset in that
section. If a pasted line gives a segment number and a section name that disagree, the name is used (with a note). An AmigaOS 4 exe is an ELF executable with addresses from 0x01000000; the Grim Reaper's `Symbol()+0x…` is exact. Its
"section 1 @ 0x…" was 4 less than the section offset of the same instruction in both crashes produced here (checked against the
crash-site disassembly in the log), so when a pasted line has both, the symbol form is used.

**Platform code.** Only user code has `.cs` lines. After each user function the generated C now has a `#line` back to the C file
itself (before, platform functions that followed a user function inherited its `.cs` file and got made-up line numbers). An address
in E# platform/runtime code (`Es_ESharp_*`) is reported as such, and platform code inlined into a user function is reported with the
user line it was inlined at (ELF) or as "runtime/platform code inlined into" that function (m68k).
A user function that gcc inlined into another is reported with its own line and an "inlined into" note on every target. On
the ELF targets that comes from `addr2line -i` (with the caller's file and line); on m68k the binutils' `addr2line` cannot read
the amiga-format object, so `esharp addr` reads the `DW_TAG_inlined_subroutine` entries from `objdump --dwarf=info` and
`--dwarf=Ranges` itself (the note names the function only, not the call line). The generated `main()` wrapper
and start-up and library code are reported as not the program's own code. Optimised code can attribute an address to a
neighbouring statement. The maps are valid for the exe built together with them (a rebuild changes the offsets).

### `Trace.Where`: where the program is, as an address

`Trace.Where("label")` (a `--trace` or `--debug` build, like the other `Trace` calls) writes the label and the address of the call
to the serial trace, in the crash reports' form, so it can go straight to `esharp addr`:

```
main: hunk 0 offset 0x912                (m68k: the segment list of the program is walked for the hunk that holds the call)
main: segment 1 .text offset 0xc7f       (AROS: debug.library DecodeLocationA, the numbers the AROS alert uses)
main: _start+0x102c                      (AmigaOS 4: relative to the start-up code's _start symbol)
```

The address is inside the call instruction (the return address minus 2 on m68k, 4 on PowerPC, 1 on x86), so the mapped line is the
line of the call; the call is never a tail jump. Use it to see how far a program got before it crashed, or to find where a line's
code is. On m68k the hunk comes from the CLI's `cli_Module` (a program started from the shell); a Workbench start falls back to
`pr_SegList` entry 3 (not tried). MorphOS prints the label only (no code offset yet); SDL and the host print the label and a note.

`esharp run samples/crash --args CRASH` ends with a timeout by design: the program crashes on purpose and the emulator then
sits in the "Software Failure" requester (or the hosted AROS alert), which nobody dismisses, so `esharp run` stops it when
`--timeout` runs out and reports the timeout. Read the trace lines (`--trace --serial <file>`, or `--log` on hosted AROS) and the
screenshot, not the exit status.

### What was checked

- Tests (`AddrTests`, `TraceAddrTests`; the Docker ones run when the image is in the local docker and show as skipped, with the image named, when it is not): the hunk table reader on a
  made-up file with chip and fast hunks, relocations, symbols and a debug block; the map reader (long names on two lines); the
  crash-report forms; addr2line output with inlined frames; an ELF object from the host gcc. `samples/crash` built for m68k: five
  hunks, a code offset from the exe's symbols maps into the right function, and an offset into the program's data, bss, chip data
  and chip bss maps to `Weights`, `_calls`, `Pattern` and `Buffer` (their places worked out from the map and the object's symbols).
  Built for aros, aros-i386 and os4: the `.text` of the exe and the `.debug` file are byte for byte the same, and each call of
  `es_trace_where_at` found in the disassembly maps to its `Trace.Where` line (27, 51, 38 inlined into Main at 60, 44), given as
  section and offset, as the AROS alert line, as `Symbol()+0x…` and as an os4 address.
- Kickstart 3.1 (40.069) in FS-UAE, A1200, `esharp run samples/crash --trace --serial trace.txt --args CRASH`: the trace has
  `main: hunk 0 offset 0x912`, `checksum: hunk 0 offset 0x7f8`, `divide: hunk 0 offset 0x99a`; `esharp addr` maps them to
  `Crash.cs:51`, `:27` and `:38` (the `Trace.Where` lines; Divide is inlined into Main). Then the zero divide shows the "Software
  Failure" requester, `dh0:crash Program failed (error #80000005)` (screenshot), and the trace has no `poke` line.
- Hosted AROS x86-64 and i386 (`tools/aros-hosted/aros-hosted.sh crash --args CRASH --log`): the trace lines map to their lines, and
  the alert's `Segment 1 .text ... Offset` and `Function ... Offset` lines map to `Crash.cs:39`, `return a / b;` in Divide,
  inlined into Main at line 60, which is the faulting `idiv` (x86-64: `Segment 1 .text ... Offset 0xD15` and `Function
  Es_CrashSample_Program_Main ... Offset 0xA5`; i386: `Offset 0x00000B9D` and `Offset 0x000000B1`). The alert says "Line 1111 (F)
  Emulator/Coprocessor error" for the zero divide on hosted AROS.
- AmigaOS 4.1 in the QEMU VM (`esharp run samples/crash --target os4 --trace --args CRASH`): the PowerPC divide does not trap, the
  null write does (a DSI exception); the kernel's crash log on the serial port has `ip : ESTEST:crash:Es_CrashSample_Program_Main()+0x108
  (section 1 @ 0x1114)`, which maps to `Crash.cs:45`, `_box!.Value = value;` in Poke, inlined into Main at line 61 (the `stw` the
  log's crash-site disassembly marks; its section offset is 0x1118), and the four trace lines (`_start+0x102c`, `+0xa60`,
  `+0x10fc`, `+0x1110`) map to lines 51, 27, 38 and 44.
- Not checked: a real Amiga (SegTracker, Enforcer, MuForce output), a Workbench start, MorphOS, a Grim Reaper requester's own
  window text (the same report was read from the serial log), whether the os4 "section @" offset is always 4 below.

### What a crash shows on the emulator

A divide by zero (`a / b` with `b == 0`) on Kickstart 3.1 in FS-UAE shows the "Software Failure" requester:
`dh0:crash  Program failed (error #80000005)`. That is all the base OS tells you: the error number (80000005 is the 68k zero-divide
trap) and the program name, not the PC. A null write does not crash on an A1200 (no MMU). To get a hunk and offset you need a tool
that reports the faulting PC: SegTracker (it prints hunk and offset), with Enforcer or MuForce on MMU machines, none of which is part of
AmigaOS (not tried here). Getting the PC out of an emulator debugger was not tried either.
Without a PC, `Trace.Where` before the suspect call gives the last place reached as a hunk and offset.

## FS-UAE debugger (trial)

Issue #14, a one-day trial of the debugger built into FS-UAE (no UAE fork). Everything below was run with FS-UAE 3.2.35 on Linux under Xvfb, `--model A1200`, Kickstart 3.1 (40.069), with `samples/hello` built by `esharp build samples/hello --target m68k --docker <m68k image>`. Addresses are from that setup; yours will differ.

**Recommendation: adopt as a recipe, not as a feature.** It works headless and gives real breakpoints, single-step, registers, memory and disassembly on the running program, and the E# symbols from `nm`/`objdump` line up with the loaded code. It is manual (about 20 lines of driver script, no `esharp` integration) and finding the load address takes an extra step, so use it for "what is this crash / this register / this buffer" questions, not as the everyday loop. No `esharp run` option was added: a useful `--debugger` flag would need a pty for the console and an injected F12+D, which is more than a pass-through flag.

### Enabling it

- Config option **`console_debugger = 1`** is required. Without it FS-UAE prints `Option "console_debugger" is not enabled` when you ask for the debugger. `use_debugger = 1` has no effect (the written `debug.uae` still says `use_debugger=false`), and the debugger does not start by itself.
- The debugger is entered by the hot key **F12 + D** (the FS-UAE "special key" F12 plus `D`; the code calls it `SPC_ENTERDEBUGGER`). Emulation stops, `WARNING: Activated debugger` is printed and a register dump appears.
- The prompt is on FS-UAE's **stdin/stdout**, not in the emulator window. Give stdin and stdout a **pty**: with a plain pipe or a file, stdout is block-buffered and the answers show up only when the process exits.
- Headless: Xvfb is enough. The hot key can be sent with XTEST (`libXtst`; X keycodes F12 = 96, `d` = 40, the same mechanism as `esharp run --input`). `esharp run --input` itself only fires after the program prints `SHOT`, and does not give you the console, so it is not used here.
- **The first line sent after entering is swallowed**; send a throw-away `r` first.

### What works (all tried)

| Command | Result |
|---|---|
| `r` | registers, SR flags, current instruction |
| `m <addr> [n]` | memory dump (hex numbers only: `m 132AC-948` is read as hex 948, so write `0x3B4` as `3B4`) |
| `d <addr> [n]`, `d pc 3` | disassembly (matches `m68k-amigaos-objdump -d` for the same code) |
| `f <addr>` then `g` | breakpoint, run until it is hit (`Breakpoint at 0001B2B6`), `f <addr>` again removes it, `fl` lists them |
| `t` | single step (it runs through the interrupt vectors too: one `t` in idle took an exception) |
| `W`, `w`, `z`, `fi`, `S` | in the help, **not tried** |

Not working: **`fp "<name>"`** (run until process active) never stopped, tried with `hello`, `dh0:hello` and `Initial CLI` for a program run from the Startup-sequence. **`s` (memory search)** finds nothing (not even `dos.library`; each scan block is cut short by a `console_isch` stub). **`T`** (task list) printed nothing. So there is no way to ask the debugger where the program was loaded; you work it out as below.

### Breaking on an E# function

E# functions have symbols: `m68k-amigaos-nm -n samples/hello/obj/m68k/hello` (in the m68k docker image; the hunk file keeps them) gives `_main` at text offset `0xfe`. Notes:

- The name is the C name (`_Es_Hello_Program_Main` is at `0xe8`) but gcc inlined it into `_main` here, so a breakpoint on `Main` never fires. Check `objdump -d` for which symbol is really called, or build with `--debug` (not tried for this).
- The program is a hunk file loaded at run time, so the breakpoint address is **load base + offset**. The text hunk starts at offset 0. The base is not known in advance, but it is the same on every run of the same program, ROM and config (checked twice for the one-shot run, and across 3 iterations of a loop run).

Finding the base without a loader hook: `hello` calls dos `PutStr` (LVO -948) with the string at text offset `0xd8`.

1. Boot, enter the debugger, and break on that library vector: DOSBase was `0x132AC` in this setup (A6 inside dos code, and `m 132AC 1` has the name pointer to `dos.library`), so `f 12EF8` (`0x132AC - 0x3B4`; `d 12EF8 1` shows `JMP $00fa51ea`), `g`.
2. At the hit `D1` is the string pointer: `0x1B290 - 0xD8` = text base `0x1B1B8`. (The return address on the stack minus the offset of the call gives the same answer.)
3. In the next run, `f 1B2B6` (`0x1B1B8 + 0xFE`), `g`: `Breakpoint at 0001B2B6`, `MOVE.L A6,-(A7)` (the first instruction of `_main`), the next lines match `objdump`, and `m 1B290 2` shows `Hello from E#!`.

For a one-shot program the debugger has to be entered before it runs (boot takes about a second; entering at 1 s worked). For a program that is already running, loop it in the Startup-sequence (`lab again` / `dh0:hello >NIL:` / `skip back again`; there is no `C:` in the run drawer, so no `Wait`), enter at any moment, and the next iteration hits the breakpoint (base was the same on every iteration).

### Driver (Python 3, pty + XTEST, no extra packages)

```python
import ctypes, os, pty, subprocess, sys, threading, time
cfg, wait, cmds = sys.argv[1], float(sys.argv[2]), sys.argv[3].split(';')   # config, seconds to wait, "r;f 1B2B6;g;r"
DISP = ':77'
xvfb = subprocess.Popen(['Xvfb', DISP, '-screen', '0', '1024x768x24'], stderr=subprocess.DEVNULL); time.sleep(1.5)
m, sl = pty.openpty()
log = open('fsuae.out', 'wb')
def read():
    while True:
        try: b = os.read(m, 4096)
        except OSError: return
        log.write(b); log.flush()
threading.Thread(target=read, daemon=True).start()
env = dict(os.environ, DISPLAY=DISP, SDL_AUDIODRIVER='dummy')
f = subprocess.Popen(['fs-uae', cfg], stdin=sl, stdout=sl, stderr=sl, env=env)
x11 = ctypes.CDLL('libX11.so.6'); xt = ctypes.CDLL('libXtst.so.6'); x11.XOpenDisplay.restype = ctypes.c_void_p
time.sleep(wait)
d = ctypes.c_void_p(x11.XOpenDisplay(DISP.encode()))
for code, press in ((96, 1), (40, 1), (40, 0), (96, 0)):      # F12 down, D down, D up, F12 up
    xt.XTestFakeKeyEvent(d, code, press, 0); x11.XFlush(d); time.sleep(0.2)
time.sleep(2)
for c in ['r'] + cmds:                                        # the first line is swallowed, so send a throw-away r
    os.write(m, (c + '\n').encode()); time.sleep(4)          # a long run (g) needs the pause
f.kill(); xvfb.kill()
```

The config is the one `esharp run` writes plus `console_debugger = 1` (see `src/ESharp.Cli/FsUae.cs`); the output is `fsuae.out`. The commands are fed with fixed pauses, so a command that needs longer than the pause (a long `g`) will see the next line too early; raise the pause.

### Not verified

- A real AmigaOS Workbench or other program than `hello` (the loop and one-shot recipes were run only for `hello`), `--debug` builds, `w` watchpoints, `z`, `fi`, `S`, source-level debugging (there is none: `#line` info is for the C compiler, the debugger sees machine code only).
- Other Kickstarts, the AROS ROM replacement, a non-headless window, other FS-UAE versions, PowerPC (FS-UAE cannot run it).
- Whether the load addresses stay the same when the program or the files in the drawer change (they depend on the heap layout; re-derive them).
- Whether `fp` works for a program started some other way (from a Workbench icon, or `Run`).
