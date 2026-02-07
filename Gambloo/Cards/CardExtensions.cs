using System;

namespace BluesBar.Gambloo.Cards
{
    public static class CardExtensions
    {
        // Blackjack base value: A=11 (handle soft/hard in hand logic), face=10
        public static int BlackjackValue(this Card c)
        {
            return c.Rank switch
            {
                Rank.Ace => 11,
                Rank.King => 10,
                Rank.Queen => 10,
                Rank.Jack => 10,
                _ => (int)c.Rank // 2..10
            };
        }

        public static bool IsAce(this Card c) => c.Rank == Rank.Ace;
    }
}

