E# hardware test pack
=====================

Thank you for testing. E# turns C# into native Amiga programs; so far everything has only run
in an emulator. Your run is the first on real hardware.

0. Arrival-day checklist (about 30 minutes; the details are in the sections below)
   [ ] Copy this drawer to the hard disk (or write hwtest.adf to a floppy, 1b). Only if copied from a PC:
       Protect RunAll RunOne +s  and  Protect #? +e
   [ ] Optional, before the run: install MUI (muimaster.library) yourself if you have it: mui and muiclass then run their checks
       (section 3a); without it they say "skip: MUI not installed" and are marked skipped, which is fine. Do not install
       anything else; SegTracker, Enforcer or MuForce are welcome if you already use them (section 4e).
   [ ] Shell, CD into the drawer, type:  Execute RunAll   (about 3 to 5 minutes; look and listen: sections 4a and 4b)
   [ ] By eye, by hand (section 4a, about 10 minutes): gui, guiclass, boopsi WAIT, keys, asl requester, wbstart by double-click,
       and mui WAIT / muiclass WAIT if MUI is installed. Write the answers into the form at the end of section 4.
   [ ] Send back (section 4d, 5): SUMMARY.txt, the results drawer, the BUILD file from this drawer (the version of this pack),
       your machine details, the by-eye answers, photos of anything odd.
   If something crashes: write down the guru number, the program name (results/PROGRESS.txt names it) and, if a tool such as
   SegTracker, Enforcer or MuForce shows one, the "hunk N offset X" line. We turn that into a C# source line with
   "esharp addr" (docs/DEBUGGING.md) using the BUILD number. Then  Execute RunAll RESUME  carries on with the other programs.

1. What you need
   An Amiga 1200 or a THEA1200 (68020 or better, AGA, 2 MB chip RAM, Kickstart 3.1, Workbench 3.1 on a
   hard disk or CF card; Kickstart/Workbench 3.0 also runs it, see 1b a)). Other machines are welcome to report (see section 5) but are not the target.

