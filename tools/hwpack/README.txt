E# hardware test pack
=====================

Thank you for testing. E# turns C# into native Amiga programs; so far everything has only run
in an emulator. Your run is the first on real hardware.

1. What you need
   An Amiga 1200 or a THEA1200 (68020 or better, AGA, 2 MB chip RAM, Kickstart 3.1, Workbench 3.1 on a
   hard disk or CF card). Other machines are welcome to report (see section 5) but are not the target.

2. Run it
   - Copy this whole drawer to the Amiga (anywhere on the hard disk), open a Shell, CD into it.
   - Only if you copied it from a PC (the Amiga protection bits are lost):
         Protect RunAll RunOne +s
         Protect #? +e
   - Type:  Execute RunAll
   First hwinfo runs, then the programs one after the other (35 runs of 34 programs). Each one writes results/NAME.txt (what it
   printed) and results/NAME.sum (its counts); RunAll collects them into SUMMARY.txt, which starts with a
   machine report (hwinfo: CPU, FPU, chipset, Kickstart and library versions, chip and fast RAM), then one line per
   program, then the start and finish times, then every line that needs a look. Several programs open a screen or
   play sound; they need no input. Look and listen (section 4), press Esc in cards and rummy or leave them
   alone for about ten seconds.

   A program that crashes: RunAll keeps going if the program returns (a failed run is listed in SUMMARY.txt
   with "return code 5 or higher"). If the machine guru-meditates or hangs, reset it, look at
   results/PROGRESS.txt (it names the program that was started last: that is the one that crashed), CD back
   into this drawer and type  Execute RunAll RESUME  to carry on after the programs that already finished
   (their results are kept). To run one program again by hand:  Execute RunOne cards cards 15

   Time: in an emulator the whole run is about three minutes (the sound, keys, cards, rummy and
   screen programs wait on purpose); a real machine may be a bit slower or faster.

3. What the numbers should be
   SUMMARY.txt marks a program with an arrow (<--) when it has a FAIL line (the FAIL lines are repeated under it),
   no output, or an "ok" count that differs from the count an emulated A1200 (Kickstart 3.1, Workbench 3.1) gives;
   the last lines of SUMMARY.txt list those programs. FAIL is the thing to report; a count difference is a
   note, and sometimes expected (below). Emulator counts:
     phase2 32, memory 19, generics 24, properties 18, operators 20, language 39, collections 20, inherit 24,
     lambdas 16, linq 37, refs 18, patterns 23, objects 21 (and 21 again with the arguments SEED=42 auto, which
     RunAll passes as AmigaDOS arguments), names 12, dates 19, interrupts 14, timing 10, fonts 8, iff 10,
     controls 12, keys 8, asl 3, screen 23, aga 6, assets 17, rtg 5, sound 21, dbuffer 30, textspeed 1,
     cards 15, rummy 28.
   hello, mainargs (run with the arguments ONE "two words" RC=3: it lists the three arguments and ends with
   return code 3, which is not an error) and trace print text, not ok lines (trace also sends 9 lines to the
   serial port; nothing needs to be connected).
   Expected differences on real machines:
     controls   12 with lowlevel.library in LIBS: (9 without it); the joystick line depends on what is plugged in
     keys       9 if you press a key while it waits (it waits about ten seconds)
     fonts      7 without diskfont.library, 8 with it, 10 with it and a disk font such as ruby in FONTS:
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
   d) Always send: SUMMARY.txt and the results drawer (or at least every file with a FAIL line in it), and
      your machine: model (A1200 or THEA1200 with its FPGA core and version), CPU and clock, RAM chip/fast,
      accelerator card if any, Kickstart and Workbench version, graphics card or Indivision/scandoubler, how it
      is connected to the display (monitor, TV, HDMI adapter), the total time from SUMMARY.txt, and whether
      anything crashed, hung, or left the screen in a bad state.
      On the serial port: trace writes 9 lines at 9600 baud; if you have a terminal on it, send what it shows.

5. Where to send it
   Open an issue "Hardware report" on https://github.com/GameGyrafe/amigaesharp/issues (there is a
   template) and attach or paste the above. Photos of the screens are very helpful.

What is not tested: PowerPC, other accelerators, RTG cards, AmigaOS 3.2/3.9 specifics. You are
welcome to report those too; say what you used.

Nothing in this pack writes outside its own drawer except: sound writes three small files to RAM: and
deletes them again; iff writes RAM:esharp_test.iff (deleted too); asl by hand uses RAM:asl-open (you delete it);
RunAll uses T: for two small temporary files; no program touches the hard disk outside this drawer.
