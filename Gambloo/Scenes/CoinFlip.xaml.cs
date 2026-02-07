using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class CoinFlipScene : UserControl, IGamblooScene
    {
        public string SceneId => "coinflip";
        public string DisplayName => "CoinFlip";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new Random();
        private bool? _pickedHeads = true; // default
        private long _lastBet = 0;

        public CoinFlipScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                RefreshWalletHud();
                SetChoiceUi();
                SetOutcome("");
            };
        }

        public void OnShown() => RefreshWalletHud();
        public void OnHidden() { }

        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            if (WalletText != null) WalletText.Text = $"Savings: {p.Coins:N0} Coins";
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            if (HeadsButton != null) HeadsButton.IsEnabled = !busy;
            if (TailsButton != null) TailsButton.IsEnabled = !busy;
            if (FlipButton != null) FlipButton.IsEnabled = !busy;
            if (BetTextBox != null) BetTextBox.IsEnabled = !busy;
            if (RebetFlipButton != null) RebetFlipButton.IsEnabled = !busy && _lastBet > 0;
        }

        private void SetOutcome(string msg, Brush? color = null)
        {
            if (OutcomeText == null) return;
            OutcomeText.Text = msg;
            OutcomeText.Foreground = color ?? Brushes.White;
        }

        private bool TryGetBet(out long bet)
        {
            bet = 0;
            return BetTextBox != null &&
                   long.TryParse(BetTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bet);
        }

        private void BetPlus1k_Click(object sender, RoutedEventArgs e) => AddToBet(1_000);
        private void BetPlus10k_Click(object sender, RoutedEventArgs e) => AddToBet(10_000);

        private void AddToBet(long add)
        {
            if (!TryGetBet(out var cur)) cur = 0;
            if (BetTextBox != null) BetTextBox.Text = (cur + add).ToString(CultureInfo.InvariantCulture);
        }

        private void Heads_Click(object sender, RoutedEventArgs e) { _pickedHeads = true; SetChoiceUi(); }
        private void Tails_Click(object sender, RoutedEventArgs e) { _pickedHeads = false; SetChoiceUi(); }

        private void SetChoiceUi()
        {
            if (HeadsButton != null)
            {
                HeadsButton.BorderBrush = _pickedHeads == true
                    ? new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A));
            }

            if (TailsButton != null)
            {
                TailsButton.BorderBrush = _pickedHeads == false
                    ? new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A));
            }
        }

        private async void Flip_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            if (!TryGetBet(out var bet) || bet <= 0)
            {
                SetOutcome("Invalid bet.", Brushes.IndianRed);
                return;
            }

            await RunFlipAsync(bet, spendNow: true);
        }

        private async void RebetFlip_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            if (_lastBet <= 0) return;

            await RunFlipAsync(_lastBet, spendNow: true);
        }

        private async Task RunFlipAsync(long bet, bool spendNow)
        {
            if (spendNow)
            {
                if (!ProfileManager.Instance.Spend(bet, "CoinFlip Stake"))
                {
                    SetOutcome("Not enough coins.", Brushes.IndianRed);
                    RefreshWalletHud();
                    return;
                }
            }

            _lastBet = bet;
            SetBusy(true);
            RefreshWalletHud();

            // animate coin
            bool resultHeads = _rng.Next(0, 2) == 0;

            await AnimateCoinFlipAsync(resultHeads);

            // resolve double or nothing
            bool win = (_pickedHeads == resultHeads);
            if (win)
            {
                long payout = bet * 2;
                ProfileManager.Instance.Earn(payout, "CoinFlip Payout");
                RefreshWalletHud();
                SetOutcome($"+{(payout - bet):N0} profit (Paid {payout:N0})", Brushes.LightGreen);
            }
            else
            {
                SetOutcome($"{(-bet):N0} loss", Brushes.IndianRed);
            }

            SetBusy(false);
        }

        private Task AnimateCoinFlipAsync(bool resultHeads)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (CoinText != null) CoinText.Text = "BB";
            if (CoinSub != null) CoinSub.Text = "FLIPPING";

            // flip illusion: scaleX oscillates + rotate
            var sb = new Storyboard();

            var rot = new DoubleAnimation(0, 720 + _rng.Next(0, 360), TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(rot, CoinRot);
            Storyboard.SetTargetProperty(rot, new PropertyPath(RotateTransform.AngleProperty));
            sb.Children.Add(rot);

            // scaleX: 1 -> 0.1 -> 1 (twice)
            var sx = new DoubleAnimationUsingKeyFrames();
            sx.Duration = TimeSpan.FromMilliseconds(900);
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))) { EasingFunction = new QuadraticEase() });
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))) { EasingFunction = new QuadraticEase() });
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(540))) { EasingFunction = new QuadraticEase() });
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))) { EasingFunction = new QuadraticEase() });

            Storyboard.SetTarget(sx, CoinScale);
            Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
            sb.Children.Add(sx);

            sb.Completed += (_, __) =>
            {
                // final face
                if (CoinText != null) CoinText.Text = resultHeads ? "H" : "T";
                if (CoinSub != null) CoinSub.Text = resultHeads ? "HEADS" : "TAILS";

                // tint subtly
                if (Coin != null)
                {
                    Coin.Background = resultHeads
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB))
                        : new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
                }

                tcs.TrySetResult(true);
            };

            sb.Begin();
            return tcs.Task;
        }
    }
}
