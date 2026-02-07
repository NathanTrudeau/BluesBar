using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BluesBar.Gambloo;
using BluesBar.Gambloo.Cards;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class BlackjackScene : UserControl, IGamblooScene
    {
        public string SceneId => "blackjack";
        public string DisplayName => "Blackjack";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Shoe _shoe = new Shoe(decks: 6, theme: new CardTheme());
        private readonly List<Card> _player = new();
        private readonly List<Card> _dealer = new();

        private long _bet = 0;
        private bool _inRound = false;
        private bool _hideHole = true;

        public BlackjackScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                RefreshWalletHud();
                ResetUi();
            };
        }

        public void OnShown() => RefreshWalletHud();
        public void OnHidden() { }

        // -------------------------
        // UI helpers
        // -------------------------
        private async Task FlipToFaceAsync(Border cardBorder, string newText, Brush newForeground, Brush newBackground)
        {
            // shrink X to 0
            if (cardBorder.RenderTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(1, 1);
                cardBorder.RenderTransform = st;
                cardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var tcs1 = new TaskCompletionSource<bool>();
            var a1 = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            a1.Completed += (_, __) => tcs1.TrySetResult(true);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a1);
            await tcs1.Task;

            // swap content at midpoint
            if (cardBorder.Child is TextBlock tb)
            {
                tb.Text = newText;
                tb.Foreground = newForeground;
            }
            cardBorder.Background = newBackground;

            // expand back
            var tcs2 = new TaskCompletionSource<bool>();
            var a2 = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            a2.Completed += (_, __) => tcs2.TrySetResult(true);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a2);
            await tcs2.Task;
        }

        private FrameworkElement CreateCardView(string text, bool isFaceUp, bool isRed, double w = 110, double h = 150)
        {
            var border = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 12, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)),
                Background = isFaceUp
                    ? new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20))   // face
                    : new SolidColorBrush(Color.FromRgb(0x0E, 0x1A, 0x2F)),  // back
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.40
                },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };

            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isFaceUp
                    ? (isRed ? Brushes.IndianRed : Brushes.White)
                    : new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)), // back “BB” accent
            };

            border.Child = tb;
            return border;
        }

        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            WalletText.Text = $"Savings: {p.Coins:N0} Coins";
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            DealButton.IsEnabled = !busy && !_inRound;
            HitButton.IsEnabled = !busy && _inRound;
            StandButton.IsEnabled = !busy && _inRound;
            DoubleButton.IsEnabled = !busy && _inRound;

            BetTextBox.IsEnabled = !busy && !_inRound;
        }

        private void SetOutcome(string msg, Brush? color = null)
        {
            OutcomeText.Text = msg;
            OutcomeText.Foreground = color ?? Brushes.White;
        }

        private bool TryGetBet(out long bet)
        {
            bet = 0;
            return long.TryParse(BetTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bet);
        }

        private void BetPlus1k_Click(object sender, RoutedEventArgs e) => AddToBet(1_000);
        private void BetPlus10k_Click(object sender, RoutedEventArgs e) => AddToBet(10_000);

        private void AddToBet(long add)
        {
            if (!TryGetBet(out var cur)) cur = 0;
            BetTextBox.Text = (cur + add).ToString(CultureInfo.InvariantCulture);
        }

        // -------------------------
        // Card visuals (bigger, blue-back theme)
        // -------------------------
        private Border MakeCardVisual(Card? c, bool faceUp)
        {
            var border = new Border
            {
                Width = 110,
                Height = 160,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)),
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20)),
                Margin = new Thickness(0, 0, 12, 0),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            group.Children.Add(new TranslateTransform(0, 0));
            border.RenderTransform = group;

            var tb = new TextBlock
            {
                Text = "--",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = tb;

            if (faceUp && c.HasValue)
            {
                tb.Text = c.Value.ShortText;
                tb.Foreground = c.Value.IsRed ? Brushes.IndianRed : Brushes.White;
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
            }

            return border;
        }

        private void AnimateDeal(Border b, int delayMs)
        {
            var tg = (TransformGroup)b.RenderTransform;
            var tt = (TranslateTransform)tg.Children.OfType<TranslateTransform>().First();

            b.Opacity = 0;
            tt.X = -16;
            tt.Y = -10;

            var sb = new Storyboard();

            var op = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(op, b);
            Storyboard.SetTargetProperty(op, new PropertyPath(Border.OpacityProperty));
            sb.Children.Add(op);

            var ax = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(ax, tt);
            Storyboard.SetTargetProperty(ax, new PropertyPath(TranslateTransform.XProperty));
            sb.Children.Add(ax);

            var ay = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(ay, tt);
            Storyboard.SetTargetProperty(ay, new PropertyPath(TranslateTransform.YProperty));
            sb.Children.Add(ay);

            sb.Begin();
        }

        private void AnimateFlip(Border b, Action midpointSwap, int delayMs = 0)
        {
            var tg = (TransformGroup)b.RenderTransform;
            var st = (ScaleTransform)tg.Children.OfType<ScaleTransform>().First();

            var sb = new Storyboard();

            var squash = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            squash.Completed += (_, __) => midpointSwap();

            Storyboard.SetTarget(squash, st);
            Storyboard.SetTargetProperty(squash, new PropertyPath(ScaleTransform.ScaleXProperty));
            sb.Children.Add(squash);

            var unsquash = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs + 110),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(unsquash, st);
            Storyboard.SetTargetProperty(unsquash, new PropertyPath(ScaleTransform.ScaleXProperty));
            sb.Children.Add(unsquash);

            sb.Begin();
        }

        // -------------------------
        // Blackjack scoring
        // -------------------------
        private static int BestTotal(List<Card> hand)
        {
            int total = 0;
            int aces = 0;

            foreach (var c in hand)
            {
                int v = (int)c.Rank switch
                {
                    >= 2 and <= 10 => (int)c.Rank,
                    11 or 12 or 13 => 10,
                    14 => 11,
                    _ => 0
                };

                if ((int)c.Rank == 14) aces++;
                total += v;
            }

            while (total > 21 && aces > 0)
            {
                total -= 10;
                aces--;
            }

            return total;
        }

        private static bool IsSoft17(List<Card> hand)
        {
            // soft 17: total 17 with an ace counted as 11
            int total = 0;
            int aces = 0;

            foreach (var c in hand)
            {
                int v = (int)c.Rank switch
                {
                    >= 2 and <= 10 => (int)c.Rank,
                    11 or 12 or 13 => 10,
                    14 => 11,
                    _ => 0
                };
                if ((int)c.Rank == 14) aces++;
                total += v;
            }

            return total == 17 && aces > 0;
        }

        private void UpdateScoreText()
        {
            int p = BestTotal(_player);
            int d = BestTotal(_dealer);

            PlayerScoreText.Text = $"Score: {p}";

            if (_hideHole && _dealer.Count >= 2)
            {
                int up = BestTotal(new List<Card> { _dealer[0] });
                DealerScoreText.Text = $"Score: {up} + ?";
            }
            else
            {
                DealerScoreText.Text = $"Score: {d}";
            }
        }

        // -------------------------
        // Rendering
        // -------------------------
        private void RenderHands(bool animate = true)
        {
            DealerCardsPanel.Children.Clear();
            PlayerCardsPanel.Children.Clear();

            // Dealer
            for (int i = 0; i < _dealer.Count; i++)
            {
                bool faceUp = !(i == 1 && _hideHole);
                var cardBorder = MakeCardVisual(_dealer[i], faceUp);
                DealerCardsPanel.Children.Add(cardBorder);

                if (animate)
                {
                    AnimateDeal(cardBorder, i * 70);

                    // if faceUp, give it a little flip pop
                    if (faceUp)
                    {
                        AnimateFlip(cardBorder, () => { /* already face up */ }, i * 70);
                    }
                }
            }

            // Player (deal down then flip up)
            for (int i = 0; i < _player.Count; i++)
            {
                int idx = i;              // capture index
                var card = _player[idx];  // capture card

                var down = MakeCardVisual(card, faceUp: false);
                PlayerCardsPanel.Children.Add(down);

                if (animate)
                {
                    int t = 160 + idx * 70;

                    AnimateDeal(down, t);
                    AnimateFlip(down, () =>
                    {
                        // swap to face-up at midpoint
                        if (down.Child is TextBlock tb)
                        {
                            tb.Text = card.ShortText;
                            tb.Foreground = card.IsRed ? Brushes.IndianRed : Brushes.White;
                        }
                        down.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
                    }, t);
                }
                else
                {
                    // force face up
                    if (down.Child is TextBlock tb)
                    {
                        tb.Text = card.ShortText;
                        tb.Foreground = card.IsRed ? Brushes.IndianRed : Brushes.White;
                    }
                    down.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
                }
            }


            UpdateScoreText();
        }

        private void ResetUi()
        {
            _player.Clear();
            _dealer.Clear();
            _bet = 0;
            _inRound = false;
            _hideHole = true;

            DealerCardsPanel.Children.Clear();
            PlayerCardsPanel.Children.Clear();
            DealerScoreText.Text = "Score: --";
            PlayerScoreText.Text = "Score: --";
            SetOutcome("");

            SetBusy(false);
        }

        // -------------------------
        // Gameplay
        // -------------------------
        private void Deal_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || _inRound) return;

            if (!TryGetBet(out var bet) || bet <= 0)
            {
                SetOutcome("Invalid bet.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(bet, "Blackjack Stake"))
            {
                SetOutcome("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            _bet = bet;
            _inRound = true;
            _hideHole = true;

            _player.Clear();
            _dealer.Clear();

            _player.Add(_shoe.Draw());
            _dealer.Add(_shoe.Draw());
            _player.Add(_shoe.Draw());
            _dealer.Add(_shoe.Draw());

            RefreshWalletHud();
            SetOutcome("Dealing...", Brushes.White);

            SetBusy(false);
            RenderHands(animate: true);

            // Natural blackjack check
            int p = BestTotal(_player);
            int d = BestTotal(_dealer);

            if (p == 21 && _player.Count == 2)
            {
                _hideHole = false;
                RenderHands(animate: false);

                if (d == 21 && _dealer.Count == 2)
                {
                    ProfileManager.Instance.Earn(_bet, "Blackjack Push");
                    SetOutcome("Push (both blackjack). Bet returned.", Brushes.White);
                }
                else
                {
                    long payout = _bet + (long)Math.Round(_bet * 1.5);
                    ProfileManager.Instance.Earn(payout, "Blackjack");
                    SetOutcome($"+{(payout - _bet):N0} profit (Blackjack!)", Brushes.LightGreen);
                }

                RefreshWalletHud();
                _inRound = false;
                SetBusy(false);
            }
        }

        private void Hit_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRound) return;

            _player.Add(_shoe.Draw());
            RenderHands(animate: true);

            int p = BestTotal(_player);
            if (p > 21)
            {
                _hideHole = false;
                RenderHands(animate: false);
                SetOutcome($"BUST. -{_bet:N0} loss", Brushes.IndianRed);
                _inRound = false;
                SetBusy(false);
            }
        }

        private void Stand_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRound) return;

            _hideHole = false;

            // Dealer plays: hits soft 17
            while (true)
            {
                int d = BestTotal(_dealer);
                if (d < 17) { _dealer.Add(_shoe.Draw()); continue; }
                if (d == 17 && IsSoft17(_dealer)) { _dealer.Add(_shoe.Draw()); continue; }
                break;
            }

            RenderHands(animate: true);

            int p = BestTotal(_player);
            int d2 = BestTotal(_dealer);

            if (d2 > 21)
            {
                ProfileManager.Instance.Earn(_bet * 2, "Blackjack Win");
                SetOutcome($"+{_bet:N0} profit (Dealer bust)", Brushes.LightGreen);
            }
            else if (p > d2)
            {
                ProfileManager.Instance.Earn(_bet * 2, "Blackjack Win");
                SetOutcome($"+{_bet:N0} profit", Brushes.LightGreen);
            }
            else if (p < d2)
            {
                SetOutcome($"-{_bet:N0} loss", Brushes.IndianRed);
            }
            else
            {
                ProfileManager.Instance.Earn(_bet, "Blackjack Push");
                SetOutcome("Push. Bet returned.", Brushes.White);
            }

            RefreshWalletHud();
            _inRound = false;
            SetBusy(false);
        }

        private void Double_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRound) return;

            // Simple rule: only allow double on first decision (2 cards)
            if (_player.Count != 2)
            {
                SetOutcome("Double only allowed on first decision.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(_bet, "Blackjack Double"))
            {
                SetOutcome("Not enough coins to double.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            _bet *= 2;
            RefreshWalletHud();

            _player.Add(_shoe.Draw());
            RenderHands(animate: true);

            int p = BestTotal(_player);
            if (p > 21)
            {
                _hideHole = false;
                RenderHands(animate: false);
                SetOutcome($"BUST. -{_bet:N0} loss", Brushes.IndianRed);
                _inRound = false;
                SetBusy(false);
                return;
            }

            // forced stand after double
            Stand_Click(sender, e);
        }
    }
}


