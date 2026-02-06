using System;
using System.Windows.Controls;
using BluesBar.Gambloo;

namespace BluesBar.Gambloo.Scenes
{
    public partial class BlackjackScene : UserControl, IGamblooScene
    {
        public string SceneId => "blackjack";
        public string DisplayName => "Blackjack";

        public bool IsBusy { get; private set; } = false;
        public event Action<bool>? BusyChanged;

        public BlackjackScene()
        {
            InitializeComponent();
        }

        public void OnShown() { }
        public void OnHidden() { }

        // Optional helper for later when you add dealing animations
        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }
    }
}