1b. The floppy image (obj/hwtest.adf)
   tools/hwpack/build.sh also writes the whole pack as ONE 880 KB AmigaDOS floppy image, hwtest.adf (FFS, volume
   HWTEST, a data disk: only E# files, no AmigaOS files, not bootable; about 760 KB used, about 120 KB left for results).
   The floppy leaves out six programs for lack of room: mui and muiclass (they need MUI, which a floppy-only machine rarely
   has), guiclass (the same window as gui, written as a class), wbstart (the Workbench start check, section 4a) and the two card
   games rummy (82 KB) and cards (68 KB). RunAll reports each one it would run as "not in this pack ... skipped". They are in the
   pack drawer: run those from a hard disk copy (or the emulator). The results need about 105 KB of the disk: with less free
   space the run fills the disk and stalls.
   Every program has a Workbench icon (NAME.info, written by esharp build): open the disk's window on Workbench and double-click a program to
   start it (a console window for its output may open: seen for the wbstart sample, not checked for these programs; RunAll still needs a Shell). The bootable variant in d) has no icons (no room).
   a) A real Amiga 1200 with only a floppy drive (no hard disk, no fast RAM; often Kickstart 3.0). A stock A1200 has ONE
      floppy drive: use the bootable variant in d) (one disk, no swapping). The Workbench-disk way below was run in the
      emulator with TWO drives (Workbench in DF0, pack in DF1); the one-drive version of it is NOT VERIFIED (steps further down).
      - Write hwtest.adf to a double-density disk with a Greaseweazle or KryoFlux (or put it on a USB stick for a Gotek
        running FlashFloppy and select it there). A PC floppy drive cannot write Amiga disks.
      - Boot your own Workbench 3.x floppy. With a second drive insert the pack disk in DF1:, open a Shell
        (Workbench disk, System drawer) and type:   CD HWTEST:   then   Execute RunAll
        Do not write-protect the pack disk: the results are written to it (results drawer and SUMMARY.txt).
      - Before RunAll, so that diskfont.library does not stop with an "insert volume FONTS:" requester (the Workbench
        disk has no Fonts drawer):   MakeDir RAM:Fonts   then   Assign FONTS: RAM:Fonts
      - The Workbench disk's startup makes T: and ENV: in RAM:, which RunAll needs. Its Libs drawer has the libraries
        asl, diskfont, iffparse and (Workbench 3.1 only) lowlevel, so the ok counts in section 3 apply; with a different boot disk some
        programs print "not installed" lines (section 3).
      - ONE DRIVE (NOT VERIFIED, not run even in the emulator): after the Workbench 3.1 startup the Workbench disk is needed again
        for every C: command and every library, so first load what the scripts use into RAM while the Workbench disk is still
        in DF0, in the Shell:
            Resident C:Execute PURE
            Resident C:Date PURE
            Resident C:Type PURE
            Resident C:Delete PURE
            Resident C:Assign PURE
            Resident C:MakeDir PURE
            Copy Libs/asl.library Libs/diskfont.library Libs/iffparse.library Libs/lowlevel.library RAM:Libs   (MakeDir RAM:Libs first)
            Assign LIBS: RAM:Libs ADD
        then swap the Workbench disk for the pack disk and CD HWTEST: ; Execute RunAll. If a requester still asks for the
        Workbench disk, put it back, answer, and tell us which command it was (it needs a Resident line too). The bootable
        variant in d) avoids all of this.
      - Afterwards read results/ and SUMMARY.txt off the disk (copy them to a hard disk or read the image back with
        tools/hwpack/mkadf.py: python3 -I mkadf.py extract <image> <folder>). With a Greaseweazle or KryoFlux read the disk
        into an .adf first.
      Kickstart 3.0 with Workbench 3.0 was run in an emulator: everything passes except controls, which gives 9 ok instead
      of 12 (Workbench 3.0 has no lowlevel.library). The bootable disk is then written without lowlevel.library.
   b) THEA500 Mini (or another UAE-based emulator that loads .adf files):
      - Copy hwtest.adf, and your own Workbench disk image, to a USB stick and choose the A1200 / AGA setting in the
        machine's menu. NOT VERIFIED: the exact menu entries and the file-name convention were not checked on a machine.
        Retro Games' official manual is at https://retrogames.biz/support/thea500-mini (not read in full here; check its
        sections on the USB stick and on loading your own programs).
      - Boot the Workbench image in DF0 and the pack image in DF1 (or insert the pack disk as the second disk), then as in a).
      - The emulator writes the results into the image; copy the image back from the stick and read it with mkadf.py.
   c) FS-UAE, no hard disk, as a stock A1200 (Kickstart 3.1, 2 MB chip RAM only):
        tools/hwpack/run-emulated.sh --floppy obj/hwtest.adf --rom <rom> --wb <Workbench 3.1 disk .adf>
      DF0 is a throwaway copy of your Workbench disk (with one extra file, S:User-Startup, that runs the pack from
      DF1), DF1 a copy of the pack disk. It prints SUMMARY.txt read back from the image; the run directory keeps
      results-disk/ (everything on the pack disk afterwards).
   d) Bootable variant, for your own use only: build.sh <image> --boot-wb <your Workbench 3.x disk 2 .adf> also writes
      obj/hwtest-boot.adf, the pack plus ONLY the few AmigaOS files needed to boot and run it (C:Execute, Date, Delete,
      Type, Assign, MakeDir, SetPatch and four libraries, read from YOUR Workbench disk at build time, boot block included, so build.sh needs --boot-wb for it) and a
      startup-sequence we wrote. build.sh refuses to write it anywhere that git does not ignore. Boot it alone in DF0: it runs SetPatch (copied from your disk, as a real Workbench does), then RunAll, and leaves a shell. THIS DISK HOLDS COPYRIGHTED AMIGAOS FILES: use it
      yourself, never share it, attach it to a report or commit it (obj/ is git-ignored). Emulated:
        tools/hwpack/run-emulated.sh --boot-floppy obj/hwtest-boot.adf --rom <rom>
      The pack disk with 880 KB has room for results, but the boot variant has only about 108 KB left: if the disk fills up
      during the run, delete README.txt on it (or use the two-disk way in a)).

