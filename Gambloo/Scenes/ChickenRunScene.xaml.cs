using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class ChickenRunScene : UserControl, IGamblooScene
    {
        public string SceneId => "chickenrun";
        public string DisplayName => "Chicken Run";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new();
        private readonly List<(Border border, TextBlock label)> _tiles = new();

        private int _totalSteps = 10;
        private int _currentStep = 0;
        private bool _roundActive = false;
        private bool _hasAdvanced = false;
        private long _wager = 0;
        private double _currentMultiplier = 1.0;
        private int _lastComboIndex = 0;

        public ChickenRunScene()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                RefreshWallet();
                BuildStepTiles();
                UpdateHud();
            };
        }

        public void OnShown()
        {
            RefreshWallet();
        }

        public void OnHidden() { }

        private void RefreshWallet()
        {
            if (WalletText != null)
            {
                var coins = ProfileManager.Instance.Current.Coins;
                WalletText.Text = $"Wallet: {coins:N0} Coins";
            }
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }

        private void BuildStepTiles()
        {
            if (StepGrid == null) return;

            StepGrid.Children.Clear();
            _tiles.Clear();

            int columns = _totalSteps switch
            {
                <= 10 => 5,
                <= 12 => 4,
                _ => 5
            };
            StepGrid.Columns = columns;

            for (int i = 1; i <= _totalSteps; i++)
            {
                var label = new TextBlock
                {
                    Text = i.ToString(CultureInfo.InvariantCulture),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold
                };

                var border = new Border
                {
                    Background = (Brush)FindResource("Panel")!,
                    BorderBrush = (Brush)FindResource("Stroke")!,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(6),
                    Height = 70,
                    Child = label
                };

                StepGrid.Children.Add(border);
                _tiles.Add((border, label));
            }
        }

        private void StartRun_Click(object sender, RoutedEventArgs e)
        {
            if (_roundActive)
            {
                SetStatus("Finish or cash out your current run first.", Brushes.OrangeRed);
                return;
            }

            if (!TryGetBet(out var bet) || bet <= 0)
            {
                SetStatus("Enter a valid wager.", Brushes.OrangeRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(bet, "Chicken Run stake"))
            {
                SetStatus("Not enough coins for that wager.", Brushes.OrangeRed);
                RefreshWallet();
                return;
            }

            _wager = bet;
            _currentStep = 0;
            _currentMultiplier = 1.0;
            _roundActive = true;
            _hasAdvanced = false;

            BuildStepTiles();
            UpdateHud();
            RefreshWallet();
            SetStatus($"Run started with {_totalSteps} steps. Advance carefully!", Brushes.LightGreen);
            UpdateControlState();
        }

        private void Advance_Click(object sender, RoutedEventArgs e)
        {
            if (!_roundActive)
            {
                SetStatus("Start a run first.", Brushes.OrangeRed);
                return;
            }

            var nextStep = _currentStep + 1;
            var hazard = ComputeHitProbability(nextStep);
            var hit = _rng.NextDouble() < hazard;

            if (hit)
            {
                MarkTile(nextStep, TileResult.Hit);
                _roundActive = false;
                _hasAdvanced = false;
                UpdateHud();
                UpdateControlState();
                SetStatus($"SPLAT! Hit on step {nextStep}. Lost {_wager:N0} coins.", Brushes.OrangeRed);
                return;
            }

            _currentStep = nextStep;
            _hasAdvanced = true;
            _currentMultiplier += nextStep <= 7 ? 0.1 : 0.2;
            MarkTile(nextStep, TileResult.Safe);
            UpdateHud();

            if (_currentStep >= _totalSteps)
            {
                SetStatus("Jackpot lane cleared! Auto cashing out…", Brushes.LightGreen);
                CashOutInternal(autoJackpot: true);
            }
            else
            {
                SetStatus($"Step {nextStep} cleared. Multiplier x{_currentMultiplier:F1}.", Brushes.White);
            }

            UpdateControlState();
        }

        private void CashOut_Click(object sender, RoutedEventArgs e)
        {
            CashOutInternal();
        }

        private void CashOutInternal(bool autoJackpot = false)
        {
            if (!_roundActive)
            {
                SetStatus("No active run.", Brushes.OrangeRed);
                return;
            }

            if (!_hasAdvanced)
            {
                SetStatus("Take at least one step before cashing out.", Brushes.OrangeRed);
                return;
            }

            var payout = (long)Math.Round(_wager * _currentMultiplier, MidpointRounding.AwayFromZero);
            ProfileManager.Instance.Earn(payout, "Chicken Run cashout");
            RefreshWallet();

            _roundActive = false;
            _hasAdvanced = false;
            UpdateHud();
            UpdateControlState();

            if (autoJackpot)
            {
                SetStatus($"Jackpot secured! Paid {payout:N0} coins (x{_currentMultiplier:F1}).", Brushes.LightGreen);
            }
            else
            {
                SetStatus($"Cashed out {payout:N0} coins (x{_currentMultiplier:F1}).", Brushes.LightGreen);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _roundActive = false;
            _hasAdvanced = false;
            _currentStep = 0;
            _currentMultiplier = 1.0;
            BuildStepTiles();
            UpdateHud();
            UpdateControlState();
            SetStatus("Run reset.", Brushes.White);
        }

        private void BetPlusSmall_Click(object sender, RoutedEventArgs e) => AdjustBet(1_000);
        private void BetPlusLarge_Click(object sender, RoutedEventArgs e) => AdjustBet(10_000);

        private void AdjustBet(long delta)
        {
            if (BetTextBox == null) return;
            if (!TryGetBet(out var current)) current = 0;
            var next = Math.Max(0, current + delta);
            BetTextBox.Text = next.ToString(CultureInfo.InvariantCulture);
        }

        private bool TryGetBet(out long bet)
        {
            bet = 0;
            return BetTextBox != null && long.TryParse(BetTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bet);
        }

        private void StepCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StepCombo == null) return;
            if (StepCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var steps))
            {
                if (StepCombo.SelectedItem is ComboBoxItem item2 && item2.Tag is int tagInt)
                    steps = tagInt;
                else
                    steps = 10;
            }

            if (_roundActive)
            {
                // revert change while run active
                StepCombo.SelectedIndex = _lastComboIndex;
                SetStatus("Finish the run before changing steps.", Brushes.OrangeRed);
                return;
            }

            _totalSteps = steps;
            _lastComboIndex = StepCombo.SelectedIndex;
            BuildStepTiles();
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (StepProgressText != null)
                StepProgressText.Text = $"Step {_currentStep} / {_totalSteps}";

            if (MultiplierText != null)
                MultiplierText.Text = $"Multiplier: x{Math.Max(1.0, _currentMultiplier):F1}";

            if (HazardText != null)
            {
                var risk = !_roundActive ? 0 : ComputeHitProbability(_currentStep + 1);
                HazardText.Text = _roundActive
                    ? $"Next risk: {(risk * 100):F0}%"
                    : "Next risk: 0%";
            }
        }

        private void UpdateControlState()
        {
            bool canAdvance = _roundActive;
            bool canCashOut = _roundActive && _hasAdvanced;

            if (AdvanceButton != null) AdvanceButton.IsEnabled = canAdvance;
            if (CashOutButton != null) CashOutButton.IsEnabled = canCashOut;
            if (ResetButton != null) ResetButton.IsEnabled = true;
        }

        private void MarkTile(int stepIndex, TileResult result)
        {
            if (stepIndex <= 0 || stepIndex > _tiles.Count) return;
            var tile = _tiles[stepIndex - 1];

            switch (result)
            {
                case TileResult.Safe:
                    tile.border.Background = (Brush)FindResource("Success")!;
                    tile.border.BorderBrush = Brushes.White;
                    tile.label.Text = $"{stepIndex}\nSAFE";
                    break;
                case TileResult.Hit:
                    tile.border.Background = (Brush)FindResource("Danger")!;
                    tile.border.BorderBrush = Brushes.Black;
                    tile.label.Text = $"{stepIndex}\nHIT";
                    break;
                default:
                    tile.border.Background = (Brush)FindResource("Panel")!;
                    tile.border.BorderBrush = (Brush)FindResource("Stroke")!;
                    tile.label.Text = stepIndex.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        private double ComputeHitProbability(int stepNumber)
        {
            if (stepNumber <= 0) return 0.0;

            double prob;
            if (stepNumber <= 7)
                prob = 0.1 * stepNumber;
            else
                prob = 0.1 * 7 + 0.2 * (stepNumber - 7);

            if (prob > 0.98) prob = 0.98;
            return prob;
        }

        private void SetStatus(string text, Brush? color = null)
        {
            if (StatusText != null)
            {
                StatusText.Text = text;
                StatusText.Foreground = color ?? Brushes.White;
            }
        }

        private enum TileResult
        {
            Neutral,
            Safe,
            Hit
        }
    }
}
