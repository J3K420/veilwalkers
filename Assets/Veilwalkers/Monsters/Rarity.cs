namespace Veilwalkers.Monsters
{
    /// <summary>
    /// The ordered rarity tier of a Monster (AR-3). Rarity = scariness = materialization
    /// tier — the UX "Dread Ladder" (OQ-2). The members ascend
    /// <see cref="Common"/> &lt; <see cref="Uncommon"/> &lt; <see cref="Rare"/> &lt;
    /// <see cref="Epic"/> &lt; <see cref="Nightmare"/>, so a <c>&gt;=</c> comparison on
    /// the enum is a meaningful "this tier or better" test.
    /// <para>
    /// <b>Append-only, never reorder.</b> The explicit underlying values are a contract:
    /// they are serialized into authored Monster <c>.asset</c> files and they back the
    /// <c>&gt;=</c> floor comparison used by the Guaranteed-Rare Lure (FR-13). Reordering
    /// members or changing their values would silently re-tier existing assets and break
    /// the floor — add new tiers only at the end with a new explicit value.
    /// </para>
    /// </summary>
    public enum Rarity
    {
        /// <summary>Lowest tier.</summary>
        Common = 0,

        /// <summary>Second tier.</summary>
        Uncommon = 1,

        /// <summary>
        /// Third tier — the Guaranteed-Rare Lure floor
        /// (<see cref="RarityThresholds.GuaranteedRareFloor"/>).
        /// </summary>
        Rare = 2,

        /// <summary>Fourth tier.</summary>
        Epic = 3,

        /// <summary>Highest (most fearsome) tier.</summary>
        Nightmare = 4,
    }

    /// <summary>
    /// Named <see cref="Rarity"/> thresholds, so callers never hard-code a tier literal.
    /// </summary>
    public static class RarityThresholds
    {
        /// <summary>
        /// The Guaranteed-Rare Lure floor (FR-13 / AR-13; architecture "Minimum rarity
        /// contract"): a Veil Pack's
        /// Guaranteed-Rare Lure forces a spawn of <c>Rarity &gt;= GuaranteedRareFloor</c>,
        /// i.e. <see cref="Rarity.Rare"/> or better. This is the single source of truth
        /// for that floor — Story 5.3's <c>GuaranteedRareLure</c> filters the spawn pool
        /// against it. The floor is <b>inclusive</b>: <see cref="Rarity.Rare"/> itself
        /// qualifies.
        /// </summary>
        public const Rarity GuaranteedRareFloor = Rarity.Rare;
    }
}
