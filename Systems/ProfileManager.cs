using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BluesBar.Systems
{
    public sealed class ProfileManager
    {
        // One manager for the app (easy mode)
        public static ProfileManager Instance { get; } = new ProfileManager();

        private readonly Mutex _mutex = new Mutex(false, "Global\\BluesBar_ProfileLock");
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public Profile Current { get; private set; } = new Profile();

        public string DataDir { get; }
        public string ProfilePath { get; }

        private ProfileManager()
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BluesBar");
            ProfilePath = Path.Combine(DataDir, "profile.json");
        }

        // ---------- Public lifecycle ----------
        public void LoadOrCreate()
        {
            Directory.CreateDirectory(DataDir);

            _mutex.WaitOne();
            try
            {
                if (!File.Exists(ProfilePath))
                {
                    Current = new Profile();
                    SaveInternal(); // create initial file
                    return;
                }

                var json = File.ReadAllText(ProfilePath);
                var loaded = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);

                Current = loaded ?? new Profile();
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public void Save()
        {
            _mutex.WaitOne();
            try
            {
                SaveInternal();
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        // ---------- Economy operations ----------
        public void Earn(long amount, string reason = "Earn")
        {
            if (amount <= 0) return;

            _mutex.WaitOne();
            try
            {
                Current.Coins += amount;
                Current.LifetimeEarned += amount;          // THIS is your “Net Worth”
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public bool Spend(long amount, string reason = "Spend")
        {
            if (amount <= 0) return true;

            _mutex.WaitOne();
            try
            {
                if (Current.Coins < amount)
                    return false;

                Current.Coins -= amount;
                Current.LifetimeSpent += amount;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
                return true;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public void Refund(long amount, string reason = "Refund")
        {
            if (amount <= 0) return;

            _mutex.WaitOne();
            try
            {
                Current.Coins += amount;
                // Refund does NOT increase LifetimeEarned. (Keeps “Net Worth” honest.)
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        // ---------- Inventory ----------
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

        public void EquipCursor(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            _mutex.WaitOne();
            try
            {
                // Optional rule: must be unlocked (you can relax this later)
                if (id != "Default" && !Current.UnlockedCursors.Contains(id))
                    return;

                Current.EquippedCursor = id;
                Current.UpdatedUtc = DateTime.UtcNow;
                SaveInternal();
            }
            finally { _mutex.ReleaseMutex(); }
        }

        // ---------- Internal atomic save ----------
        private void SaveInternal()
        {
            Directory.CreateDirectory(DataDir);

            Current.UpdatedUtc = DateTime.UtcNow;

            var tmp = ProfilePath + ".tmp";
            var backup = ProfilePath + ".bak";

            var json = JsonSerializer.Serialize(Current, _jsonOptions);

            // Write temp file first
            File.WriteAllText(tmp, json);

            // Backup existing profile if it exists
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

