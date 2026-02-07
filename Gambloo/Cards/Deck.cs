using System;
using System.Collections.Generic;
using System.Linq;

namespace BluesBar.Gambloo.Cards
{
    public sealed class Deck
    {
        private readonly Random _rng = new Random();
        private readonly List<Card> _cards = new List<Card>(52);

        public int Count => _cards.Count;

        public Deck(bool autoBuildAndShuffle = true)
        {
            if (autoBuildAndShuffle)
            {
                Reset();
                Shuffle();
            }
        }

        public void Reset()
        {
            _cards.Clear();
            foreach (Suit s in Enum.GetValues(typeof(Suit)))
            {
                for (int r = 2; r <= 14; r++)
                    _cards.Add(new Card(s, (Rank)r));
            }
        }

        public void Shuffle()
        {
            // Fisher–Yates
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(0, i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
        }

        public Card Draw()
        {
            if (_cards.Count == 0)
            {
                Reset();
                Shuffle();
            }

            var c = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            return c;
        }

        public IEnumerable<Card> PeekAll() => _cards.ToList();
    }
}
