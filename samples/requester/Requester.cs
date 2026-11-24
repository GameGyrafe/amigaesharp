using ESharp;
using ESharp.Platform;

namespace RequesterSample;

/// <summary>
/// <see cref="Requester.Show"/>: a message box on every target (EasyRequest on the Amiga, SDL_ShowMessageBox on SDL). The program
/// prints SHOT, shows "Quit the game?" with the buttons Yes, Maybe and No, and checks the answer against ANSWER=n (the button a
/// person or --input picks, counted from the left; default 2, the last button, which is also the answer when no box can be shown,
/// as on SDL headless). SCREEN shows the box on a 320x256 game screen opened with <see cref="Video"/> instead of the Workbench.
/// On the host the answer is preset with Requester.HostSet.
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
        int expected = Args.Int("ANSWER", 2);
#if !AMIGA && !SDL
        Requester.HostSet(expected);
#endif
        Check(Requester.Count("Yes|Maybe|No") == 3 && Requester.Count("") == 1 && Requester.Count("OK") == 1, "button counts");
        bool screen = Args.Has("SCREEN");
        if (screen)
        {
            Check(Video.Open(320, 256, 2), "game screen opens");
            Video.SetColor(0, 0, 0, 6);
            Video.SetColor(1, 15, 15, 15);
            Video.SetColor(2, 15, 8, 0);
            Video.SetColor(3, 0, 10, 4);
            Video.FillRect(0, 0, 320, 256, 0);
            Video.FillRect(20, 20, 280, 40, 2);
            Video.FillRect(20, 200, 280, 36, 3);
        }
        Dos.PutStr("SHOT\n");                                     // esharp run --screenshot and --input act after this
        Dos.FlushOutput();
        int answer = Requester.Show("E# requester", "Quit the game?\n100% sure? (% as typed)", "Yes|Maybe|No");
        Dos.PutStr($"answer {answer}\n");
        Check(answer == expected, "the chosen button");
        if (screen) Video.Close();
    }
}
