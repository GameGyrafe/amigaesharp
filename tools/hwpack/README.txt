E# hardware test pack
=====================

Thank you for testing. E# turns C# into native Amiga programs; so far everything has only run
in an emulator. Your run is the first on real hardware.

1. What you need
   An Amiga 1200 (68020 or better, AGA, 2 MB chip RAM, Kickstart 3.1, hard disk or CF).
   Other machines are welcome to report (see section 4) but are not the target.

2. Run it
   - Copy this whole drawer to the Amiga (anywhere on the hard disk), open a Shell, CD into it.
   - Type:  Protect RunAll +s     (only needed once, if the script was copied from a PC)
   - Type:  Execute RunAll
   The programs run one after the other and write into the results drawer.
   Several open a screen. Look at each, press Esc (cards, rummy) or wait a few seconds.

   A whole run takes about two minutes in an emulator; a real machine may be slower.
   Expected: no line starting FAIL anywhere. In an emulated A1200 (Kickstart 3.1) the programs print
   this many "ok" lines: phase2 32, memory 19, generics 24, properties 18, operators 20, language 28,
   collections 20, inherit 24, lambdas 16, linq 37, screen 17, aga 6, rtg 5, sound 21, controls 12
   (9 if lowlevel.library is not in Libs:), cards 15, rummy 28 (hello and hwinfo print text, not ok lines). To count: Search results/memory.txt "ok " .

3. Please tell us
   a) SUMMARY.txt (it starts with what hwinfo found out about your machine), and the results drawer, or at least any file in it that has a line starting FAIL.
   b) Your machine: model, CPU (and clock), RAM chip/fast, accelerator card if any, Kickstart and
      Workbench version, graphics card or Indivision/scandoubler, how it is connected to the
      display (monitor, TV, HDMI adapter).
   c) By eye:
      screen   a picture of cards on a coloured background, not garbled, no flicker      yes / no
      aga      a smooth 256 colour image                                                  yes / no
      rtg      says no RTG card (normal on a stock A1200) or runs on yours                note it
      sound    clean tones, the pitch rises/falls as it should, no crackle                yes / no
      controls the "clock ... ticks per second" line (709379 on PAL) and the "port 1 kind" line    note them
      cards    cards drawn right; mouse moves; clicking Higher/Lower works                yes / no
      rummy    cards drawn right; you can draw, discard, and the bot replies              yes / no
      Roughly how long did the bot take to reply in rummy (seconds)?
      Did anything crash, hang, or leave the screen in a bad state?
   d) The whole run took how long, start to end?

4. Where to send it
   Open an issue "Hardware report" on https://github.com/GameGyrafe/amigaesharp/issues (there is a
   template) and attach or paste the above. Photos of the screens are very helpful.

What is not tested: PowerPC, other accelerators, RTG cards, AmigaOS 3.2/3.9 specifics. You are
welcome to report those too; say what you used.

Nothing in this pack writes outside its own drawer except the sound test, which writes three
small files to RAM: and deletes them again.