2. Run it
   - (Floppy only: see 1b.) Copy this whole drawer to the Amiga (anywhere on the hard disk), open a Shell, CD into it.
   - Only if you copied it from a PC (the Amiga protection bits are lost):
         Protect RunAll RunOne +s
         Protect #? +e
   - Type:  Execute RunAll
   First hwinfo runs, then the programs one after the other (41 runs of 40 programs; the samples named in section 4a are left to you). Each one writes results/NAME.txt (what it
   printed) and results/NAME.sum (its counts); RunAll collects them into SUMMARY.txt, which starts with a
   machine report (hwinfo: CPU, FPU, chipset, Kickstart and library versions, chip and fast RAM), then one line per
   program, then the start and finish times, then every line that needs a look. Several programs open a screen or
   play sound; they need no input. Look and listen (section 4), press Esc in cards and rummy or leave them
   alone for about ten seconds.

   A program that crashes: RunAll keeps going if the program returns (a failed run is listed in SUMMARY.txt
   with "return code 5 or higher"). If the machine guru-meditates or hangs, reset it, look at
   results/PROGRESS.txt (it names the program that was started last: that is the one that crashed), CD back
   into this drawer and type  Execute RunAll RESUME  to carry on after the programs that already finished
   (their results are kept). The program that was running at the crash is NOT run again: it is listed in
   SUMMARY.txt and results/FLAGS.txt as crashed, with the RunOne line to try it by hand. To run one program
   again by hand (also after a RESUME run):  Execute RunOne cards cards 15
   If you stop a RESUME run with Ctrl-C, the resume marker T:hwpack-resume stays behind and a later RunOne
   by hand would skip finished programs: type  Delete T:hwpack-resume  (or reboot: T: is in RAM). A plain
   Execute RunAll also removes it.

   Time: in an emulator the whole run is about four minutes (the sound, keys, cards, rummy and
   screen programs wait on purpose); a real machine may be a bit slower or faster.

