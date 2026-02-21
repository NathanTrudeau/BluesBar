using BluesBar.Rooms;
using BluesBar.Systems;
using BluesShared;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        private ProfileManagerWindow? _profileManager;

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

        private void OpenProfileManager_Click(object sender, RoutedEventArgs e)
        {
            _profileManager = OpenOrActivate(_profileManager, () =>
            {
                var w = new ProfileManagerWindow();
                w.ProfileSaved += () =>
                {
                    RefreshProfileHud(); // reflect new username/color immediately
                };
                return w;
            });
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
            else if (ReferenceEquals(closed, _profileManager))
                _profileManager = null;
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
            var shared = Systems.ProfileManager.Instance.Shared;

            // Detection scaffold update (pending accumulation)
            bool changed = Systems.LevelProgression.DetectAndAccumulatePending(shared);
            if (changed)
                Systems.ProfileManager.Instance.Save();

            // Derived level from Aim XP
            var state = Systems.LevelCalculator.Compute(shared.LifetimeAimCoinsEarned);

            PlayerNameText.Text = p.PlayerName;

            try
            {
                PlayerNameText.Foreground = (Brush)new BrushConverter().ConvertFromString(p.PlayerNameColorHex);
            }
            catch
            {
                PlayerNameText.Foreground = Brushes.DarkSlateBlue;
            }

            // New: level line (helmet later)
            if (LevelLineText != null)
                LevelLineText.Text = $"Lv {state.LevelInPrestige}";

            // Existing money display
            NetWorthText.Text = Systems.NumberFormat.AbbrevMoney((long)p.NetWorth);
            SavingsText.Text = $"{Systems.NumberFormat.Abbrev(p.Coins)} Coins";
        }


        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Systems.ProfileManager.Instance.Save();
        }

        private void InitProfileCoinSync()
        {
            _profileSync = ProfileSync.CreateDefault();

            void HydrateAndRefresh()
            {
                var disk = _profileSync.LoadOrCreateLocked();
                BluesBar.Systems.ProfileManager.Instance.HydrateFromDisk(disk);
                RefreshProfileHud();
            }

            // initial
            HydrateAndRefresh();

            _profileSync.CoinsChanged += _ =>
            {
                Dispatcher.Invoke(HydrateAndRefresh);
            };

            _profileSync.StartWatching();
        }




        protected override void OnClosed(EventArgs e)
        {
            _profileSync?.Dispose();
            base.OnClosed(e);
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
