using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;

using BluesBar.Gambloo.Cards;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class RideTheBusScene : UserControl, IGamblooScene
    {
        public string SceneId => "ridethebus";
        public string DisplayName => "Ride The Bus";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new Random();
        private readonly Shoe _shoe = new Shoe(decks: 6, theme: new CardTheme());

        private bool _inRun = false;

        // stage = how many steps already CLEARED (0..4)
        // 0 = waiting for Step1 pick, 1 = waiting for Step2, 2 = waiting for Step3, 3 = waiting for Step4, 4 = finished
        private int _stageCleared = 0;

        private long _stake = 0;

        private Card _c1, _c2, _c3, _c4;

        // multipliers after clearing N steps (cashout after 1+)
        private static readonly double[] StepMultipliers = { 2, 3, 5, 100};

        public RideTheBusScene()
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
        // HUD + State
        // -------------------------
        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            WalletText.Text = $"Savings: {p.Coins:N0} Coins";
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            StartButton.IsEnabled = !busy && !_inRun;
            ChipTextBox.IsEnabled = !busy && !_inRun;

            bool canPick = !busy && _inRun;
            SetPanelEnabled(ColorPanel, canPick && _stageCleared == 0);
            SetPanelEnabled(HighLowPanel, canPick && _stageCleared == 1);
            SetPanelEnabled(InOutPanel, canPick && _stageCleared == 2);
            SetPanelEnabled(SuitPanel, canPick && _stageCleared == 3);

            CashOutButton.IsEnabled = !busy && _inRun && _stageCleared >= 1 && _stageCleared <= 3;
        }

        private static void SetPanelEnabled(UIElement el, bool enabled)
        {
            el.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            el.IsEnabled = enabled;
        }

        private void SetOutcome(string msg, Brush? color = null)
        {
            OutcomeText.Text = msg;
            OutcomeText.Foreground = color ?? Brushes.White;
        }

        private bool TryGetStake(out long stake)
        {
            stake = 0;
            return long.TryParse(ChipTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out stake);
        }

        private void UpdateHud()
        {
            StakeText.Text = $"Stake: {_stake:N0}";

            if (!_inRun)
            {
                StatusText.Text = "Start a round.";
                MultText.Text = "x1.00";
                PayoutText.Text = "Cashout: -";
                return;
            }

            StatusText.Text = _stageCleared switch
            {
                0 => "Step 1/4: RED or BLACK?",
                1 => "Step 2/4: HIGHER or LOWER?",
                2 => "Step 3/4: INSIDE or OUTSIDE?",
                3 => "Step 4/4: SUIT?",
                _ => "—"
            };

            if (_stageCleared <= 0)
            {
                MultText.Text = "x1.00";
                PayoutText.Text = "Cashout: -";
                return;
            }

            double mult = StepMultipliers[Math.Clamp(_stageCleared - 1, 0, 3)];
            MultText.Text = $"x{mult:0.00}";
            PayoutText.Text = $"Cashout: {(long)Math.Round(_stake * mult):N0}";
        }

        // -------------------------
        // Card visuals (XAML cards)
        // -------------------------
        private Border CardBorderFor(TextBlock tb) => (Border)tb.Parent;

        private static Brush FaceBg => new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
        private static Brush BackBg => new SolidColorBrush(Color.FromRgb(0x0E, 0x1A, 0x2F));
        private static Brush Accent => new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
        private static Brush Stroke => new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A));

        private void SetCardEmpty(TextBlock tb)
        {
            tb.Text = "";
            tb.Foreground = Brushes.White;

            if (tb.Parent is Border b)
                b.Background = FaceBg;
        }

        private void SetCardBack(TextBlock tb)
        {
            tb.Text = "--";
            tb.Foreground = Accent;

            if (tb.Parent is Border b)
                b.Background = BackBg;
        }

        private void SetCardFace(TextBlock tb, Card card)
        {
            tb.Text = card.ShortText;
            tb.Foreground = card.IsRed ? Brushes.IndianRed : Brushes.White;

            if (tb.Parent is Border b)
                b.Background = FaceBg;
        }

        private void EnsureTransforms(Border b)
        {
            if (b.RenderTransform is TransformGroup) return;

            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            group.Children.Add(new TranslateTransform(0, 0));
            b.RenderTransform = group;
            b.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void AnimateDeal(Border b, int delayMs)
        {
            EnsureTransforms(b);
            var tg = (TransformGroup)b.RenderTransform;
            var tt = tg.Children.OfType<TranslateTransform>().First();

            b.Opacity = 0;
            tt.X = -14;
            tt.Y = -10;

            var sb = new Storyboard();

            var aOp = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(aOp, b);
            Storyboard.SetTargetProperty(aOp, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(aOp);

            var aX = new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(aX, tt);
            Storyboard.SetTargetProperty(aX, new PropertyPath(TranslateTransform.XProperty));
            sb.Children.Add(aX);

            var aY = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(aY, tt);
            Storyboard.SetTargetProperty(aY, new PropertyPath(TranslateTransform.YProperty));
            sb.Children.Add(aY);

            sb.Begin();
        }

        private void AnimateFlip(Border b, Action midpointSwap, int delayMs = 0)
        {
            EnsureTransforms(b);
            var tg = (TransformGroup)b.RenderTransform;
            var st = tg.Children.OfType<ScaleTransform>().First();

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

        private void DealFaceDownFour()
        {
            // Set backs
            SetCardBack(Card1Text);
            SetCardBack(Card2Text);
            SetCardBack(Card3Text);
            SetCardBack(Card4Text);

            // Animate in
            AnimateDeal(CardBorderFor(Card1Text), 0);
            AnimateDeal(CardBorderFor(Card2Text), 70);
            AnimateDeal(CardBorderFor(Card3Text), 140);
            AnimateDeal(CardBorderFor(Card4Text), 210);
        }

        private void RevealCard(int index)
        {
            if (index == 1) AnimateFlip(CardBorderFor(Card1Text), () => SetCardFace(Card1Text, _c1));
            else if (index == 2) AnimateFlip(CardBorderFor(Card2Text), () => SetCardFace(Card2Text, _c2));
            else if (index == 3) AnimateFlip(CardBorderFor(Card3Text), () => SetCardFace(Card3Text, _c3));
            else if (index == 4) AnimateFlip(CardBorderFor(Card4Text), () => SetCardFace(Card4Text, _c4));
        }

        // -------------------------
        // Game flow
        // -------------------------
        private void ResetUi()
        {
            _inRun = false;
            _stageCleared = 0;
            _stake = 0;

            SetCardEmpty(Card1Text);
            SetCardEmpty(Card2Text);
            SetCardEmpty(Card3Text);
            SetCardEmpty(Card4Text);

            SetOutcome("");
            UpdateHud();

            // Hide guess panels
            ColorPanel.Visibility = Visibility.Collapsed;
            HighLowPanel.Visibility = Visibility.Collapsed;
            InOutPanel.Visibility = Visibility.Collapsed;
            SuitPanel.Visibility = Visibility.Collapsed;

            SetBusy(false);
        }

        private void BeginRound()
        {
            _stageCleared = 0;

            _c1 = _shoe.Draw();
            _c2 = _shoe.Draw();
            _c3 = _shoe.Draw();
            _c4 = _shoe.Draw();

            DealFaceDownFour();

            // Show step 1
            SetPanelEnabled(ColorPanel, true);
            HighLowPanel.Visibility = Visibility.Collapsed;
            InOutPanel.Visibility = Visibility.Collapsed;
            SuitPanel.Visibility = Visibility.Collapsed;

            UpdateHud();
            SetBusy(false);
        }

        private void Bust(string msg)
        {
            _inRun = false;
            SetOutcome(msg, Brushes.IndianRed);

            // reveal everything for drama
            RevealCard(1);
            RevealCard(2);
            RevealCard(3);
            RevealCard(4);

            // hide panels
            ColorPanel.Visibility = Visibility.Collapsed;
            HighLowPanel.Visibility = Visibility.Collapsed;
            InOutPanel.Visibility = Visibility.Collapsed;
            SuitPanel.Visibility = Visibility.Collapsed;

            UpdateHud();
            SetBusy(false);
        }

        private void StepWinAdvance()
        {
            // reveal the card for the step you just cleared
            if (_stageCleared == 0) RevealCard(1);
            else if (_stageCleared == 1) RevealCard(2);
            else if (_stageCleared == 2) RevealCard(3);
            else if (_stageCleared == 3) RevealCard(4);

            _stageCleared++;

            // Completed all 4 steps
            if (_stageCleared >= 4)
            {
                long payout = (long)Math.Round(_stake * StepMultipliers[3]);
                ProfileManager.Instance.Earn(payout, "RideTheBus Payout");
                RefreshWalletHud();

                _inRun = false;

                SetOutcome($"+{(payout - _stake):N0} profit  (Paid {payout:N0})", Brushes.LightGreen);

                // hide panels
                ColorPanel.Visibility = Visibility.Collapsed;
                HighLowPanel.Visibility = Visibility.Collapsed;
                InOutPanel.Visibility = Visibility.Collapsed;
                SuitPanel.Visibility = Visibility.Collapsed;

                StatusText.Text = "Perfect run. Start again?";
                MultText.Text = "x—";
                PayoutText.Text = $"Cashout: {payout:N0}";

                SetBusy(false);
                return;
            }

            // show next stage panel
            ColorPanel.Visibility = Visibility.Collapsed;
            HighLowPanel.Visibility = Visibility.Collapsed;
            InOutPanel.Visibility = Visibility.Collapsed;
            SuitPanel.Visibility = Visibility.Collapsed;

            if (_stageCleared == 1) SetPanelEnabled(HighLowPanel, true);
            else if (_stageCleared == 2) SetPanelEnabled(InOutPanel, true);
            else if (_stageCleared == 3) SetPanelEnabled(SuitPanel, true);

            UpdateHud();
            SetOutcome("Hit. Keep going.", Brushes.White);
            SetBusy(false);
        }

        // -------------------------
        // Button handlers (match your XAML)
        // -------------------------
        private void StartRound_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            if (!TryGetStake(out var stake) || stake <= 0)
            {
                SetOutcome("Invalid stake.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(stake, "RideTheBus Stake"))
            {
                SetOutcome("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            _stake = stake;
            _inRun = true;

            RefreshWalletHud();
            SetOutcome("Good luck.", Brushes.White);

            BeginRound();
        }

        private void CashOut_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared <= 0) return;

            double mult = StepMultipliers[Math.Clamp(_stageCleared - 1, 0, 3)];
            long payout = (long)Math.Round(_stake * mult);

            ProfileManager.Instance.Earn(payout, "RideTheBus Cashout");
            RefreshWalletHud();

            _inRun = false;

            SetOutcome($"+{(payout - _stake):N0} profit  (Cashout {payout:N0})", Brushes.LightGreen);

            // reveal everything for drama (cashout)
            RevealCard(1);
            RevealCard(2);
            RevealCard(3);
            RevealCard(4);

            // hide panels
            ColorPanel.Visibility = Visibility.Collapsed;
            HighLowPanel.Visibility = Visibility.Collapsed;
            InOutPanel.Visibility = Visibility.Collapsed;
            SuitPanel.Visibility = Visibility.Collapsed;

            StatusText.Text = "Cashed out. Start again?";
            MultText.Text = "x—";
            PayoutText.Text = $"Cashout: {payout:N0}";
            SetBusy(false);
        }

        // Step 1: color on card1
        private void GuessRed_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 0) return;

            bool win = _c1.IsRed;
            if (!win) { RevealCard(1); Bust("BUST. Wrong color."); return; }

            StepWinAdvance();
        }

        private void GuessBlack_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 0) return;

            bool win = !_c1.IsRed;
            if (!win) { RevealCard(1); Bust("BUST. Wrong color."); return; }

            StepWinAdvance();
        }

        // Step 2: higher/lower card2 vs card1, equal loses
        private void GuessHigh_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 1) return;

            bool win = (int)_c2.Rank > (int)_c1.Rank;
            if (!win) { RevealCard(2); Bust("BUST. Not higher."); return; }

            StepWinAdvance();
        }

        private void GuessLow_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 1) return;

            bool win = (int)_c2.Rank < (int)_c1.Rank;
            if (!win) { RevealCard(2); Bust("BUST. Not lower."); return; }

            StepWinAdvance();
        }

        // Step 3: inside/outside card3 between card1&2 (strictly inside; boundary loses)
        private void GuessInside_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 2) return;

            int lo = Math.Min((int)_c1.Rank, (int)_c2.Rank);
            int hi = Math.Max((int)_c1.Rank, (int)_c2.Rank);

            bool inside = (int)_c3.Rank > lo && (int)_c3.Rank < hi; // strict
            if (!inside) { RevealCard(3); Bust("BUST. Not inside."); return; }

            StepWinAdvance();
        }

        private void GuessOutside_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy || !_inRun || _stageCleared != 2) return;

            int lo = Math.Min((int)_c1.Rank, (int)_c2.Rank);
            int hi = Math.Max((int)_c1.Rank, (int)_c2.Rank);

            bool outside = (int)_c3.Rank < lo || (int)_c3.Rank > hi; // strict boundary loses
            if (!outside) { RevealCard(3); Bust("BUST. Not outside."); return; }

            StepWinAdvance();
        }

        // Step 4: suit card4
        private void GuessSuitClubs_Click(object sender, RoutedEventArgs e) => GuessSuit(Suit.Clubs);
        private void GuessSuitDiamonds_Click(object sender, RoutedEventArgs e) => GuessSuit(Suit.Diamonds);
        private void GuessSuitHearts_Click(object sender, RoutedEventArgs e) => GuessSuit(Suit.Hearts);
        private void GuessSuitSpades_Click(object sender, RoutedEventArgs e) => GuessSuit(Suit.Spades);

        private void GuessSuit(Suit suit)
        {
            if (IsBusy || !_inRun || _stageCleared != 3) return;

            bool win = _c4.Suit == suit;
            if (!win) { RevealCard(4); Bust("BUST. Wrong suit."); return; }

            StepWinAdvance();
        }
    }
}

