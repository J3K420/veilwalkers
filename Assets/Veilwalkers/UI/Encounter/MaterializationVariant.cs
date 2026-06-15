namespace Veilwalkers.UI
{
    /// <summary>
    /// The per-tier materialization entrance variant (Story 4.7, UX-DR10). One member per
    /// <see cref="Veilwalkers.Monsters.Rarity"/> tier, ordered Common→Nightmare so the entrance
    /// "scales with tier" — a snappy <see cref="PopIn"/> for a common Monster up to the dramatic
    /// <see cref="Breach"/> for a Nightmare. The <see cref="MaterializationPresenter"/> picks the
    /// variant; the (Epic-6) view maps each to a concrete tween / FX. This enum is the DECISION,
    /// not the render — no Unity types, no animation lives here.
    /// </summary>
    public enum MaterializationVariant
    {
        /// <summary>T1 (Common) — a quick pop-in (~0.5s). The snappy, low-drama entrance.</summary>
        PopIn,

        /// <summary>T2 (Uncommon) — the Monster unfurls into place (~1s).</summary>
        Unfurl,

        /// <summary>T3 (Rare) — the Monster seeps in (~1.5s); the entrance starts to read as an event.</summary>
        Seep,

        /// <summary>T4 (Epic) — a tear in the veil (~2s).</summary>
        Tear,

        /// <summary>
        /// T5 (Nightmare) — a full Breach (~2.5–3s): the camera/cartoon-UI "corrupts" as a
        /// designed branded glitch (<see cref="MaterializationPlan.BrandedGlitch"/>), the hero
        /// entrance. Tamed to a glitch→static low-motion transition under reduced-motion (AC-3).
        /// </summary>
        Breach,
    }
}
