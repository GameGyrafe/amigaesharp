using System;
using ESharp;
using ESharp.Platform;

namespace RummyDemo;

/// <summary>
/// A playable two-player rummy against a simple bot, on the Amiga screen (320x200, 16 colours). The card art is the
/// shared sprite sheet from samples/cards (blitted with a mask), the text is the built-in <see cref="Font"/>, the mouse
/// plays: click the stock or the discard pile to draw, click a card of yours to throw it away, click KNOCK to end
/// the round when your unmatched cards are worth 10 or less. Esc quits. The rules live in Game.cs and
/// ESharp.Platform.Rummy and run under plain dotnet too.
/// Prints "ok NAME" or "FAIL NAME" for the self-checks, then "SHOT", then plays until Esc or 10 seconds without input.
/// </summary>
public static class Program
{
    const int CardW = 32, CardH = 40;
    const int Table = 8, ButtonColor = 9, Group1 = 10, Group2 = 11, Group3 = 12, Ink = 13, Shadow = 14, Gold = 15;
    const int BotY = 6, MiddleY = 60, HandY = 150, BarY = 192;
    const int StockX = 108, PileX = 160;
    const int KnockX = 232, KnockY = 62, KnockW = 72, KnockH = 24;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    // ---------------------------------------------------------------- drawing

    static void Sprite(int column, int row, int x, int y)
    {
        Video.BlitMasked(Sheet.CardsPlanes, Sheet.CardsMask, Sheet.CardsWidth, Sheet.CardsHeight, Sheet.CardsDepth,
            column * CardW, row * CardH, CardW, CardH, x, y);
    }

    static void DrawCard(int card, int x, int y)
    {
        Sprite(Deck.Rank(card), Deck.Suit(card), x, y);
    }

    static void CardBack(int x, int y)
    {
        Sprite(0, 4, x, y);
    }

    static int HandX(int index) { return 16 + index * 36; }

    static int BotX(int index) { return 40 + index * 24; }

    static int GroupColor(int group) => group switch { 1 => Group1, 2 => Group2, 3 => Group3, _ => Table };

    static string Message() => Game.Phase switch
    {
        Phase.PlayerDraw => "YOUR TURN: DRAW FROM THE STOCK OR THE PILE",
        Phase.PlayerDiscard => "THROW AWAY A CARD, OR KNOCK",
        Phase.BotTurn => "THE BOT IS THINKING...",
        _ => Game.Result switch
        {
            Result.PlayerWins => "YOU WIN! CLICK TO PLAY AGAIN",
            Result.BotWins => "THE BOT WINS. CLICK TO PLAY AGAIN",
            _ => "NO CARDS LEFT: A DRAW. CLICK TO PLAY AGAIN",
        },
    };

    static void Draw()
    {
        Video.FillRect(0, 0, 320, 200, Table);

        // The bot's hand: backs, or faces once the round is over.
        for (int i = 0; i < Game.Bot.Count; i++)
        {
            if (Game.Phase == Phase.Over) DrawCard(Game.Bot[i], BotX(i), BotY);
            else CardBack(BotX(i), BotY);
        }

        // Stock and discard pile.
        if (Game.Stock.Count > 0) CardBack(StockX, MiddleY);
        else Video.FillRect(StockX, MiddleY, CardW, CardH, Shadow);
        if (Game.PileTop >= 0) DrawCard(Game.PileTop, PileX, MiddleY);
        Font.Draw($"STOCK {Game.Stock.Count}", StockX - 8, MiddleY + CardH + 4, Ink, 1);
        Font.Draw("PILE", PileX, MiddleY + CardH + 4, Ink, 1);

        if (Game.Phase == Phase.PlayerDiscard)
        {
            Video.FillRect(KnockX + 3, KnockY + 3, KnockW, KnockH, Shadow);
            Video.FillRect(KnockX, KnockY, KnockW, KnockH, ButtonColor);
            Font.Draw("KNOCK", KnockX + (KnockW - Font.Width("KNOCK", 2)) / 2, KnockY + 5, Ink, 2);
        }

        Font.Draw(Message(), 8, 118, Ink, 1);
        Font.Draw($"DEADWOOD {Game.PlayerDeadwood}", 8, 132, Gold, 1);

        // The player's hand, with a coloured bar under every card that belongs to a meld.
        byte[] groups = new byte[Rummy.MaxHand];
        Span<byte> group = groups;
        Rummy.Melds(Game.Player.AsSpan(), group);
        for (int i = 0; i < Game.Player.Count; i++)
        {
            DrawCard(Game.Player[i], HandX(i), HandY);
            Video.FillRect(HandX(i), BarY, CardW, 4, GroupColor(group[i]));
        }
    }

