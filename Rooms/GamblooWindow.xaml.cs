using System.Windows;
using System.Windows.Input;
using BluesBar.Gambloo;
using BluesBar.Gambloo.Scenes;

namespace BluesBar.Rooms
{
    public partial class GamblooWindow : Window
    {
        private readonly GamblooHost _host = new GamblooHost();

        public GamblooWindow()
        {
            InitializeComponent();

            // Register scenes (real games later, placeholders now)
            _host.Register(new BlackjackScene());
            _host.Register(new RideTheBusScene());
            _host.Register(new RouletteScene());
            _host.Register(new SlotsScene());
            _host.Register(new RocketCrashScene());
            _host.Register(new BlinkoScene());
            _host.Register(new CoinFlipScene());
            _host.Register(new RockPaperScissorsScene());

            // Start on Blackjack
            SetScene("blackjack");
        }

        private void SetScene(string id)
        {
            if (!_host.CanSwapTo(id))
            {
                StatusText.Text = "Finish the current roll before switching.";
                return;
            }

            StatusText.Text = "";

            // Unhook old
            if (_host.ActiveScene != null)
                _host.ActiveScene.BusyChanged -= OnSceneBusyChanged;

            SceneHost.Content = _host.SwapTo(id);

            // Hook new
            if (_host.ActiveScene != null)
                _host.ActiveScene.BusyChanged += OnSceneBusyChanged;

            UpdateBusyUi();
        }

        private void OnSceneBusyChanged(bool busy)
        {
            UpdateBusyUi();
        }

        private void UpdateBusyUi()
        {
            // Optional overlay: if active scene is busy, block interactions
            var busy = _host.ActiveScene?.IsBusy ?? false;
            BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        // Nav buttons
        private void NavBlackjack_Click(object sender, RoutedEventArgs e) => SetScene("blackjack");
        private void NavRideTheBus_Click(object sender, RoutedEventArgs e) => SetScene("ridethebus");
        private void NavRoulette_Click(object sender, RoutedEventArgs e) => SetScene("roulette");
        private void NavSlots_Click(object sender, RoutedEventArgs e) => SetScene("slots");
        private void NavRocketCrash_Click(object sender, RoutedEventArgs e) => SetScene("rocketcrash");
        private void NavBlinko_Click(object sender, RoutedEventArgs e) => SetScene("blinko");
        private void NavCoinFlip_Click(object sender, RoutedEventArgs e) => SetScene("coinflip");
        private void NavRockPaperScissors_Click(object sender, RoutedEventArgs e) => SetScene("rockpaperscissors");



        // Title bar + window buttons
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}

