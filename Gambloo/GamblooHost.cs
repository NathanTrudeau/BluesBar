using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace BluesBar.Gambloo
{
    public sealed class GamblooHost
    {
        private readonly Dictionary<string, (UserControl control, IGamblooScene scene)> _scenes = new();
        public IGamblooScene? ActiveScene { get; private set; }
        public UserControl? ActiveControl { get; private set; }

        public IReadOnlyList<IGamblooScene> AllScenes =>
            _scenes.Values.Select(v => v.scene).ToList();

        public void Register(UserControl control)
        {
            if (control is not IGamblooScene scene)
                throw new InvalidOperationException("Scene control must implement IGamblooScene.");

            _scenes[scene.SceneId] = (control, scene);
        }

        public bool CanSwapTo(string sceneId)
        {
            if (!_scenes.ContainsKey(sceneId)) return false;
            if (ActiveScene == null) return true;
            return !ActiveScene.IsBusy;
        }

        public UserControl SwapTo(string sceneId)
        {
            if (!_scenes.TryGetValue(sceneId, out var next))
                throw new InvalidOperationException($"Scene not found: {sceneId}");

            // Block swaps if busy
            if (ActiveScene != null && ActiveScene.IsBusy)
                return ActiveControl!;

            // Hide old
            ActiveScene?.OnHidden();

            // Show new
            ActiveScene = next.scene;
            ActiveControl = next.control;
            ActiveScene.OnShown();

            return ActiveControl;
        }
    }
}