3. What the numbers should be
   SUMMARY.txt marks a program with an arrow (<--) when it has a FAIL line (the FAIL lines are repeated under it),
   no output, or an "ok" count that differs from the count an emulated A1200 (Kickstart 3.1, Workbench 3.1) gives;
   the last lines of SUMMARY.txt list those programs. FAIL is the thing to report; a count difference is a
   note, and sometimes expected (below). Emulator counts:
     phase2 32, memory 19, generics 24, properties 18, operators 20, language 39, collections 20, inherit 24,
     lambdas 16, closures 23, linq 37, refs 18, patterns 23, objects 21 (and 21 again with the arguments SEED=42 auto, which
     RunAll passes as AmigaDOS arguments), names 12, dates 19, interrupts 14, timing 10, fonts 8, iff 10,
     controls 12, keys 8, asl 3, boopsi 37, screen 23, aga 6, assets 17, rtg 5, sound 21, dbuffer 30, textspeed 1,
     cards 15, rummy 28, retcode 1, mui 32 and muiclass 26 (both only with MUI installed, see below).
   retcode prints one ok line and ends with return code 0. To see a return code reach the script, run it by hand with
   the target return code 5:  Execute RunOne retcode-rc5 retcode 1 "TARGETRC=5"  : it must print "rc 5" in results/retcode-rc5.txt and
   RunOne must answer "return code 5 or higher ... <-- CHECK" (that is the wanted result here, not a failure).
   muicheck prints the version of muimaster.library ("muimaster.library 19.35" is MUI 3.8, "21.227" MUI 5) or "not installed".
   MUI is third-party shareware: you install it yourself, nothing of it is in this pack. Without it mui and muiclass print one
   line, "skip: MUI not installed", and SUMMARY.txt says "skipped, MUI is not installed (optional)": not a failure. With it they
   open a small window for two seconds. RunAll gives them a 64 KB stack (MUI 5 asks for more than a plain shell has).
   hello, mainargs (run with the arguments ONE "two words" RC=3: it lists the three arguments and ends with
   return code 3, which is not an error) and trace print text, not ok lines (trace also sends 9 lines to the
   serial port; nothing needs to be connected).
   Expected differences on real machines:
     controls   12 with lowlevel.library in LIBS: (9 without it); the joystick line depends on what is plugged in
     keys       9 if you press a key while it waits (it waits about ten seconds)
     fonts      7 without diskfont.library, 8 with it, 10 with it and a disk font such as ruby in FONTS:
     dates      19. The "accented letters fold too" line (0xE9 and 0xC4) follows the language rules of utility.library: with
                locale.library running (the Workbench startup runs IPrefs) it follows the user's locale, so a language driver
                that is not Latin-1 could fail it. It passed in the emulator both with the plain ROM and after the Workbench
                3.1 startup (default locale). The sample also prints an "info: ToUpper(0xF7) = ..." line: 0xF7 (the ROM) or
                another value under a locale; it is information, not a check.
     iff, asl   one line saying "not installed" and no ok lines if iffparse.library / asl.library is not in LIBS:
     timing     one fewer ok and a "note" line if the machine is not real time; 60 frames per second on NTSC
     rtg        runs more checks on a machine with an RTG card
     sound      "skip" lines (they are not failures) if the audio hardware cannot play
   In our emulated run LIBS: also points at the Workbench 3.1 disk's Libs drawer (diskfont, iffparse, asl and
   lowlevel are there), which a real hard disk install has too; the Fonts disk is not mounted, so no ruby.

