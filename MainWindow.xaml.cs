using BluesBar.Rooms;
using BluesBar.Systems;
using BluesShared;
using System;
using System.Windows;
using System.Windows.Input;

namespace BluesBar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ProfileSync _profileSync = null!;
        private AimRoomWindow? _aim;
        private GamblooWindow? _gambloo;
        private BackpackWindow? _backpack;
        private StoreWindow? _store;

        public MainWindow()
        {
            InitializeComponent();
            Systems.ProfileManager.Instance.LoadOrCreate();
            InitProfileCoinSync();
            RefreshProfileHud();


            this.Closing += MainWindow_Closing;
        }

        private void OpenAim_Click(object sender, RoutedEventArgs e)
        {
            //------------------------------------------------------------
            //       BluesAimTrain        BETA V3.0         2/5/26
            //------------------------------------------------------------
            try
            {
                AimTrainerLauncher.Launch();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to launch Aim Trainer:\n\n" + ex.Message,
                    "Launch Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            } 
        }

        private void OpenGambloo_Click(object sender, RoutedEventArgs e)
        {
            _gambloo = OpenOrActivate(_gambloo, () => new GamblooWindow());
        }

        private void OpenBackpack_Click(object sender, RoutedEventArgs e)
        {
            _backpack = OpenOrActivate(_backpack, () => new BackpackWindow());
        }

        private void OpenStore_Click(object sender, RoutedEventArgs e)
        {
            _store = OpenOrActivate(_store, () => new StoreWindow());
        }

        private T OpenOrActivate<T>(T? existing, Func<T> factory) where T : Window
        {
            if (existing == null)
            {
                existing = factory();

                // Tie room lifetime to MainWindow (BluesBar)
                existing.Owner = this;
                existing.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                existing.Closed += (_, __) => ClearReference(existing);

                existing.Show();
                return existing;
            }

            // Bring existing window to front
            if (!existing.IsVisible)
                existing.Show();

            existing.Activate();
            existing.Focus();

            return existing;
        }

        private void ClearReference(Window closed)
        {
            if (ReferenceEquals(closed, _aim))
                _aim = null;
            else if (ReferenceEquals(closed, _gambloo))
                _gambloo = null;
            else if (ReferenceEquals(closed, _backpack))
                _backpack = null;
            else if (ReferenceEquals(closed, _store))
                _store = null;
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void RefreshProfileHud()
        {
            var p = Systems.ProfileManager.Instance.Current;

            PlayerNameText.Text = p.PlayerName;
            NetWorthText.Text = $"${p.NetWorth:N0}";
            SavingsText.Text = $"{p.Coins:N0} Coins";
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Systems.ProfileManager.Instance.Save();
        }

        private void InitProfileCoinSync()
        {
            _profileSync = ProfileSync.CreateDefault();

            // Ensure profile exists and hydrate your in-app ProfileManager
            var profile = _profileSync.LoadOrCreateLocked();
            BluesBar.Systems.ProfileManager.Instance.LoadOrCreate(); // if you already do this elsewhere, keep only one call

            // Set displayed coins immediately
            BluesBar.Systems.ProfileManager.Instance.Current.Coins = profile.Coins;
            RefreshProfileHud();

            // Live updates from AimTrain or BluesBar writes
            _profileSync.CoinsChanged += coins =>
            {
                Dispatcher.Invoke(() =>
                {
                    BluesBar.Systems.ProfileManager.Instance.Current.Coins = coins;
                    RefreshProfileHud();
                });
            };

            _profileSync.StartWatching();
        }

        protected override void OnClosed(EventArgs e)
        {
            _profileSync?.Dispose();
            base.OnClosed(e);
        }

        public void AwardCoins(int amount)
        {
            if (amount <= 0) return;

            // Writes into BluesBar's profile.json under the shared lock.
            _profileSync.EarnLocked(amount, reason: "AimTrain");
        }


        //DEBUGGING ONLY -- remove before shipping
        private void DebugEarn_Click(object sender, RoutedEventArgs e)
        {
            Systems.ProfileManager.Instance.Earn(100000, "Debug Earn");
            RefreshProfileHud();
        }
        //DEBUGGING ONLY -- remove before shipping

        
    }




}
