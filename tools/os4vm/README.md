# os4vm: running E# programs in an AmigaOS 4.1 VM

`esharp run --target os4` and `esharp test --target os4` boot an AmigaOS 4.1 Final Edition (Pegasos II) VM in QEMU,
run the program unattended and read its output back. This directory has the pieces that are not in the CLI itself:

- `os4vm.py` (run with `python3 -I`, standard library only): `check`, `start` (boot the VM by hand, on a throwaway
  overlay unless `--write`), `install-hook` and `remove-hook` (the one-time change below).
- `estype.c`: a tiny AmigaOS 4 command that copies text to the kernel debug output. The CLI embeds this source and
  compiles it with `ppc-amigaos-gcc` (on the host, or in the `--docker` image) for each test CD.

No AmigaOS file is in this repository. AmigaOS 4.1 is commercial: you supply your own installed VM.

## The VM directory (`ESHARP_OS4_VM`)

Set `ESHARP_OS4_VM` to a directory with:

| file | what |
| --- | --- |
| `bboot` | BBoot 0.8 or later (by the qmiga project, on codeberg.org) (boot loader passed as `-kernel`) |
| `Kickstart.zip` | the Kickstart modules and `Kicklayout` of your install (passed as `-initrd`; AmigaOS 4.1 FE Update 3 tested, with the sm502 graphics driver `siliconmotion502.chip` added to `Kicklayout`) |
| `amiga.img` | raw hard disk image with AmigaOS 4.1 FE installed (it boots to Workbench) and the test hook below |
| `qemu-data/` | optional extra QEMU data dir (`-L`), e.g. `qemu-data/dtb/pegasos2.dtb` when your QEMU package lacks it |

The VM runs as `qemu-system-ppc -M pegasos2 -m 2048 -kernel bboot -initrd Kickstart.zip -append serial -vga none
-device sm501` (QEMU 10.x; `qemu-img` is needed too). Every run boots on a new qcow2 overlay of `amiga.img` under /tmp,
deleted afterwards, so test runs never write `amiga.img`. Only one VM may use the directory at a time: the CLI refuses
to start while a `qemu-system-ppc` that uses it is running, and esharp runs wait for each other (/tmp/esharp-os4vm.lock).
Warm reboots do not work in this VM; each run is a fresh boot (about 10 s to the program on a fast host).

## How a run works

1. The program, `estype` and a script `autorun` go on an ISO 9660 CD image labelled `ESTEST`, with Rock Ridge modes 755
   (they become the Amiga protection bits; files without `e` would not run). `genisoimage` comes from the host or the
   `--docker` image.
2. QEMU boots the overlay with the CD, its display off, the serial port written to `obj/os4/run/serial.log` and the
   monitor on a unix socket. `-append serial` makes the kernel send its debug output to the serial port.
3. The hook in `S:User-Startup` sees the volume `ESTEST:` and executes `ESTEST:autorun`, before `LoadWB`:

   ```
   ESTEST:estype -t "ESHARP-BEGIN <nonce>"
   CD RAM:
   ESTEST:<program> | ESTEST:estype
   ESTEST:estype -t "ESHARP-END <nonce>"
   ```

   `estype` writes each line with `DebugPrintF`, prefixed `ES: `, so the host can tell the program's lines from kernel
   and driver messages on the same port. (`SER:` also reaches the serial file, but serial.device dropped characters.)
4. The host polls the serial file. When the program prints `SHOT` and `--screenshot` was given, it waits a second and
   saves the monitor's `screendump` as a PNG. At the end marker (or the `--timeout`) it quits QEMU. Lines without the
   prefix (Trace output, which uses `DebugPrintF` on AmigaOS 4, and kernel messages) go to the `--serial` file.

A crash leaves the Grim Reaper waiting, so the run ends at the timeout with the output so far and the serial tail.
Not supported yet: `--input` (scripted mouse and keys; the monitor's `sendkey`/`mouse_move` could do it), the program's
return code (the pipe hides it).

## The hook (one-time change to `amiga.img`)

`python3 -I tools/os4vm/os4vm.py install-hook` boots the VM on an overlay with a CD holding a script, opens Workbench's
Execute Command (Right Amiga+E through the monitor's `sendkey`), types `Execute ESTEST:change`, waits for the script's
confirmation on the serial output, and merges the overlay into `amiga.img` with `qemu-img commit`. The monitor's `quit` is a
power-off with no shutdown, so the change is made safe in steps: the script waits 15 s (`Wait`) before it prints the
confirmation, the host waits another 15 s before `quit`, `amiga.img` is copied to `amiga.img.bak-<date-time>` (`cp
--reflink=auto`) before the commit, and after the commit a second boot of the new image (a fresh overlay) searches
`S:User-Startup` for the hook (install) or its absence (remove); success is reported only if that matches. Delete the backup
when you are happy. SIGTERM/SIGHUP to the script also stop its QEMU. The script copies `S:User-Startup` to `S:User-Startup.pre-esharp` and appends these lines:

```
;BEGIN ESHARP-TEST (E# os4 test harness hook, see tools/os4vm/README.md in the E# repo)
Assign >NIL: ESTEST: EXISTS
If NOT WARN
  If EXISTS ESTEST:autorun
    Execute ESTEST:autorun
  EndIf
EndIf
;END ESHARP-TEST
```

Without a volume `ESTEST:` (or without `autorun` on it) nothing happens and Workbench boots as before; `Assign EXISTS`
does not open a "please insert volume" requester. To undo: `os4vm.py remove-hook` (copies `S:User-Startup.pre-esharp`
back), or delete the lines between `;BEGIN ESHARP-TEST` and `;END ESHARP-TEST` by hand. Make a copy of `amiga.img` first
if you want to be able to go back further.

## Stopping and stray VMs

`esharp run/test --target os4` stops its QEMU (monitor `quit`, then kill of that pid only) and removes `/tmp/esharp-os4-<id>`
when the run ends, and also on SIGTERM, SIGHUP, SIGINT and process exit. As a backstop for a SIGKILL, each run records the
qemu pid and work dir in `/tmp/esharp-os4vm.pid`; the next run kills that pid if it is still a qemu-system-ppc using that work
dir, then goes on. A qemu you started by hand is never touched (the run refuses to start instead).
