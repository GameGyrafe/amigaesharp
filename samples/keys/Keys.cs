using ESharp;
using ESharp.Platform;

namespace KeysSample;

/// <summary>
/// Raw keys to characters with the user's keymap (keymap.library, <see cref="Keys"/>). Prints "ok NAME" or
/// "FAIL NAME". The checks hold on any Latin keymap (the letter on a key differs between layouts, so they test that
/// the two directions agree rather than which letter it is). Then it opens a screen and waits up to 10 seconds for a
/// key (it prints SHOT first, which is when <c>esharp run --input</c> sends its steps): run with <c>--input "key 38"</c> (the X11 keycode of A) to see <see cref="Input.LastChar"/> type 'a', or <c>"key 50+38"</c> (Shift+A) for 'A'.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        Check(Keys.Open(), "keymap.library opens");
        int a = Keys.Char(0x20, 0);                              // the key right of Caps Lock: 'a' on US, German, UK; 'q' on French
        int shifted = Keys.Char(0x20, Keys.LeftShift);
        Dos.PutStr($"key 0x20 types {a}, with shift {shifted}\n");
        Check(a >= 'a' && a <= 'z' && shifted == a - 32, "a letter key types a letter, upper case with shift");
        Check(Keys.Char(0x20, Keys.CapsLock) == a - 32, "Caps Lock types upper case");
        Check(Keys.Char(0x40, 0) == ' ' && Keys.Char(0x44, 0) == 13 && Keys.Char(0x45, 0) == 27 && Keys.Char(0x41, 0) == 8,
            "space, return, escape and backspace");
        Check(Keys.Char(0x4C, 0) == 0, "cursor up types no single character");
        Check(Keys.Char(0x20 | 0x80, 0) == 0, "a key release types nothing");
        var (code, qual) = Keys.Find(a);
        Check(code == 0x20 && qual == 0, "Find goes back from the letter to its key");
        var (code2, qual2) = Keys.Find(shifted);
        Check(code2 == 0x20 && (qual2 & Keys.Shift) != 0, "Find gives the upper case letter with shift");
        bool qwerty = Keys.Char(0x10, 0) == 'q' && Keys.Char(0x11, 0) == 'w' && Keys.Char(0x15, 0) == 'y';
        Dos.PutStr(qwerty ? "keymap: qwerty\n" : "keymap: not qwerty\n");

        if (!Video.Open(320, 256, 2)) { Check(false, "a screen opens"); return; }
        Dos.PutStr("SHOT\n");                                     // esharp run --input sends its keys after this
        Dos.FlushOutput();
        int typed = 0, key = 0, held = 0;
        for (int i = 0; i < 500 && typed == 0; i++)                // about 10 seconds
        {
            Video.WaitFrames(1);
            Input.Poll();
            if (Input.LastKey != 0) { key = Input.LastKey; typed = Input.LastChar; held = Input.LastQualifier; }
        }
        Video.Close();
        if (key != 0)
        {
            Dos.PutStr($"pressed key {key} (qualifier {held}), typed {typed}\n");
            Check(typed == Keys.Char(key, held & ~Keys.Repeat), "a pressed key types what Keys.Char says");
        }
        else Dos.PutStr("no key pressed\n");
    }
}
