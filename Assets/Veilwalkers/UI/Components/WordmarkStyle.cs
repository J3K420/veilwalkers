namespace Veilwalkers.UI
{
    /// <summary>
    /// The wordmark component descriptor (Story 6.2; UX-DR8). A pure-logic <c>readonly struct</c> for
    /// the chunky outlined "Veilwalkers" display lockup — the brand anchor on Home + onboarding, with
    /// personality (a slight wobble). The render (the display font, the outline sprite, the wobble
    /// motion) is Story 6.3 / 6.5.
    /// <para>
    /// <b>UX-DR8:</b> a chunky outlined display lockup with a slight wobble; a brand anchor on Home +
    /// onboarding; NEVER used for body text (<see cref="BodyTextForbidden"/>). It carries the same hard
    /// outline + offset shadow elevation device as every other chunky surface (AC-2).
    /// </para>
    /// </summary>
    public readonly struct WordmarkStyle
    {
        /// <summary>The literal wordmark text — the brand name, exactly "Veilwalkers".</summary>
        public const string Text = "Veilwalkers";

        /// <summary>
        /// The "slight wobble" rotation magnitude (degrees) that gives the lockup personality (UX-DR8).
        /// A small feel value a 6.3 / 6.5 polish pass may tune; the AC is "slight wobble" (a small
        /// non-zero tilt), pinned as &gt; 0 and small.
        /// </summary>
        public const float WobbleDegrees = 3f;

        /// <summary>The chunky elevation style — text-primary fill, md radius, the hard outline + shadow (AC-2).</summary>
        public ChunkyStyle Style { get; }

        /// <summary>The wordmark text to render (always <see cref="Text"/>).</summary>
        public string DisplayText => Text;

        /// <summary>The wobble magnitude for this lockup (degrees).</summary>
        public float Wobble { get; }

        /// <summary>
        /// Always true — the wordmark is a display lockup ONLY and must NEVER be used for body text
        /// (UX-DR8). A documented, testable marker of that discipline.
        /// </summary>
        public bool BodyTextForbidden => true;

        private WordmarkStyle(ChunkyStyle style, float wobble)
        {
            Style = style;
            Wobble = wobble;
        }

        /// <summary>
        /// Build the wordmark lockup. Text-primary fill (the warm cream) on the chunky outline + hard
        /// shadow elevation device, with the slight wobble — all from Story-6.1 tokens (AC-2).
        /// </summary>
        public static WordmarkStyle Create()
        {
            return new WordmarkStyle(
                ChunkyStyle.Elevated(PumpkinPatchTokens.TextPrimary, PumpkinPatchTokens.RadiusMd),
                WobbleDegrees);
        }
    }
}
