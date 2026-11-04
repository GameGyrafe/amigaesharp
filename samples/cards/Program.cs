using System;
using ESharp;
using ESharp.Platform;

namespace CardsDemo;

/// <summary>
/// Phase 4: a playable card game on the Amiga screen. Higher or lower with a shuffled 52-card deck: the sprite sheet
/// (cards.png, drawn by tools/CardArt) is converted by `esharp assets` and blitted with a mask, the mouse picks
/// Higher or Lower, Esc quits. The game rules live in HigherLower.cs and run under plain dotnet too.
/// Prints "ok NAME" or "FAIL NAME" for self-checks, then "SHOT" and plays until Esc or 10 seconds without input.
/// </summary>
public static class Program
{
    const int CardW = 32;
    const int CardH = 40;
    // Palette indices. The high-resolution sheet uses entries 0..55, so on AROS these move up to 56..60 (see OpenHd).
    static int Table = 8, ButtonColor = 9, PipOn = 10, PipOff = 11, Shadow = 12;
    const int HigherX = 30, LowerX = 190, ButtonY = 130, ButtonW = 100, ButtonH = 30;
    const int CurrentX = 144, CurrentY = 70;

    /// <summary>Screen scale: the layout is in 320 x 200 logical pixels; with Z = 2 (AROS) the screen is 640 x 400 and every pixel is drawn 2 x 2.</summary>
    static int Z = 1;

    /// <summary>True when the 64 x 80 sheet is in use (AROS with an RTG mode): the cards are drawn at that size, not scaled.</summary>
    static bool Hd;

    static void Fill(int x, int y, int w, int h, int color)
    {
        Video.FillRect(x * Z, y * Z, w * Z, h * Z, color);
    }

    static void SetColors()
    {
        Video.SetPalette(Sheet.CardsPalette);
        Video.SetColor(Table, 0, 7, 2);
        Video.SetColor(ButtonColor, 2, 10, 3);
        Video.SetColor(PipOn, 15, 12, 0);
        Video.SetColor(PipOff, 5, 5, 5);
        Video.SetColor(Shadow, 0, 3, 1);
    }

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>Palette index of a sheet pixel, decoded from the planar data.</summary>
    static int SheetIndex(int x, int y)
    {
        int bytesPerRow = (Sheet.CardsWidth + 15) / 16 * 2;
        int value = 0;
        for (int plane = 0; plane < Sheet.CardsDepth; plane++)
        {
            int offset = plane * bytesPerRow * Sheet.CardsHeight + y * bytesPerRow + (x >> 3);
            if ((Sheet.CardsPlanes[offset] & (0x80 >> (x & 7))) != 0) value |= 1 << plane;
        }
        return value;
    }

