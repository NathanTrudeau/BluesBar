using System;
using System.Collections.Generic;
using System.Linq;


namespace BluesBar.Gambloo.Cards
{

    public enum Suit { Clubs, Diamonds, Hearts, Spades }

    public enum Rank
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14
    }

    public sealed class Shoe
    {
        private readonly Random _rng;
        private readonly List<Card> _cards = new();

        public int DeckCount { get; private set; }
        public int Count => _cards.Count;

        public CardTheme Theme { get; private set; }

        public Shoe(int decks = 6, CardTheme? theme = null, int? seed = null, bool autoBuildAndShuffle = true)
        {
            DeckCount = Math.Max(1, decks);
            Theme = theme ?? new CardTheme();
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();

            if (autoBuildAndShuffle)
            {
                Reset(DeckCount);
                Shuffle();
            }
        }

        public void SetTheme(CardTheme theme) => Theme = theme;

        public void Reset(int decks)
        {
            DeckCount = Math.Max(1, decks);
            _cards.Clear();

            for (int d = 0; d < DeckCount; d++)
            {
                foreach (Suit s in Enum.GetValues(typeof(Suit)))
                {
                    for (int r = 2; r <= 14; r++)
                        _cards.Add(new Card(s, (Rank)r));
                }
            }
        }

        public void Shuffle()
        {
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
                Reset(DeckCount);
                Shuffle();
            }

            var c = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            return c;
        }

        public IReadOnlyList<Card> PeekAll() => _cards.ToList();
    }

    public readonly struct Card
    {
        public Suit Suit { get; }
        public Rank Rank { get; } // 2..14

        public Card(Suit suit, Rank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        public bool IsRed => Suit == Suit.Diamonds || Suit == Suit.Hearts;

        public string RankText => Rank switch
        {
            Rank.Jack => "J",
            Rank.Queen => "Q",
            Rank.King => "K",
            Rank.Ace => "A",
            _ => ((int)Rank).ToString()
        };

        public string SuitText => Suit switch
        {
            Suit.Clubs => "♣",
            Suit.Diamonds => "♦",
            Suit.Hearts => "♥",
            Suit.Spades => "♠",
            _ => "?"
        };

        public string ShortText => $"{RankText}{SuitText}";

        public int CompareRank(Card other) => ((int)Rank).CompareTo((int)other.Rank);
    }
}
