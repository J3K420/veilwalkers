namespace Veilwalkers.UI
{
    /// <summary>
    /// The TalkBack / screen-reader ROLE of an interactive element (Story 6.6, AC-4; UX-DR17). The
    /// closed set of roles the Veilwalkers surfaces need — every interactive element is labeled with a
    /// role + state so a TalkBack user knows what it is and what it will do. PascalCase, the
    /// <see cref="StateTreatmentKind"/> / <c>OnboardingStep</c> enum precedent. Append-only (never
    /// reorder): the render maps each to the platform accessibility role.
    /// </summary>
    public enum AccessibilityRole
    {
        /// <summary>No announced role (a non-interactive decorative element). The graceful default.</summary>
        None,

        /// <summary>A tappable button / CTA (chunky button, the credit pill that taps to Shop, a pack-card Buy).</summary>
        Button,

        /// <summary>An image / picture element (a discovered Codex slot's art).</summary>
        Image,

        /// <summary>A tab / selectable grid cell (a Codex grid slot as a navigable cell).</summary>
        Tab,

        /// <summary>A header / section title (the wordmark, a surface heading).</summary>
        Header,

        /// <summary>An adjustable control with on/off or stepped state (the reduced-motion toggle).</summary>
        Adjustable,
    }
}
