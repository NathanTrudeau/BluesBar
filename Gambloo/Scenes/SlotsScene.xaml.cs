using System;
using System.Windows.Controls;
using BluesBar.Gambloo;

namespace BluesBar.Gambloo.Scenes
{
    public partial class SlotsScene : UserControl, IGamblooScene
    {
        public string SceneId => "slots";
        public string DisplayName => "Slots";

        public bool IsBusy { get; private set; } = false;
        public event Action<bool>? BusyChanged;

        public SlotsScene()
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
