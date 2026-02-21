using System;

namespace BluesBar.Systems
{
    public static class LevelProgression
    {
        /// <summary>
        /// Updates PendingLevelUps based on last seen derived total level.
        /// Returns true if it changed.
        /// </summary>
        public static bool DetectAndAccumulatePending(BluesShared.Profile shared)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));

            var state = LevelCalculator.Compute(shared.LifetimeAimCoinsEarned);
            int now = state.TotalLevel;

            int lastSeen = shared.LastSeenTotalLevel;
            if (lastSeen <= 0)
            {
                // First time initializing: set baseline to current so we don't spam popups.
                shared.LastSeenTotalLevel = now;
                shared.PendingLevelUps = 0;
                return true;
            }

            if (now > lastSeen)
            {
                shared.PendingLevelUps += (now - lastSeen);
                // DO NOT advance LastSeen here. That happens when user "redeems" later.
                return true;
            }

            return false;
        }

        /// <summary>
        /// Call this later when user redeems level ups (popup button / redeem all).
        /// Sets LastSeenTotalLevel to current and clears PendingLevelUps.
        /// </summary>
        public static bool RedeemAllPending(BluesShared.Profile shared)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));

            var state = LevelCalculator.Compute(shared.LifetimeAimCoinsEarned);
            int now = state.TotalLevel;

            bool changed = false;

            if (shared.LastSeenTotalLevel != now)
            {
                shared.LastSeenTotalLevel = now;
                changed = true;
            }

            if (shared.PendingLevelUps != 0)
            {
                shared.PendingLevelUps = 0;
                changed = true;
            }

            return changed;
        }
    }
}

