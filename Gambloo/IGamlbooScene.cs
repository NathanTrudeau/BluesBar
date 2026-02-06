namespace BluesBar.Gambloo
{
    public interface IGamblooScene
    {
        string SceneId { get; }
        string DisplayName { get; }
        bool IsBusy { get; }

        event Action<bool>? BusyChanged;
        void OnShown();
        void OnHidden();
    }
}