4. By eye, and what to send back
   RunAll has no one to click for it, so these are done by hand. Open a Shell in this drawer.
   a) Left out of RunAll because they need you (they are in this drawer):
      gui    Type  gui . A small window "E# Greeter" opens on the Workbench screen. Click Hello: the Reply field
             must show "Hello, Amiga!". Then click the close gadget at the top left: the program ends and the
             shell prints its ok lines (7 ok). Does the window look right, do the gadgets react?       yes / no
      asl    RunAll ran the allocation checks only. Type  Echo >RAM:asl-open x  then  asl . A file requester opens
             on a blue screen; click Cancel, run it again and pick a
             file and OK. Both must close the requester without trouble. Then  Delete RAM:asl-open .   yes / no
      boopsi RunAll ran it without waiting. Type  boopsi WAIT . A window "E# BOOPSI" opens with framed Hello and Quit buttons,
             a slider and a text field holding "Amiga". Click Hello, drag the slider knob, click into the text field,
             type " 1200" and press Return, then click the close gadget. It prints an "event:" line for each thing you
             did and an "events: ..." summary (the number of keys the edit hook saw included), then 38 ok. Quit
             instead of the close gadget also ends it (fewer ok lines then). Do the gadgets look and react right?   yes / no
      guiclass  The same window as gui, written as a class of its own. Type  guiclass . Click Hello (Reply: "Hello, Amiga!"), then the
             close gadget: 8 ok. Same look and reaction as gui?                                         yes / no
      wbstart  Workbench start by double-click (the shell cannot test it). Open the drawer's window on Workbench and double-click the wbstart
             icon: a window "E# Workbench start" opens with Arguments 2, SEED 42, AUTO yes and Stack 32768 (the icon's tool types and stack size).
             Close it with its close gadget. From a Shell (wbstart SEED=7) it shows Arguments 1, SEED 7, AUTO no and your shell's stack.
             Does the icon start it, and are those four values right?                                    yes / no, values
      mui, muiclass  Only if MUI is installed. Type  Stack 65536  then  muiclass WAIT : a window "E# MUI custom class" with two bar graphs in a
             group, a slider and Quit. Click or drag on a graph: its level follows the mouse and the slider follows it. Quit ends it (26 ok).
             Type  mui WAIT : a window with text, buttons, a slider, a string, a cycle gadget, a check mark, a gauge and a list; click things,
             then Quit or the close gadget. Do they draw and react right? Which MUI (muicheck)?          yes / no, version
      keys   RunAll ran it without a key press. Type  keys , wait for the blank screen and press A within ten
             seconds: it prints a "pressed key ... typed ..." line (your keymap; Shift+A types 65). Send that
             line, and say what keyboard (original, USB adapter, THEA1200 input) you used.             note it
   b) Pictures and sound that RunAll shows for a few seconds (run the program by hand again if you missed it):
      screen   a picture of cards on a coloured background, not garbled, no flicker                  yes / no
      aga      a smooth 256 colour image                                                              yes / no
      assets   dithered gradients and a badge, one short beep                                         yes / no
      dbuffer  a bar sweeps across the screen with no tearing or flicker (double buffering)           yes / no
      textspeed  the two timing numbers in results/textspeed.txt (Font.Draw against rectangles)       note them
      sound    clean tones, the pitch rises/falls as it should, no crackle                            yes / no
      rtg      says no RTG card (normal on a stock A1200) or runs on yours                            note it
      cards    cards drawn right; mouse moves; clicking Higher/Lower works (run it by hand to play)   yes / no
      rummy    cards drawn right; you can draw, discard, and the bot replies                          yes / no
      Roughly how long did the bot take to reply in rummy (seconds)?
   c) Read from the result files:
      controls the "clock ... ticks per second" line (709379 on PAL) and the "port 1 kind" line      note them
      fonts    the "diskfont.library opens" line and which disk font was found                        note them
      timing   the "display runs at ... frames per second" line and "50 WaitVBlank calls took ... ms"   note them
      interrupts  "25 frames: the first server counted ..."                                           note it
   d) Always send: SUMMARY.txt and the results drawer (or at least every file with a FAIL line in it), the BUILD file, and
      your machine: model (A1200 or THEA1200 with its FPGA core and version), CPU and clock, RAM chip/fast,
      accelerator card if any, Kickstart and Workbench version, graphics card or Indivision/scandoubler, how it
      is connected to the display (monitor, TV, HDMI adapter), the total time from SUMMARY.txt, and whether
      anything crashed, hung, or left the screen in a bad state.
      On the serial port: trace writes 9 lines at 9600 baud; if you have a terminal on it, send what it shows.

   e) If something crashes (a guru, a requester "Software Failure", a hang): the guru or requester number and the name of the
      program that was running (results/PROGRESS.txt) are the minimum; a photo of the screen is fine. If you run SegTracker (it
      prints "hunk N offset X" for the crashing address) or Enforcer/MuForce (on a machine with an MMU), send that line too: we
      turn it into a C# file and line with   esharp addr samples/<program> <hunk> <offset> --docker <m68k image>   (the BUILD file
      in this drawer names the version; the address only fits the exe built from that version; docs/DEBUGGING.md). Without such a
      tool the number alone does not give a place; then the serial trace of "trace" or a note of how far the program got helps.
      None of these tools is needed or shipped with the pack; use them only if you already have them. Then reset and type
      Execute RunAll RESUME (section 2).

5. Where to send it
   Open an issue "Hardware report" on https://github.com/GameGyrafe/amigaesharp/issues (there is a
   template) and attach or paste the above. Photos of the screens are very helpful.

What is not tested: PowerPC, other accelerators, RTG cards, AmigaOS 3.2/3.9 specifics. You are
welcome to report those too; say what you used.

Nothing in this pack writes outside its own drawer except: sound writes three small files to RAM: and
deletes them again; iff writes RAM:esharp_test.iff (deleted too); asl by hand uses RAM:asl-open (you delete it);
RunAll uses T: for two small temporary files; no program touches the hard disk outside this drawer.
