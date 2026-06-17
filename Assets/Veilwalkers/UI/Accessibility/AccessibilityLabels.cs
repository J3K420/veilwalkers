namespace Veilwalkers.UI
{
    /// <summary>
    /// The per-component accessibility-label DECISIONS (Story 6.6, AC-4; UX-DR17) — pure logic that maps
    /// a 6.2 component descriptor (+ the live label/state the deferred view supplies) to an
    /// <see cref="AccessibilityLabel"/> (role + text + state). Every interactive element gets a role +
    /// state so a TalkBack user knows what it is and what it will do. The live binding (setting the
    /// platform accessibility node) is the deferred Epic-6 view concern; this is the DECISION.
    /// <para>
    /// <b>Reveal firewall (load-bearing — D8, the 2.4/2.5 contract).</b> An UNDISCOVERED Codex slot's
    /// label NEVER names the Monster — a <c>Silhouette</c> announces "Undiscovered", a <c>Blank</c>
    /// announces "Locked". Only a <see cref="CodexSlotState.Discovered"/> slot speaks the Monster's name.
    /// An accessibility label must not leak an undiscovered Monster's identity (the a11y output respects
    /// the same reveal firewall as the visual).
    /// </para>
    /// </summary>
    public static class AccessibilityLabels
    {
        /// <summary>The announced text for an undiscovered begun-tier (silhouette) slot.</summary>
        public const string UndiscoveredText = "Undiscovered";

        /// <summary>The announced text for an unreached-tier (blank) locked slot.</summary>
        public const string LockedText = "Locked";

        /// <summary>
        /// A chunky button's label: <see cref="AccessibilityRole.Button"/> + its ALL-CAPS
        /// <see cref="ChunkyButtonStyle.DisplayLabel"/>, with an optional disabled state. The SLAY
        /// button's label is "SLAY" (the icon + label are the UX-DR17 never-red-alone guarantee — the
        /// label carries the meaning even with no color).
        /// </summary>
        public static AccessibilityLabel ForButton(ChunkyButtonStyle button, bool enabled = true) =>
            AccessibilityLabel.Of(
                AccessibilityRole.Button,
                button.DisplayLabel,
                enabled ? null : "disabled");

        /// <summary>
        /// The credit pill's label: <see cref="AccessibilityRole.Button"/> (it taps to Shop) + the
        /// balance announced as "N credits". The pill never locks at zero, so a 0 balance is a valid
        /// "0 credits" announcement, never "disabled".
        /// </summary>
        public static AccessibilityLabel ForCreditPill(CreditPillStyle pill) =>
            AccessibilityLabel.Of(AccessibilityRole.Button, AccessibilityAnnouncer.Credits(pill.Count));

        /// <summary>
        /// A pack-card's label: <see cref="AccessibilityRole.Button"/> (it taps to Buy) + the pack's
        /// total Credits announced as "N credits" (base + bonus — the transparent total the card shows),
        /// with the soft-nudge badge ("POPULAR" / "BEST VALUE") carried as the state when present. The
        /// localized Play PRICE is bound at runtime (the 5.1 localized-price contract — never an in-code
        /// price), so the announcement speaks the grant, not the price.
        /// </summary>
        public static AccessibilityLabel ForPackCard(PackCardStyle card)
        {
            int total = card.BaseCredits + card.BonusCredits;
            string text = AccessibilityAnnouncer.Credits(total < 0 ? 0 : total);
            return AccessibilityLabel.Of(
                AccessibilityRole.Button,
                text,
                card.HasBadge ? card.BadgeText : null);
        }

        /// <summary>
        /// A Codex grid slot's label (REVEAL-PRESERVING — D8). A <see cref="CodexSlotState.Discovered"/>
        /// slot announces the Monster's <paramref name="discoveredName"/> as an <see cref="AccessibilityRole.Image"/>
        /// with a "discovered" state; an undiscovered slot NEVER names the Monster — a silhouette
        /// announces <see cref="UndiscoveredText"/>, a blank announces <see cref="LockedText"/>, both as a
        /// <see cref="AccessibilityRole.Tab"/> (a navigable grid cell). The <paramref name="discoveredName"/>
        /// is IGNORED for any non-discovered state — the reveal can never leak through the label.
        /// </summary>
        public static AccessibilityLabel ForCodexSlot(CodexSlotStyle slot, string discoveredName)
        {
            switch (slot.State)
            {
                case CodexSlotState.Discovered:
                    return AccessibilityLabel.Of(
                        AccessibilityRole.Image,
                        string.IsNullOrEmpty(discoveredName) ? "Discovered" : discoveredName,
                        "discovered");

                case CodexSlotState.Silhouette:
                    // Begun-tier, not yet found — never name the Monster (preserve the reveal).
                    return AccessibilityLabel.Of(AccessibilityRole.Tab, UndiscoveredText);

                case CodexSlotState.Blank:
                default:
                    // Unreached/unauthored — locked, never named (preserve the reveal).
                    return AccessibilityLabel.Of(AccessibilityRole.Tab, LockedText);
            }
        }
    }
}
