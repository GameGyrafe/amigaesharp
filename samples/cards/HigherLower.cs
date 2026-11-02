using System;
using ESharp.Platform;

namespace CardsDemo;

public enum Outcome
{
    Playing,
    Won,
    Lost,
}

/// <summary>
/// Higher or lower: a card is showing, guess whether the next one has a higher or lower rank (aces low).
/// An equal rank is a push: the streak neither grows nor ends. A wrong guess ends the game; so does an
/// empty deck (the player wins when all 52 cards were turned over without a miss).
/// </summary>
public static class HigherLower
{
    public const int Suits = 4;
    public const int DeckSize = Suits * Deck.Ranks;

    public static byte[] Cards = new byte[DeckSize];
    public static int Next;        // index of the next card to turn over
    public static int Streak;      // correct guesses in a row
    public static int Best;
    public static Outcome State;
    public static int LastGuess;   // +1 higher, -1 lower, 0 none yet
    public static int LastResult;  // +1 right, 0 push, -1 wrong

    private static Rng _rng;

    public static int Current { get { return Cards[Next - 1]; } }

    public static int Previous { get { return Next >= 2 ? Cards[Next - 2] : -1; } }

    public static void Deal(uint seed)
    {
        _rng = Rng.FromSeed(seed);
        Span<byte> all = Cards;
        Deck.Fill(all, Suits);
        _rng.Shuffle(all);
        Next = 1;
        Streak = 0;
        State = Outcome.Playing;
        LastGuess = 0;
        LastResult = 0;
    }

    /// <summary>Turns over the next card for a guess of higher (true) or lower (false). Ignored once the game is over.</summary>
    public static void Guess(bool higher)
    {
        if (State != Outcome.Playing) return;
        int before = Deck.Rank(Cards[Next - 1]);
        int after = Deck.Rank(Cards[Next]);
        Next++;
        LastGuess = higher ? 1 : -1;
        if (after == before)
        {
            LastResult = 0;
        }
        else if ((after > before) == higher)
        {
            LastResult = 1;
            Streak++;
            if (Streak > Best) Best = Streak;
        }
        else
        {
            LastResult = -1;
            State = Outcome.Lost;
            return;
        }
        if (Next >= DeckSize) State = Outcome.Won;
    }
}