    static bool SheetOpaque(int x, int y)
    {
        int bytesPerRow = (Sheet.CardsWidth + 15) / 16 * 2;
        return (Sheet.CardsMask[y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }

    static void Sprite(int column, int row, int x, int y)
    {
#if AROS
        if (Hd)
        {
            Video.BlitMasked(HdSheet.HdPlanes, HdSheet.HdMask, HdSheet.HdWidth, HdSheet.HdHeight, HdSheet.HdDepth,
                column * 2 * CardW, row * 2 * CardH, 2 * CardW, 2 * CardH, x * 2, y * 2);
            return;
        }
#endif
        Video.BlitMaskedScaled(Sheet.CardsPlanes, Sheet.CardsMask, Sheet.CardsWidth, Sheet.CardsHeight, Sheet.CardsDepth,
            column * CardW, row * CardH, CardW, CardH, x * Z, y * Z, Z);
    }

    static void DrawCard(int card, int x, int y)
    {
        Sprite(Deck.Rank(card), Deck.Suit(card), x, y);
    }

    static void Button(int x, bool up)
    {
        Fill(x + 3, ButtonY + 3, ButtonW, ButtonH, Shadow);
        Fill(x, ButtonY, ButtonW, ButtonH, ButtonColor);
        // The arrow cells are 32 wide; the 13 pixel arrow sits in the middle of the cell.
        Sprite(up ? 1 : 2, 4, x + (ButtonW - CardW) / 2, ButtonY + (ButtonH - CardH) / 2);
    }

    static void Draw()
    {
        Fill(0, 0, 320, 200, Table);
        if (HigherLower.Next < HigherLower.DeckSize) Sprite(0, 4, 60, CurrentY); // the deck: a card back
        int shown = 0;
        for (int i = HigherLower.Next - 2; i >= 0 && shown < 8; i--)
        {
            DrawCard(HigherLower.Cards[i], 16 + shown * 36, 16);
            shown++;
        }
        DrawCard(HigherLower.Current, CurrentX, CurrentY);
        if (HigherLower.State == Outcome.Playing)
        {
            Button(HigherX, true);
            Button(LowerX, false);
        }
        else
        {
            Sprite(HigherLower.State == Outcome.Won ? 3 : 4, 4, 232, CurrentY);
        }
        for (int i = 0; i < HigherLower.Best; i++)
            Fill(8 + i * 5, 176, 4, 4, i < HigherLower.Streak ? PipOn : PipOff);
    }

#if AROS
    /// <summary>
    /// The checks ran at 320 x 200. To play, open a 640 x 400 screen: on an RTG mode with the 64 x 80 high resolution sheet (real detail,
    /// not enlarged pixels), otherwise a native screen with the 32 x 40 sprites drawn 2 x 2.
    /// </summary>
    static void OpenLarge()
    {
        Video.Close();
        Z = 2;
        if (Video.HasRtg(640, 400) && Video.OpenRtg(640, 400, 6))
        {
            Hd = true;
            Table = 56; ButtonColor = 57; PipOn = 58; PipOff = 59; Shadow = 60;
            Video.SetPalette24(HdSheet.HdPalette);
            Video.SetColor24(Table, 0, 119, 34);
            Video.SetColor24(ButtonColor, 34, 170, 51);
            Video.SetColor24(PipOn, 255, 204, 0);
            Video.SetColor24(PipOff, 85, 85, 85);
            Video.SetColor24(Shadow, 0, 51, 17);
            return;
        }
        if (!Video.Open(640, 400, 4)) { Z = 1; Video.Open(320, 200, 4); }
        SetColors();
    }
#endif

    static bool Inside(int x, int y, int left, int top, int w, int h)
    {
        return x >= left && y >= top && x < left + w && y < top + h;
    }

    /// <summary>Reads input and applies it. Returns false when the player quits. <c>_acted</c> says whether anything happened.</summary>
    static bool _acted;

    static bool Step()
    {
        bool acted = false;
        Input.Poll();
        if (Input.LastKey == Input.KeyEscape) { _acted = true; return false; }
        if (Input.LastKey != 0) acted = true;
        if (Input.LeftPressed)
        {
            acted = true;
            int x = Input.MouseX / Z;
            int y = Input.MouseY / Z;
            if (HigherLower.State != Outcome.Playing)
            {
                HigherLower.Deal((uint)(Input.MouseX * 31 + Input.MouseY * 17 + HigherLower.Next + 1));
            }
            else if (Inside(x, y, HigherX, ButtonY, ButtonW, ButtonH))
            {
                HigherLower.Guess(true);
            }
            else if (Inside(x, y, LowerX, ButtonY, ButtonW, ButtonH))
            {
                HigherLower.Guess(false);
            }
        }
        _acted = acted;
        return true;
    }

    /// <summary>A number that depends on the order of the cards.</summary>
    static int Weighted()
    {
        int w = 0;
        for (int i = 0; i < HigherLower.DeckSize; i++) w += (i + 1) * HigherLower.Cards[i];
        return w;
    }

    /// <summary>Independent re-statement of the rules, used to cross-check HigherLower.</summary>
    static bool _expectLost;

    static int ExpectedStreakAlwaysHigher(Span<byte> cards)
    {
        int streak = 0;
        _expectLost = false;
        for (int i = 1; i < cards.Length; i++)
        {
            int a = cards[i - 1] % 13;
            int b = cards[i] % 13;
            if (b == a) continue;
            if (b > a) { streak++; continue; }
            _expectLost = true;
            return streak;
        }
        return streak;
    }

    [EntryPoint]
    public static void Main()
    {
        // ---- logic ----
        HigherLower.Deal(12345);
        bool unique = true;
        int sum = 0;
        for (int i = 0; i < HigherLower.DeckSize; i++)
        {
            sum += HigherLower.Cards[i];
            for (int j = 0; j < i; j++)
                if (HigherLower.Cards[i] == HigherLower.Cards[j]) unique = false;
        }
        Check(unique && sum == 51 * 52 / 2, "deal: 52 distinct cards");
        Check(Deck.Rank(14) == 1 && Deck.Suit(14) == 1 && Deck.Rank(51) == 12 && Deck.Suit(51) == 3, "rank and suit");

        int weighted = Weighted();
        HigherLower.Deal(12345);
        Check(Weighted() == weighted, "same seed gives the same shuffle");
        HigherLower.Deal(777);
        Check(Weighted() != weighted, "other seed shuffles again");

        byte[] eight = new byte[104];
        Check(Deck.Fill(eight, 8) == 104 && eight[103] == 103 && Deck.Suit(eight[103]) == 7, "deck with eight suits");

        HigherLower.Deal(2024);
        byte[] copy = new byte[HigherLower.DeckSize];
        Span<byte> snapshot = copy;
        for (int i = 0; i < HigherLower.DeckSize; i++) snapshot[i] = HigherLower.Cards[i];
        int expectStreak = ExpectedStreakAlwaysHigher(snapshot);
        int guesses = 0;
        while (HigherLower.State == Outcome.Playing && guesses < 100)
        {
            HigherLower.Guess(true);
            guesses++;
        }
        Check(HigherLower.Streak == expectStreak, "streak matches an independent count");
        Check((HigherLower.State == Outcome.Lost) == _expectLost && HigherLower.State != Outcome.Playing, "game ends on a miss");
        int frozen = HigherLower.Next;
        HigherLower.Guess(false);
        Check(HigherLower.Next == frozen, "no guesses after the end");

        // ---- screen ----
        bool opened = Video.Open(320, 200, 4);
        Check(opened, "screen opens");
        if (!opened) return;
        SetColors();

        HigherLower.Deal(99);
        Draw();
        Check(Video.GetPixel(HigherX + 5, ButtonY + 5) == ButtonColor && Video.GetPixel(LowerX + 5, ButtonY + 5) == ButtonColor, "buttons are drawn");
        Check(Video.GetPixel(HigherX + ButtonW + 1, ButtonY + 1) == Table, "button edge is exact");

        HigherLower.Guess(true);
        HigherLower.Guess(false);
        Draw();

        int cardIndex = HigherLower.Current;
        int sx = Deck.Rank(cardIndex) * CardW;
        int sy = Deck.Suit(cardIndex) * CardH;
        int wrong = 0;
        int transparent = 0;
        for (int y = 0; y < CardH; y++)
        {
            for (int x = 0; x < CardW; x++)
            {
                int expect = SheetOpaque(sx + x, sy + y) ? SheetIndex(sx + x, sy + y) : Table;
                if (!SheetOpaque(sx + x, sy + y)) transparent++;
                if (Video.GetPixel(CurrentX + x, CurrentY + y) != expect) wrong++;
            }
        }
        Check(wrong == 0, "masked card matches the sheet pixel for pixel");
        Check(transparent > 0 && Video.GetPixel(CurrentX, CurrentY) == Table, "masked corner shows the table");
        Check(Video.GetPixel(CurrentX + 16, CurrentY + 20) != Table, "card face is drawn");

        // ---- input (the host feeds scripted events; on the Amiga the mouse does) ----
#if !AMIGA
        HigherLower.Deal(99);
        int before = HigherLower.Next;
        Input.HostMove(5, 5);
        Input.HostPress();
        Step();
        Check(_acted && HigherLower.Next == before, "click outside the buttons does nothing");
        Input.HostMove(HigherX + 10, ButtonY + 10);
        Input.HostPress();
        Step();
        Check(HigherLower.Next == before + 1 && HigherLower.LastGuess == 1, "click on Higher guesses higher");
        int mid = HigherLower.Next;
        bool playing = HigherLower.State == Outcome.Playing;
        Input.HostMove(LowerX + 10, ButtonY + 10);
        Input.HostPress();
        Step();
        Check(!playing || HigherLower.Next == mid + 1 && HigherLower.LastGuess == -1, "click on Lower guesses lower");
        Input.HostKey(Input.KeyEscape);
        Check(!Step(), "Esc quits");
        Check(Step() && !_acted, "nothing queued: nothing happens");
#endif

        // A game in progress for the screenshot: the first seed where a simple strategy builds a streak of 3.
        for (uint seed = 1; seed < 100; seed++)
        {
            HigherLower.Deal(seed);
            while (HigherLower.State == Outcome.Playing && HigherLower.Streak < 3)
                HigherLower.Guess(Deck.Rank(HigherLower.Current) < 6);
            if (HigherLower.State == Outcome.Playing) break;
        }
#if AROS
        OpenLarge();
#endif
        Draw();
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();

        // ---- play until Esc or ten seconds without input ----
        int idle = 0;
#if AMIGA
        while (idle < 500)
        {
            int before = HigherLower.Next;
            if (!Step())
            {
                Dos.PutStr("quit\n");
                break;
            }
            if (Input.LeftPressed)
            {
                Dos.PutStr("click ");
                Dos.PutInt(Input.MouseX);
                Dos.PutStr(" ");
                Dos.PutInt(Input.MouseY);
                Dos.PutStr("\n");
                Dos.FlushOutput();
            }
            if (HigherLower.Next != before)
            {
                Dos.PutStr(HigherLower.LastGuess > 0 ? "guess higher: " : "guess lower: ");
                Dos.PutStr(HigherLower.LastResult > 0 ? "right\n" : HigherLower.LastResult == 0 ? "push\n" : "wrong\n");
                Dos.FlushOutput();
            }
            if (_acted)
            {
                idle = 0;
                Draw();
            }
            else
            {
                idle++;
            }
            Video.WaitFrames(1);
        }
#endif
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");
    }
}
