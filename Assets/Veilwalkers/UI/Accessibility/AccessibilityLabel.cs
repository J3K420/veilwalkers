namespace Veilwalkers.UI
{
    /// <summary>
    /// A TalkBack / screen-reader label for one interactive element (Story 6.6, AC-4; UX-DR17): a
    /// <see cref="Role"/> + a human <see cref="Text"/> + an optional <see cref="State"/> (e.g.
    /// "selected", "12 of 67", "disabled"). A pure-logic <c>readonly struct</c> (the
    /// <see cref="StateTreatment"/> / <see cref="MaterializationPlan"/> private-ctor + factory
    /// precedent): every interactive component descriptor surfaces one; the deferred Epic-6 view binds
    /// it to the platform accessibility node. All fields are null-safe (coalesce → empty), so a
    /// <c>default(AccessibilityLabel)</c> never NREs and <see cref="ToScreenReaderString"/> never
    /// throws (NFR-3 graceful).
    /// <para>
    /// <b>Canonical announce order (pinned):</b> <c>Text</c>, then <c>State</c> (when present), then the
    /// <c>Role</c> word — e.g. "Slay, button" / "Antlered Shade, 12 of 67, image". Empty parts are
    /// dropped; a <see cref="AccessibilityRole.None"/> role contributes no role word.
    /// </para>
    /// </summary>
    public readonly struct AccessibilityLabel
    {
        private readonly string _text;
        private readonly string _state;

        /// <summary>The element's role (button / image / tab / …).</summary>
        public readonly AccessibilityRole Role;

        /// <summary>The human label ("Slay", "Antlered Shade", "20 credits"). Null-safe → empty.</summary>
        public string Text => _text ?? string.Empty;

        /// <summary>The optional state suffix ("selected" / "12 of 67" / "disabled"). Null-safe → empty.</summary>
        public string State => _state ?? string.Empty;

        private AccessibilityLabel(AccessibilityRole role, string text, string state)
        {
            Role = role;
            _text = text ?? string.Empty;
            _state = state ?? string.Empty;
        }

        /// <summary>Build a label with a role + text + optional state. Null/empty inputs are coalesced
        /// to empty (never throws).</summary>
        public static AccessibilityLabel Of(AccessibilityRole role, string text, string state = null) =>
            new AccessibilityLabel(role, text, state);

        /// <summary>
        /// The canonical TalkBack string: <c>Text</c> + <c>State</c> (when present) + the role word
        /// (when the role is not <see cref="AccessibilityRole.None"/>), comma-joined. Empty parts are
        /// dropped. Always returns a non-empty calm string when ANY part is present; a fully-empty label
        /// (default) returns <see cref="string.Empty"/> rather than throwing (NFR-3 graceful).
        /// </summary>
        public string ToScreenReaderString()
        {
            string text = Text;
            string state = State;
            string role = RoleWord(Role);

            var parts = new System.Collections.Generic.List<string>(3);
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
            if (!string.IsNullOrEmpty(state)) parts.Add(state);
            if (!string.IsNullOrEmpty(role)) parts.Add(role);

            return string.Join(", ", parts);
        }

        // The spoken role word. None → no word (an empty string the join drops). Lowercase, the
        // natural TalkBack phrasing ("…, button").
        private static string RoleWord(AccessibilityRole role)
        {
            switch (role)
            {
                case AccessibilityRole.Button: return "button";
                case AccessibilityRole.Image: return "image";
                case AccessibilityRole.Tab: return "tab";
                case AccessibilityRole.Header: return "header";
                case AccessibilityRole.Adjustable: return "adjustable";
                case AccessibilityRole.None: return string.Empty;
                // An unmapped/out-of-range role degrades to no role word rather than throwing at the
                // announce boundary (NFR-3 graceful — the ForTier default-to-calmest precedent).
                default: return string.Empty;
            }
        }
    }
}
