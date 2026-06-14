namespace Veilwalkers.UI
{
    /// <summary>
    /// The reveal state of one of the 67 Codex grid slots (Story 2.4, OQ-7 hybrid reveal).
    /// The presenter classifies every slot into exactly one of these; Epic 6 renders the
    /// pixels (art, tier color, caught stamp, flip animation) per state.
    /// </summary>
    public enum CodexSlotState
    {
        /// <summary>
        /// The Monster has been discovered: full art + a tier-tinted "caught" stamp
        /// (rendered in Epic 6). Discovered always wins — regardless of tier or whether the
        /// id is authored.
        /// </summary>
        Discovered,

        /// <summary>
        /// Undiscovered, but in a tier the player has <em>begun</em> encountering (at or one
        /// tier above the Dread-Ladder high-water mark): shown as a <c>???</c> silhouette.
        /// Only an authored id (one with a known <see cref="Veilwalkers.Monsters.Rarity"/>)
        /// can be silhouetted.
        /// </summary>
        Silhouette,

        /// <summary>
        /// Undiscovered in a tier not yet reached, OR a reserved id with no authored
        /// definition (no known tier to silhouette): shown as a <c>?</c> blank. Preserves the
        /// reveal for higher tiers and for not-yet-authored content.
        /// </summary>
        Blank,
    }
}
