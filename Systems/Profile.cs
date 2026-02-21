using BluesShared;
using System;
using System.Windows.Media;

namespace BluesBar.Systems
{
    // UI wrapper around the shared persisted data.
    // DO NOT serialize this type to profile.json.
    public sealed class Profile
    {
        public BluesShared.Profile Data { get; }

        public Profile(BluesShared.Profile data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        // --- Identity ---
        public string PlayerName
        {
            get => Data.PlayerName;
            set => Data.PlayerName = value;
        }

        public string PlayerNameColorHex
        {
            get => Data.PlayerNameColorHex;
            set => Data.PlayerNameColorHex = value;
        }

        // --- Wallet ---
        public long Coins
        {
            get => Data.Coins;
            set => Data.Coins = value;
        }

        public long LifetimeEarned
        {
            get => Data.LifetimeEarned;
            set => Data.LifetimeEarned = value;
        }

        public long LifetimeSpent
        {
            get => Data.LifetimeSpent;
            set => Data.LifetimeSpent = value;
        }

        public long NetWorth => Data.LifetimeEarned;

        // --- Cosmetics ---
        public System.Collections.Generic.HashSet<string> UnlockedCursors
        {
            get => Data.UnlockedCursors;
            set => Data.UnlockedCursors = value;
        }

        public System.Collections.Generic.HashSet<string> UnlockedBackgrounds
        {
            get => Data.UnlockedBackgrounds;
            set => Data.UnlockedBackgrounds = value;
        }

        public System.Collections.Generic.HashSet<string> UnlockedSfx
        {
            get => Data.UnlockedSfx;
            set => Data.UnlockedSfx = value;
        }

        public string EquippedCursor
        {
            get => Data.EquippedCursor;
            set => Data.EquippedCursor = value;
        }

        public string EquippedBackground
        {
            get => Data.EquippedBackground;
            set => Data.EquippedBackground = value;
        }

        public string EquippedSfx
        {
            get => Data.EquippedSfx;
            set => Data.EquippedSfx = value;
        }

        // --- AimTrainer XP source ---
        public long LifetimeAimCoinsEarned
        {
            get => Data.LifetimeAimCoinsEarned;
            set => Data.LifetimeAimCoinsEarned = value;
        }

        // --- Metadata ---
        public DateTime CreatedUtc
        {
            get => Data.CreatedUtc;
            set => Data.CreatedUtc = value;
        }

        public DateTime UpdatedUtc
        {
            get => Data.UpdatedUtc;
            set => Data.UpdatedUtc = value;
        }

        public int SchemaVersion
        {
            get => Data.SchemaVersion;
            set => Data.SchemaVersion = value;
        }
    }
}
