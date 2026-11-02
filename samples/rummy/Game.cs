using System;
using ESharp.Platform;

namespace RummyDemo;

public enum Phase
{
    PlayerDraw,
    PlayerDiscard,
    BotTurn,
    Over,
}

public enum Result
{
    None,
    PlayerWins,
    BotWins,
    Drawn,
}

/// <summary>
/// A two-player rummy: seven cards each, draw from the stock or take the top discard, then throw one card away.
/// Melds are sets (three or more of a rank) and runs (three or more in a row of one suit, ace low). Knock when your
/// unmatched cards (deadwood) are worth <see cref="KnockLimit"/> points or less: the lower deadwood wins, and a tie
/// goes to the player who did not knock. An empty stock draws the game. All state is static; the screen code only reads it.
/// </summary>
public static class Game
{
    public const int HandSize = 7;
    public const int KnockLimit = 10;
    public const int DeckSize = 52;

    public static Vec<byte> Player;
    public static Vec<byte> Bot;
    public static Vec<byte> Stock;      // the top is the last card
    public static Vec<byte> Pile;       // the discard pile, the top is the last card
    public static Phase Phase;
    public static Result Result;
    public static int PlayerDeadwoodAtEnd;
    public static int BotDeadwoodAtEnd;
    public static bool BotKnocked;
    public static bool PlayerKnocked;
    public static int Turns;

    private static Rng _rng;

    public static void Deal(uint seed)
    {
        Player.Destroy();
        Bot.Destroy();
        Stock.Destroy();
        Pile.Destroy();
        _rng = Rng.FromSeed(seed);
        byte[] deck = new byte[DeckSize];
        Span<byte> all = deck;
        Deck.Fill(all, 4);
        _rng.Shuffle(all);
        int next = 0;
        for (int i = 0; i < HandSize; i++)
        {
            Player.Add(all[next]); next++;
            Bot.Add(all[next]); next++;
        }
        Pile.Add(all[next]); next++;
        while (next < DeckSize) { Stock.Add(all[next]); next++; }
        Deck.Sort(Player.AsSpan());
        Deck.Sort(Bot.AsSpan());
        Phase = Phase.PlayerDraw;
        Result = Result.None;
        PlayerDeadwoodAtEnd = 0;
        BotDeadwoodAtEnd = 0;
        PlayerKnocked = false;
        BotKnocked = false;
        Turns = 0;
    }

    public static int PileTop { get { return Pile.Count == 0 ? -1 : Pile[Pile.Count - 1]; } }

    /// <summary>Total number of cards in play; always 52 after a deal.</summary>
    public static int CardCount { get { return Player.Count + Bot.Count + Stock.Count + Pile.Count; } }

    /// <summary>The deadwood the player could reach now (after the best discard when holding eight cards).</summary>
    public static int PlayerDeadwood
    {
        get
        {
            if (Player.Count > HandSize) return Rummy.BestDiscard(Player.AsSpan()).Deadwood;
            return Rummy.Deadwood(Player.AsSpan());
        }
    }

    // ---------------------------------------------------------------- the player's moves

    /// <summary>Draws the top stock card. Returns false if it is not the player's draw. An empty stock draws the game.</summary>
    public static bool DrawStock()
    {
        if (Phase != Phase.PlayerDraw) return false;
        if (!Draw(ref Player, true)) return false;
        Phase = Phase.PlayerDiscard;
        return true;
    }

    public static bool DrawPile()
    {
        if (Phase != Phase.PlayerDraw || Pile.Count == 0) return false;
        Draw(ref Player, false);
        Phase = Phase.PlayerDiscard;
        return true;
    }

    /// <summary>Throws away the card at <paramref name="index"/> of the player's hand and lets the bot move.</summary>
    public static bool Discard(int index)
    {
        if (Phase != Phase.PlayerDiscard || index < 0 || index >= Player.Count) return false;
        Throw(ref Player, index);
        Phase = Phase.BotTurn;
        return true;
    }

    /// <summary>Discards the best card and knocks, if the remaining deadwood is within the limit.</summary>
    public static bool Knock()
    {
        if (Phase != Phase.PlayerDiscard) return false;
        var (index, deadwood) = Rummy.BestDiscard(Player.AsSpan());
        if (deadwood > KnockLimit) return false;
        Throw(ref Player, index);
        PlayerKnocked = true;
        Finish();
        return true;
    }

    // ---------------------------------------------------------------- the bot

    /// <summary>One bot turn: take the pile card if it helps, then discard or knock.</summary>
    public static void BotMove()
    {
        if (Phase != Phase.BotTurn) return;
        Turns++;
        if (!TakeTurn(ref Bot, out bool knocked)) return;
        if (knocked)
        {
            BotKnocked = true;
            Finish();
            return;
        }
        Phase = Phase.PlayerDraw;
    }

    /// <summary>Plays the player's turn with the bot's logic (used to test whole games).</summary>
    public static void AutoPlayerMove()
    {
        if (Phase != Phase.PlayerDraw) return;
        Turns++;
        if (!TakeTurn(ref Player, out bool knocked)) return;
        if (knocked)
        {
            PlayerKnocked = true;
            Finish();
            return;
        }
        Phase = Phase.BotTurn;
    }

    /// <summary>Draws and discards for the hand. Returns false when the game ended (empty stock).</summary>
    private static bool TakeTurn(ref Vec<byte> hand, out bool knocked)
    {
        knocked = false;
        bool fromPile = false;
        if (Pile.Count > 0)
        {
            int before = Rummy.Deadwood(hand.AsSpan());
            hand.Add((byte)PileTop);
            int after = Rummy.BestDiscard(hand.AsSpan()).Deadwood;
            hand.RemoveAt(hand.Count - 1);
            fromPile = after < before;
        }
        if (!Draw(ref hand, !fromPile)) return false;
        var (index, deadwood) = Rummy.BestDiscard(hand.AsSpan());
        Throw(ref hand, index);
        knocked = deadwood <= KnockLimit;
        return true;
    }

    // ---------------------------------------------------------------- shared steps

    private static bool Draw(ref Vec<byte> hand, bool fromStock)
    {
        if (fromStock)
        {
            if (Stock.Count == 0)
            {
                Result = Result.Drawn;
                Phase = Phase.Over;
                return false;
            }
            hand.Add(Stock[Stock.Count - 1]);
            Stock.RemoveAt(Stock.Count - 1);
        }
        else
        {
            hand.Add(Pile[Pile.Count - 1]);
            Pile.RemoveAt(Pile.Count - 1);
        }
        Deck.Sort(hand.AsSpan());
        return true;
    }

    private static void Throw(ref Vec<byte> hand, int index)
    {
        Pile.Add(hand[index]);
        hand.RemoveAt(index);
    }

    private static void Finish()
    {
        PlayerDeadwoodAtEnd = Rummy.Deadwood(Player.AsSpan());
        BotDeadwoodAtEnd = Rummy.Deadwood(Bot.AsSpan());
        int diff = PlayerDeadwoodAtEnd - BotDeadwoodAtEnd;   // negative: the player has less deadwood; the bot knocking with a tie loses
        // The knocker needs strictly less deadwood than the other player; a tie or worse goes to the other side.
        bool playerWins = PlayerKnocked ? diff < 0 : diff <= 0;
        Result = playerWins ? Result.PlayerWins : Result.BotWins;
        Phase = Phase.Over;
    }
}
