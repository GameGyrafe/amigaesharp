# Debugging E# programs

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
