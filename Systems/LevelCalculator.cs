using System;

namespace BluesBar.Systems
{
    /// <summary>
    /// Computes derived leveling from LifetimeAimCoinsEarned (XP).
    /// - 1..100 levels per prestige
    /// - 10 prestiges max (total level cap 1000)
    /// - Target: 10,000,000 coins total to complete one prestige (1..100)
    /// </summary>
    public static class LevelCalculator
    {
        // Tunables (easy to tweak later)
        // Base level cost in "XP coins" (AimTrainer coins) at the start of each prestige.
        public const double BaseCost = 25_000.0;

        // Shape of curve. 2.0 = smooth quadratic ramp.
        public const double Power = 2.0;

        // Target sum of costs for levels 1..100 inside one prestige.
        public const double TargetPrestigeTotal = 10_000_000.0;

        public const int LevelsPerPrestige = 100;
        public const int MaxPrestige = 10;
        public const int MaxTotalLevel = LevelsPerPrestige * MaxPrestige; // 1000

        public readonly record struct LevelState(int TotalLevel, int Prestige, int LevelInPrestige);

        /// <summary>
        /// Compute the player's derived level state from lifetime AimTrainer XP coins.
        /// </summary>
        public static LevelState Compute(long lifetimeAimCoinsEarned)
        {
            if (lifetimeAimCoinsEarned < 0) lifetimeAimCoinsEarned = 0;

            // Total level starts at 1
            int totalLevel = 1;
            int prestige = 1;
            int levelInPrestige = 1;

            // We walk levels until we run out of XP or hit cap.
            double xp = lifetimeAimCoinsEarned;

            // Precompute growth multiplier A so sum(costs 1..100) = TargetPrestigeTotal
            double a = ComputeGrowthMultiplierA();

            while (totalLevel < MaxTotalLevel)
            {
                double cost = CostForLevel(levelInPrestige, a);
                if (xp < cost) break;

                xp -= cost;

                totalLevel++;
                levelInPrestige++;

                if (levelInPrestige > LevelsPerPrestige)
                {
                    prestige++;
                    if (prestige > MaxPrestige)
                    {
                        // Cap hard at level 1000 (prestige 10, level 100)
                        prestige = MaxPrestige;
                        levelInPrestige = LevelsPerPrestige;
                        totalLevel = MaxTotalLevel;
                        break;
                    }

                    levelInPrestige = 1;
                }
            }

            // Clamp for safety
            if (totalLevel < 1) totalLevel = 1;
            if (totalLevel > MaxTotalLevel) totalLevel = MaxTotalLevel;
            if (prestige < 1) prestige = 1;
            if (prestige > MaxPrestige) prestige = MaxPrestige;
            if (levelInPrestige < 1) levelInPrestige = 1;
            if (levelInPrestige > LevelsPerPrestige) levelInPrestige = LevelsPerPrestige;

            return new LevelState(totalLevel, prestige, levelInPrestige);
        }

        /// <summary>
        /// Cost to advance from level L -> L+1 within a prestige (L is 1..100).
        /// </summary>
        private static double CostForLevel(int levelInPrestige, double a)
        {
            // Normalize level across 0..1
            double t = (levelInPrestige - 1) / 99.0; // 0 at L=1, 1 at L=100
            double ramp = Math.Pow(t, Power);

            // BaseCost * (1 + A * ramp)
            return BaseCost * (1.0 + a * ramp);
        }

        /// <summary>
        /// Solve A such that sum_{L=1..100} CostForLevel(L, A) = TargetPrestigeTotal.
        /// With our cost form, sum = BaseCost * (100 + A * sum(ramp)).
        /// </summary>
        private static double ComputeGrowthMultiplierA()
        {
            double sumRamp = 0.0;
            for (int l = 1; l <= LevelsPerPrestige; l++)
            {
                double t = (l - 1) / 99.0;
                sumRamp += Math.Pow(t, Power);
            }

            // Target = BaseCost * (100 + A * sumRamp)
            // A = (Target/BaseCost - 100) / sumRamp
            double a = (TargetPrestigeTotal / BaseCost - LevelsPerPrestige) / sumRamp;

            // Safety clamp: never allow negative growth
            if (a < 0) a = 0;

            return a;
        }
    }
}

