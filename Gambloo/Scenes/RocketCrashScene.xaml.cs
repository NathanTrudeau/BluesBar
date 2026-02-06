using System;
using System.Windows.Controls;
using BluesBar.Gambloo;

namespace BluesBar.Gambloo.Scenes
{
    public partial class RocketCrashScene : UserControl, IGamblooScene
    {
        public string SceneId => "rocketcrash";
        public string DisplayName => "RocketCrash";

        public bool IsBusy { get; private set; } = false;
        public event Action<bool>? BusyChanged;

        public RocketCrashScene()
        {
            InitializeComponent();
        }

        public void OnShown() { }
        public void OnHidden() { }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }
    }
}