    static bool Inside(int x, int y, int left, int top, int w, int h)
    {
        return x >= left && y >= top && x < left + w && y < top + h;
    }

    /// <summary>The index of the player's card under the point, or -1.</summary>
    static int HandCardAt(int x, int y)
    {
        for (int i = Game.Player.Count - 1; i >= 0; i--)
            if (Inside(x, y, HandX(i), HandY, CardW, CardH)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- input

    static bool _acted;

    /// <summary>Reads input and applies it. Returns false when the player quits.</summary>
    static bool Step()
    {
        bool acted = false;
        Input.Poll();
        if (Input.LastKey == Input.KeyEscape) { _acted = true; return false; }
        if (Input.LastKey != 0) acted = true;
        if (Input.LeftPressed)
        {
            acted = true;
            int x = Input.PressX;
            int y = Input.PressY;
            switch (Game.Phase)
            {
                case Phase.Over:
                    Game.Deal((uint)(x * 31 + y * 17 + Game.Turns + 1));
                    break;
                case Phase.PlayerDraw:
                    if (Inside(x, y, StockX, MiddleY, CardW, CardH)) Game.DrawStock();
                    else if (Inside(x, y, PileX, MiddleY, CardW, CardH)) Game.DrawPile();
                    break;
                case Phase.PlayerDiscard:
                {
                    int card = HandCardAt(x, y);
                    if (card >= 0) Game.Discard(card);
                    else if (Inside(x, y, KnockX, KnockY, KnockW, KnockH)) Game.Knock();
                    break;
                }
            }
        }
        _acted = acted;
        return true;
    }

    // ---------------------------------------------------------------- self-checks

    static int Count(ReadOnlySpan<byte> cards, Func<int, bool> test)
    {
        int n = 0;
        for (int i = 0; i < cards.Length; i++) if (test(cards[i])) n++;
        return n;
    }

    static bool AllDistinct()
    {
        byte[] seen = new byte[Game.DeckSize];
        int total = 0;
        for (int i = 0; i < Game.Player.Count; i++) { seen[Game.Player[i]]++; total++; }
        for (int i = 0; i < Game.Bot.Count; i++) { seen[Game.Bot[i]]++; total++; }
        for (int i = 0; i < Game.Stock.Count; i++) { seen[Game.Stock[i]]++; total++; }
        for (int i = 0; i < Game.Pile.Count; i++) { seen[Game.Pile[i]]++; total++; }
        for (int i = 0; i < Game.DeckSize; i++) if (seen[i] != 1) return false;
        return total == Game.DeckSize;
    }

    /// <summary>Plays a whole game with the bot's logic on both sides. True when it ended cleanly with all 52 cards accounted for.</summary>
    static bool PlayBotGame(uint seed, out int moves)
    {
        Game.Deal(seed);
        moves = 0;
        while (Game.Phase != Phase.Over && moves < 300)
        {
            if (Game.CardCount != Game.DeckSize) return false;
            if (Game.Phase == Phase.PlayerDraw) Game.AutoPlayerMove();
            else Game.BotMove();
            moves++;
        }
        return Game.Phase == Phase.Over && Game.Result != Result.None && AllDistinct();
    }

    static void RulesChecks()
    {
        // Cards: suit * 13 + rank (ace = 0). Hand: A 2 3 of spades, three fives, the king of spades.
        byte[] cards = new byte[7];
        cards[0] = 0; cards[1] = 1; cards[2] = 2;                    // A 2 3 of spades
        cards[3] = 4; cards[4] = 13 + 4; cards[5] = 26 + 4;          // 5 of spades, hearts, diamonds
        cards[6] = 12;                                              // K of spades
        Span<byte> hand = cards;
        Check(Rummy.Points(0) == 1 && Rummy.Points(8) == 9 && Rummy.Points(9) == 10 && Rummy.Points(12) == 10 && Rummy.Points(13 + 10) == 10, "card points");
        Check(Rummy.Deadwood(hand) == 10, "deadwood leaves only the king");
        byte[] groups = new byte[7];
        Span<byte> g = groups;
        int dw = Rummy.Melds(hand, g);
        Check(dw == 10 && g[6] == 0 && g[0] != 0 && g[0] == g[1] && g[1] == g[2] && g[3] != 0 && g[3] == g[4] && g[3] == g[5] && g[0] != g[3], "melds are numbered per group");

        cards[0] = 3; cards[1] = 4; cards[2] = 5;                    // 4 5 6 of spades
        Check(Rummy.IsMeld(hand.Slice(0, 3)), "run of three");
        Check(!Rummy.IsMeld(hand.Slice(1, 3)), "two ranks of a run plus a stray card are no meld");
        byte[] queenKingAce = new byte[3];
        queenKingAce[0] = 11; queenKingAce[1] = 12; queenKingAce[2] = 0;
        Check(!Rummy.IsMeld(queenKingAce), "ace is low only");
        byte[] twoFives = new byte[3];
        twoFives[0] = 4; twoFives[1] = 17; twoFives[2] = 4;
        Check(!Rummy.IsMeld(twoFives), "a set needs different suits");

        cards[0] = 0; cards[1] = 1; cards[2] = 2;
        var (index, left) = Rummy.BestDiscard(hand);
        Check(index == 6 && left == 0, "best discard is the king");
        byte[] junk = new byte[7];
        junk[0] = 0; junk[1] = 15; junk[2] = 30; junk[3] = 45; junk[4] = 7; junk[5] = 22; junk[6] = 37;
        Check(Rummy.Deadwood(junk) == 1 + 3 + 5 + 7 + 8 + 10 + 10, "no melds: every point counts");
        Check(Count(hand, c => Rummy.Points(c) >= 10) == 1 && Count(hand, c => Deck.Suit(c) == 0) == 5, "counting with lambdas");
    }

    static void GameChecks()
    {
        Game.Deal(4242);
        Check(Game.Player.Count == 7 && Game.Bot.Count == 7 && Game.Pile.Count == 1 && Game.Stock.Count == 37 && Game.CardCount == 52, "deal: 7 + 7 + pile + stock");
        Check(AllDistinct(), "deal: 52 distinct cards");
        Check(Game.Phase == Phase.PlayerDraw, "player moves first");
        int firstCard = Game.Player[0];
        Game.Deal(4242);
        Check(Game.Player[0] == firstCard, "same seed deals the same hand");
        Check(!Game.Discard(0) && !Game.Knock(), "cannot discard or knock before drawing");

        int stockBefore = Game.Stock.Count;
        Check(Game.DrawStock() && Game.Player.Count == 8 && Game.Stock.Count == stockBefore - 1 && Game.Phase == Phase.PlayerDiscard, "draw from the stock");
        Check(!Game.DrawStock(), "no second draw");
        int pileBefore = Game.Pile.Count;
        Check(Game.Discard(3) && Game.Player.Count == 7 && Game.Pile.Count == pileBefore + 1 && Game.Phase == Phase.BotTurn, "discard hands the move to the bot");
        Game.BotMove();
        Check(Game.Bot.Count == 7 && Game.CardCount == 52 && (Game.Phase == Phase.PlayerDraw || Game.Phase == Phase.Over), "the bot draws and discards");

        Game.Deal(4242);
        Game.DrawPile();
        Check(Game.Player.Count == 8 && Game.Pile.Count == 0 && Game.PileTop == -1, "draw from the pile");

        int finished = 0, wins = 0, losses = 0, draws = 0;
        int longest = 0;
        bool clean = true;
#if AMIGA
        const int games = 3;   // each game is a few hundred hand analyses: keep the 68000 run short
#else
        const int games = 40;
#endif
        for (uint seed = 1; seed <= games; seed++)
        {
            if (!PlayBotGame(seed, out int moves)) { clean = false; continue; }
            finished++;
            if (moves > longest) longest = moves;
            switch (Game.Result)
            {
                case Result.PlayerWins: wins++; break;
                case Result.BotWins: losses++; break;
                default: draws++; break;
            }
        }
        Check(clean && finished == games, "bot-against-bot games all end cleanly");
#if !AMIGA
        Check(wins + losses + draws == games && wins > 0 && losses > 0, "both sides win some games");
#endif
        Check(longest < 300, "games are not endless");
        Game.Deal(9);
    }

    [EntryPoint]
    public static void Main()
    {
        RulesChecks();
        GameChecks();

        // ---- screen ----
        bool opened = Video.Open(320, 200, 4);
        Check(opened, "screen opens");
        if (!opened) return;
        Video.SetPalette(Sheet.CardsPalette);
        Video.SetColor(Table, 0, 7, 2);
        Video.SetColor(ButtonColor, 2, 10, 3);
        Video.SetColor(Group1, 15, 12, 0);
        Video.SetColor(Group2, 4, 12, 15);
        Video.SetColor(Group3, 15, 6, 12);
        Video.SetColor(Ink, 15, 15, 15);
        Video.SetColor(Shadow, 0, 3, 1);
        Video.SetColor(Gold, 15, 14, 4);

        Game.Deal(4242);
        Draw();
        Check(Video.GetPixel(2, 2) == Table, "table is drawn");
        Check(Video.GetPixel(StockX + 16, MiddleY + 20) != Table && Video.GetPixel(PileX + 16, MiddleY + 20) != Table, "stock and pile are drawn");
        Check(Video.GetPixel(HandX(0) + 16, HandY + 20) != Table && Video.GetPixel(HandX(6) + 16, HandY + 20) != Table, "the hand is drawn");
        Check(Video.GetPixel(HandX(7) + 16, HandY + 20) == Table, "no eighth card yet");

        // ---- input (the host feeds scripted events; on the Amiga the mouse does) ----
#if !AMIGA
        Input.HostMove(5, 5);
        Input.HostPress();
        Step();
        Check(_acted && Game.Phase == Phase.PlayerDraw, "click on the table does nothing");
        Input.HostMove(StockX + 4, MiddleY + 4);
        Input.HostPress();
        Step();
        Check(Game.Phase == Phase.PlayerDiscard && Game.Player.Count == 8, "click on the stock draws");
        Draw();
        Check(Video.GetPixel(KnockX + 4, KnockY + 4) == ButtonColor && Video.GetPixel(HandX(7) + 16, HandY + 20) != Table, "eighth card and knock button appear");
        Input.HostMove(HandX(2) + 4, HandY + 4);
        Input.HostPress();
        Step();
        Check(Game.Phase == Phase.BotTurn && Game.Player.Count == 7, "click on a card discards it");
        Game.BotMove();
        Check(Game.Phase == Phase.PlayerDraw || Game.Phase == Phase.Over, "bot replies");
        Input.HostKey(Input.KeyEscape);
        Check(!Step(), "Esc quits");
        Check(Step() && !_acted, "nothing queued: nothing happens");
#endif

        // A mid-game position for the screenshot.
        Game.Deal(4242);
        Game.DrawStock();
        Draw();
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();

        // ---- play until Esc or ten seconds without input ----
        int idle = 0;
#if AMIGA
        while (idle < 500)
        {
            if (Game.Phase == Phase.BotTurn)
            {
                Draw();
                Video.WaitFrames(40);
                Game.BotMove();
                Dos.PutStr("bot moved\n");
                Dos.FlushOutput();
                Draw();
                idle = 0;
                continue;
            }
            Phase before = Game.Phase;
            if (!Step())
            {
                Dos.PutStr("quit\n");
                break;
            }
            if (Input.LeftPressed)
            {
                Dos.PutStr("click ");
                Dos.PutInt(Input.PressX);
                Dos.PutStr(" ");
                Dos.PutInt(Input.PressY);
                Dos.PutStr("\n");
                Dos.FlushOutput();
            }
            if (Game.Phase != before)
            {
                Dos.PutStr(Game.Phase switch
                {
                    Phase.PlayerDiscard => "player drew\n",
                    Phase.BotTurn => "player discarded\n",
                    Phase.Over => "round over\n",
                    _ => "new round\n",
                });
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
