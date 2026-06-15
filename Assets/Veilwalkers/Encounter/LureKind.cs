namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The kind of Lure the player chose (Story 4.2, FR-6). Each maps to a Credit cost (read from
    /// <c>EconomyConfig</c>) and a rare-tier roll probability in <see cref="LureSystem"/>.
    /// <para>
    /// <b><c>GuaranteedRare</c> (Story 5.3)</b> — the one-shot Veil-Pack item that FORCES a spawn of
    /// <c>Rarity &gt;= Rare</c> (NOT a probabilistic roll). It costs ZERO Credits (the Veil pack already paid;
    /// it is consumed from <c>SaveModel.GuaranteedRareLures</c>, never Credits) and spawns ONE Monster from the
    /// rare-or-better pool with NO common fallback (<see cref="LureSystem.TryRollGuaranteedRare"/>).
    /// </para>
    /// </summary>
    public enum LureKind
    {
        /// <summary>Basic Lure (1 Credit): one Monster, baseline rare chance.</summary>
        Basic,

        /// <summary>Premium Lure (4 Credits): one Monster, a STRICTLY higher rare chance than Basic (AC-2).</summary>
        Premium,

        /// <summary>Multi-Lure (5 Credits): two Monsters in one encounter (AC-3), each rolled independently.</summary>
        Multi,

        /// <summary>Guaranteed-Rare Lure (Story 5.3, FR-13): a one-shot Veil-Pack item, ZERO Credit cost,
        /// consumed from <c>SaveModel.GuaranteedRareLures</c>. FORCES a single <c>Rarity &gt;= Rare</c> spawn
        /// (no probabilistic roll, no common fallback).</summary>
        GuaranteedRare,
    }
}
