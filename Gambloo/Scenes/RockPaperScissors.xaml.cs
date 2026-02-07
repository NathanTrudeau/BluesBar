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
    public partial class RockPaperScissorsScene : UserControl, IGamblooScene
    {
        public string SceneId => "rockpaperscissors";
        public string DisplayName => "RockPaperScissors";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new Random();

        private enum Pick { Rock, Paper, Scissors }

        public RockPaperScissorsScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                RefreshWalletHud();
                ResetBoard();
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

            if (BetTextBox != null) BetTextBox.IsEnabled = !busy;
        }

        private void SetOutcome(string msg, Brush? color = null)
        {
            if (OutcomeText == null) return;
            OutcomeText.Text = msg;
            OutcomeText.Foreground = color ?? Brushes.White;
        }

        private void ResetBoard()
        {
            if (YouPickText != null) YouPickText.Text = "—";
            if (HousePickText != null) HousePickText.Text = "—";
            SetOutcome("");
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

        private void Rock_Click(object sender, RoutedEventArgs e) => Play(Pick.Rock);
        private void Paper_Click(object sender, RoutedEventArgs e) => Play(Pick.Paper);
        private void Scissors_Click(object sender, RoutedEventArgs e) => Play(Pick.Scissors);

        private async void Play(Pick you)
        {
            if (IsBusy) return;

            if (!TryGetBet(out var bet) || bet <= 0)
            {
                SetOutcome("Invalid bet.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(bet, "RPS Stake"))
            {
                SetOutcome("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            RefreshWalletHud();
            SetBusy(true);

            // reveal animation: show house cycling quickly
            var house = (Pick)_rng.Next(0, 3);

            if (YouPickText != null) YouPickText.Text = ToGlyph(you);
            if (HousePickText != null) HousePickText.Text = "…";

            await HouseDrumrollAsync();

            if (HousePickText != null) HousePickText.Text = ToGlyph(house);

            // resolve
            int outcome = Compare(you, house); // 1 win, 0 tie, -1 loss

            if (outcome == 1)
            {
                long payout = bet * 2; // win pays x2 (profit = +bet)
                ProfileManager.Instance.Earn(payout, "RPS Payout");
                RefreshWalletHud();
                SetOutcome($"+{(payout - bet):N0} profit (Paid {payout:N0})", Brushes.LightGreen);
            }
            else if (outcome == 0)
            {
                // push returns bet
                ProfileManager.Instance.Earn(bet, "RPS Push");
                RefreshWalletHud();
                SetOutcome($"Push. Bet returned ({bet:N0}).", Brushes.White);
            }
            else
            {
                SetOutcome($"{(-bet):N0} loss", Brushes.IndianRed);
            }

            SetBusy(false);
        }

        private static int Compare(Pick you, Pick house)
        {
            if (you == house) return 0;

            // rock beats scissors, paper beats rock, scissors beats paper
            return (you, house) switch
            {
                (Pick.Rock, Pick.Scissors) => 1,
                (Pick.Paper, Pick.Rock) => 1,
                (Pick.Scissors, Pick.Paper) => 1,
                _ => -1
            };
        }

        private static string ToGlyph(Pick p)
        {
            // simple “emoji-ish” glyphs that read well in UI
            return p switch
            {
                Pick.Rock => "✊",
                Pick.Paper => "✋",
                Pick.Scissors => "✌",
                _ => "?"
            };
        }

        private Task HouseDrumrollAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            if (HousePickText == null) { tcs.TrySetResult(true); return tcs.Task; }

            var sb = new Storyboard();

            // pulse opacity 3 times
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))) { EasingFunction = new QuadraticEase() });
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240))) { EasingFunction = new QuadraticEase() });
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))) { EasingFunction = new QuadraticEase() });
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))) { EasingFunction = new QuadraticEase() });

            Storyboard.SetTarget(a, HousePickText);
            Storyboard.SetTargetProperty(a, new PropertyPath(TextBlock.OpacityProperty));
            sb.Children.Add(a);

            sb.Completed += (_, __) => tcs.TrySetResult(true);
            sb.Begin();

            return tcs.Task;
        }
    }
}
