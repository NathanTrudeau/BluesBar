using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BluesBar.Systems
{
    public sealed class ProfileManager
    {
        public static ProfileManager Instance { get; } = new ProfileManager();

        private readonly Mutex _mutex = new Mutex(false, "Global\\BluesBar_ProfileLock");
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // IMPORTANT: shared schema type (the one that goes to disk)
        public BluesShared.Profile Shared { get; private set; } = new BluesShared.Profile();

        public Profile Current { get; private set; }

        public string DataDir { get; }
        public string ProfilePath { get; }

        private ProfileManager()
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BluesBar");
            ProfilePath = Path.Combine(DataDir, "profile.json");
            Current = new Profile(Shared);
        }

        public void LoadOrCreate()
        {
            Directory.CreateDirectory(DataDir);

            _mutex.WaitOne();
            try
            {
                if (!File.Exists(ProfilePath))
                {
                    Shared = new BluesShared.Profile();
                    Current = new Profile(Shared);
                    SaveInternal();
                    return;
                }

                var json = File.ReadAllText(ProfilePath);
                var loaded = JsonSerializer.Deserialize<BluesShared.Profile>(json, _jsonOptions);

                Shared = loaded ?? new BluesShared.Profile();
                Current = new Profile(Shared);
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public void Save()
        {
            _mutex.WaitOne();
            try { SaveInternal(); }
            finally { _mutex.ReleaseMutex(); }
        }

        public void Earn(long amount, string reason = "Earn")
        {
            if (amount <= 0) return;

            _mutex.WaitOne();
            try
            {
                Current.Coins += amount;
                Current.LifetimeEarned += amount;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public bool Spend(long amount, string reason = "Spend")
        {
            if (amount <= 0) return true;

            _mutex.WaitOne();
            try
            {
                if (Current.Coins < amount) return false;

                Current.Coins -= amount;
                Current.LifetimeSpent += amount;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
                return true;
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public void Refund(long amount, string reason = "Refund")
        {
            if (amount <= 0) return;

            _mutex.WaitOne();
            try
            {
                Current.Coins += amount;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public void UnlockCursor(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            _mutex.WaitOne();
            try
            {
                Current.UnlockedCursors.Add(id);
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public void HydrateFromDisk(BluesShared.Profile disk)
        {
            if (disk == null) return;

            _mutex.WaitOne();
            try
            {
                Shared = disk;
                Current = new Profile(Shared);
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }


        public void EquipCursor(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            _mutex.WaitOne();
            try
            {
                if (id != "Default" && !Current.UnlockedCursors.Contains(id))
                    return;

                Current.EquippedCursor = id;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally { _mutex.ReleaseMutex(); }
        }

        private void SaveInternal()
        {
            Directory.CreateDirectory(DataDir);
            Shared.UpdatedUtc = DateTime.UtcNow;

            var tmp = ProfilePath + ".tmp";
            var backup = ProfilePath + ".bak";

            var json = JsonSerializer.Serialize(Shared, _jsonOptions);
            File.WriteAllText(tmp, json);

            if (File.Exists(ProfilePath))
            {
                File.Copy(ProfilePath, backup, true);
                File.Replace(tmp, ProfilePath, null);
            }
            else
            {
                File.Move(tmp, ProfilePath);
            }
        }
    }
}
