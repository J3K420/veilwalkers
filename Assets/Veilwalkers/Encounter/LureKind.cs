namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The kind of Lure the player chose (Story 4.2, FR-6). Each maps to a Credit cost (read from
    /// <c>EconomyConfig</c>) and a rare-tier roll probability in <see cref="LureSystem"/>.
    /// <para>
    /// <b>Story 5.3 adds <c>GuaranteedRare</c></b> — the one-shot Veil-Pack item that FORCES a spawn of
    /// <c>Rarity &gt;= Rare</c> (not a probabilistic roll). It is deliberately NOT here yet: 4.2 builds the
    /// three probabilistic kinds only.
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
    }
}
