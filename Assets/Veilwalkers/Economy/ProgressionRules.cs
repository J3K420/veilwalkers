using System;
using System.Collections.Generic;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The immutable numbers <see cref="ProgressionService"/> needs to turn XP into
    /// levels and level-ups into charge grants. A plain class, NOT a ScriptableObject
    /// and NOT a set of constants in service logic.
    /// <para>
    /// <b>Why this seam (AR-16):</b> economy numbers must be data-driven and must not
    /// be hard-coded in service logic. Story 1.6 owns <c>EconomyConfig</c> (the
    /// tunable SO); it will construct and expose a <see cref="ProgressionRules"/> that
    /// Bootstrap reads from the asset. Until then Bootstrap injects a clearly-marked
    /// provisional instance, and tests inject tiny curves. Same pattern as Story 1.4's
    /// "cost is a parameter, never a constant in the service".
    /// </para>
    /// <para>
    /// Level model: <see cref="LevelXpThresholds"/> element <c>k</c> is the LIFETIME
    /// XP required to reach level <c>k+1</c>. The level for a given XP is the count of
    /// thresholds <c>&lt;= Xp</c> (an exactly-met threshold counts as reached), capped
    /// at the list length. So thresholds <c>{100, 250}</c> give: XP 0–99 → level 0,
    /// 100–249 → level 1, ≥250 → level 2 (the cap). The thresholds are strictly
    /// increasing, so this count is the contiguous run from the start.
    /// </para>
    /// </summary>
    public sealed class ProgressionRules
    {
        /// <summary>
        /// Lifetime-XP thresholds, strictly increasing and all positive. Element
        /// <c>k</c> = XP to reach level <c>k+1</c>; the list length is the level cap.
        /// </summary>
        public IReadOnlyList<int> LevelXpThresholds { get; }

        /// <summary>Strong Capture charges granted per single level-up. May be 0.</summary>
        public int StrongCapturePerLevelUp { get; }

        /// <summary>Stability Boost charges granted per single level-up. May be 0.</summary>
        public int StabilityBoostPerLevelUp { get; }

        /// <summary>Nightveil Filter charges granted per single level-up. May be 0.</summary>
        public int NightveilFilterPerLevelUp { get; }

        /// <summary>
        /// Validates the curve up front so an invalid rule set fails fast at
        /// construction (Bootstrap/1.6), never as a silent mis-level at runtime:
        /// thresholds must be non-null, non-empty, all positive, and strictly
        /// increasing; the per-level-up grants must be non-negative (a 0 grant for a
        /// type is legal — that type simply earns nothing on level-up).
        /// </summary>
        public ProgressionRules(
            IReadOnlyList<int> levelXpThresholds,
            int strongCapturePerLevelUp,
            int stabilityBoostPerLevelUp,
            int nightveilFilterPerLevelUp)
        {
            if (levelXpThresholds == null)
            {
                throw new ArgumentNullException(nameof(levelXpThresholds));
            }

            if (levelXpThresholds.Count == 0)
            {
                throw new ArgumentException(
                    "Progression requires at least one XP threshold.", nameof(levelXpThresholds));
            }

            int previous = 0;
            for (int i = 0; i < levelXpThresholds.Count; i++)
            {
                int threshold = levelXpThresholds[i];
                if (threshold <= 0)
                {
                    throw new ArgumentException(
                        $"XP thresholds must be positive; threshold[{i}] = {threshold}.",
                        nameof(levelXpThresholds));
                }

                if (threshold <= previous)
                {
                    throw new ArgumentException(
                        "XP thresholds must be strictly increasing; " +
                        $"threshold[{i}] = {threshold} is not greater than the previous {previous}.",
                        nameof(levelXpThresholds));
                }

                previous = threshold;
            }

            if (strongCapturePerLevelUp < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(strongCapturePerLevelUp), strongCapturePerLevelUp,
                    "A per-level-up grant cannot be negative.");
            }

            if (stabilityBoostPerLevelUp < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stabilityBoostPerLevelUp), stabilityBoostPerLevelUp,
                    "A per-level-up grant cannot be negative.");
            }

            if (nightveilFilterPerLevelUp < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nightveilFilterPerLevelUp), nightveilFilterPerLevelUp,
                    "A per-level-up grant cannot be negative.");
            }

            // Defensive copy: the caller's array must not be able to mutate the rules
            // after validation (immutability is the whole contract of this type).
            var copy = new int[levelXpThresholds.Count];
            for (int i = 0; i < levelXpThresholds.Count; i++)
            {
                copy[i] = levelXpThresholds[i];
            }

            LevelXpThresholds = Array.AsReadOnly(copy);
            StrongCapturePerLevelUp = strongCapturePerLevelUp;
            StabilityBoostPerLevelUp = stabilityBoostPerLevelUp;
            NightveilFilterPerLevelUp = nightveilFilterPerLevelUp;
        }

        /// <summary>
        /// The level for a given lifetime <paramref name="xp"/>: the count of
        /// thresholds <c>&lt;= xp</c>, capped at the threshold count. Single source of
        /// the level formula so <see cref="ProgressionService"/> never re-derives it.
        /// </summary>
        public int LevelForXp(int xp)
        {
            int level = 0;
            for (int i = 0; i < LevelXpThresholds.Count; i++)
            {
                if (LevelXpThresholds[i] <= xp)
                {
                    level++;
                }
                else
                {
                    // Strictly increasing: once one threshold is unmet, all later are.
                    break;
                }
            }

            return level;
        }

        /// <summary>
        /// The per-level-up grant for <paramref name="type"/> (the bundle added once
        /// for each level gained). Routes through the same mapping discipline as the
        /// counts. An undefined enum value throws <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        public int GrantPerLevelUp(ChargeType type)
        {
            switch (type)
            {
                case ChargeType.StrongCapture:
                    return StrongCapturePerLevelUp;
                case ChargeType.StabilityBoost:
                    return StabilityBoostPerLevelUp;
                case ChargeType.NightveilFilter:
                    return NightveilFilterPerLevelUp;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type), type, "Unknown charge type.");
            }
        }
    }
}
