using BluesBar.Systems;

namespace BluesShared
{
    internal static class ProfileFixups
    {
        /// <summary>
        /// Applies legacy migrations needed for leveling data consistency.
        /// Returns true if the profile was modified.
        /// </summary>
        public static bool ApplyLegacyAimXpMigration(Profile profile)
        {
            if (profile == null) return false;

            bool changed = false;

            if (profile.LifetimeAimCoinsEarned <= 0 && profile.LifetimeEarned > 0)
            {
                profile.LifetimeAimCoinsEarned = profile.LifetimeEarned;
                changed = true;
            }

            // Ensure LastSeenTotalLevel has a sane baseline so we don't stay stuck at Lv1.
            if (profile.LastSeenTotalLevel <= 0)
            {
                var state = LevelCalculator.Compute(profile.LifetimeAimCoinsEarned);
                profile.LastSeenTotalLevel = state.TotalLevel;
                changed = true;
            }

            return changed;
        }
    }
}
