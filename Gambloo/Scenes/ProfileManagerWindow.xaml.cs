using BluesBar.Systems;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluesBar
{
    public partial class ProfileManagerWindow : Window
    {
        public event Action? ProfileSaved;

        private readonly Profile _profile; // replace type name if yours differs
        private string _pickedColorHex;

        public ProfileManagerWindow()
        {
            InitializeComponent();

            _profile = ProfileManager.Instance.Current;
            _pickedColorHex = string.IsNullOrWhiteSpace(_profile.PlayerNameColorHex)
                ? "#FF483DFF"
                : _profile.PlayerNameColorHex;

            LoadUiFromProfile();
        }

        private void LoadUiFromProfile()
        {
            UsernameBox.Text = _profile.PlayerName;

            CoinsFullText.Text = $"{_profile.Coins:N0} Coins";
            CoinsFullText.ToolTip = CoinsFullText.Text;

            NetWorthFullText.Text = $"${_profile.NetWorth:N0}";
            NetWorthFullText.ToolTip = NetWorthFullText.Text;

            // Derived leveling display
            var shared = ProfileManager.Instance.Shared;

            // Accumulate pending (so opening the profile also “detects” new levels)
            bool changed = Systems.LevelProgression.DetectAndAccumulatePending(shared);
            if (changed)
                ProfileManager.Instance.Save();

            var state = Systems.LevelCalculator.Compute(shared.LifetimeAimCoinsEarned);

            if (LevelFullText != null)
                LevelFullText.Text = $"Lv {state.LevelInPrestige}";

            if (PendingLevelsText != null)
            {
                PendingLevelsText.Text = shared.PendingLevelUps > 0
                    ? $"Pending level-ups: +{shared.PendingLevelUps}"
                    : "";
            }

            ApplyPreview(_profile.PlayerName, _pickedColorHex);
        }


        private void ApplyPreview(string name, string hex)
        {
            PreviewNameText.Text = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            PreviewNameText.Foreground = BrushFromHex(hex, Brushes.DarkSlateBlue);
        }
        private static Brush BrushFromHex(string hex, Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;

            try
            {
                // Supports #RGB, #ARGB, #RRGGBB, #AARRGGBB
                var obj = ColorConverter.ConvertFromString(hex);
                if (obj is Color c)
                {
                    var b = new SolidColorBrush(c);
                    b.Freeze();
                    return b;
                }
            }
            catch { /* ignored */ }

            return fallback;
        }


        private void ColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                _pickedColorHex = hex;
                ApplyPreview(UsernameBox.Text, _pickedColorHex);
            }
        }
        private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyPreview(UsernameBox.Text, _pickedColorHex);
        }


        private void Save_Click(object sender, RoutedEventArgs e)
        {
            UsernameErrorText.Visibility = Visibility.Collapsed;

            var newName = (UsernameBox.Text ?? "").Trim();

            // Basic validation (tune as desired)
            if (newName.Length < 2)
            {
                UsernameErrorText.Text = "Username must be at least 2 characters.";
                UsernameErrorText.Visibility = Visibility.Visible;
                return;
            }
            if (newName.Length > 16)
            {
                UsernameErrorText.Text = "Username must be 16 characters or fewer.";
                UsernameErrorText.Visibility = Visibility.Visible;
                return;
            }

            // Commit to profile + save
            _profile.PlayerName = newName;
            _profile.PlayerNameColorHex = _pickedColorHex;

            ProfileManager.Instance.Save();

            ProfileSaved?.Invoke();
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ApplyPreview(UsernameBox.Text, _pickedColorHex);
        }
    }
}

