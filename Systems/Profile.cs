using System;
using System.Collections.Generic;

namespace BluesBar.Systems
{
    public class Profile
    {
        // Identity (optional for now)
        public string PlayerName { get; set; } = "b1uepack";

        // Wallet
        public long Coins { get; set; } = 0;                 // spendable balance ("savings")
        public long LifetimeEarned { get; set; } = 0;         // total earned EVER (monotonic) = "Net Worth"
        public long LifetimeSpent { get; set; } = 0;          // total spent EVER (for stats)

        // Cosmetic inventory
        public HashSet<string> UnlockedCursors { get; set; } = new();
        public HashSet<string> UnlockedBackgrounds { get; set; } = new();
        public HashSet<string> UnlockedSfx { get; set; } = new();

        // Equipped cosmetics
        public string EquippedCursor { get; set; } = "Default";
        public string EquippedBackground { get; set; } = "Default";
        public string EquippedSfx { get; set; } = "Default";

        // Convenience: your “phony” net worth flex number
        public long NetWorth => LifetimeEarned;

        // Metadata (nice for debugging)
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public int SchemaVersion { get; set; } = 1;
    }
}

