using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// One diegetic "system moment" the <see cref="VeilVoice"/> table speaks for (Story 6.5; UX-DR14).
    /// A small closed set — the moments the AC enumerates — so <see cref="VeilVoice.For"/> can be a
    /// total enum→copy map (the <c>DreadScaleTokens.ForTier</c> total-resolver precedent).
    /// </summary>
    public enum VeilMoment
    {
        /// <summary>Entering AR Mode — "Part the Veil".</summary>
        EnterAr,

        /// <summary>A rewarded action (daily / ad reward) — "Consult the Veil".</summary>
        RewardedAction,

        /// <summary>A new tier was discovered — "The Veil shows you more…".</summary>
        NewTier,

        /// <summary>An anchor was relocated/restored — "Pulled back through the Veil".</summary>
        AnchorRestored,

        /// <summary>A locked / undiscovered Codex slot — "Not yet discovered.".</summary>
        LockedSlot,
    }

    /// <summary>
    /// The SINGLE home for the app's diegetic "Veil voice" system-moment copy (Story 6.5; UX-DR14).
    /// A pure static copy class (the <see cref="PumpkinPatchTokens"/> / <see cref="DreadScaleTokens"/>
    /// static-constants precedent) — the canonical wording is defined ONCE here, and every system
    /// moment ("Part the Veil" on AR entry, "Consult the Veil" on a reward, "The Veil shows you more…"
    /// on a new tier, "Pulled back through the Veil" on an anchor restore, "Not yet discovered." on a
    /// locked slot) reads it from here, never re-baked per call site (AC-1). The existing deferred
    /// placeholders point here: the Home daily-reward label (<c>HomeViewModel</c>) and the Codex
    /// locked-slot copy (<c>CodexDetailViewModel.NotDiscoveredCopy</c>).
    /// <para>
    /// <b>The safety EXCEPTION (AC-2; UX-DR14 exception).</b> Safety / disclosure copy is the explicit
    /// exception — it is PLAIN, legible, high-contrast, and NEVER costumed in Veil voice. It lives in
    /// the nested <see cref="Safety"/> class, structurally segregated from the diegetic lines so a
    /// safety moment can never accidentally borrow a flavored string. The
    /// <see cref="StateTreatmentPresenter"/> upholds the firewall: a safety treatment carries ONLY a
    /// <see cref="Safety"/> string, never a diegetic one.
    /// </para>
    /// </summary>
    public static class VeilVoice
    {
        // ---- Diegetic system-moment voice (AC-1; UX-DR14) ----

        /// <summary>Enter AR Mode.</summary>
        public const string EnterAr = "Part the Veil";

        /// <summary>A rewarded action (daily / ad reward) — the label the Home daily-reward control
        /// surfaces (the <c>HomeViewModel</c> placeholder this closes).</summary>
        public const string RewardedAction = "Consult the Veil";

        /// <summary>A new dread-scale tier was discovered. Uses the typographic ellipsis (U+2026), per
        /// the AC + the UX-DR14 table.</summary>
        public const string NewTier = "The Veil shows you more…";

        /// <summary>An anchor was relocated onto a fresh plane and restored — the "never vanish" beat
        /// (architecture.md:472–473): the Monster is pulled back, not lost.</summary>
        public const string AnchorRestored = "Pulled back through the Veil";

        /// <summary>A locked / undiscovered Codex slot. The period form is authoritative (the AC at
        /// epics.md:925 and the existing <c>CodexDetailViewModel.NotDiscoveredCopy</c> value) — NOT the
        /// period-less UX-DR14 table entry.</summary>
        public const string LockedSlot = "Not yet discovered.";

        /// <summary>
        /// The total <see cref="VeilMoment"/>→copy map (the <c>DreadScaleTokens.ForTier</c> total-resolver
        /// precedent). Every defined moment returns its canonical non-empty line; an out-of-range moment
        /// (defensive — callers always pass a real moment) degrades to <see cref="EnterAr"/> with a
        /// <see cref="GameLog.Warn"/> rather than throwing (NFR-3 graceful boundary).
        /// </summary>
        public static string For(VeilMoment moment)
        {
            switch (moment)
            {
                case VeilMoment.EnterAr: return EnterAr;
                case VeilMoment.RewardedAction: return RewardedAction;
                case VeilMoment.NewTier: return NewTier;
                case VeilMoment.AnchorRestored: return AnchorRestored;
                case VeilMoment.LockedSlot: return LockedSlot;
                default:
                    GameLog.Warn($"VeilVoice.For: unmapped VeilMoment '{moment}' — degrading to EnterAr.");
                    return EnterAr;
            }
        }

        /// <summary>
        /// The PLAIN safety / disclosure copy (AC-2; the UX-DR14 EXCEPTION). NOT diegetic, NOT flavored —
        /// plain, legible, high-contrast. Structurally separate from the Veil-voice lines above so a
        /// safety moment can never wear a costume. The chunky safety-overlay RENDER stays deferred to the
        /// Epic-6 view layer (this only supplies the copy the deferred view binds — closing the
        /// <c>ArSafetyView</c> <c>TODO(Epic 6)</c> copy defer).
        /// </summary>
        public static class Safety
        {
            /// <summary>The full, deliberate-read AR Safety Warning body (the
            /// <see cref="ArSafetyGateState.BlockingFull"/> screen). Plain wording — never buried in
            /// flavor (epics.md:107 / epics.md:926).</summary>
            public const string ArWarningFull =
                "The Veil pulls your attention. Keep one eye on the real world — watch your step, "
                + "mind your surroundings, and don't play while walking, driving, or near hazards.";

            /// <summary>The fast minimal-dwell AR Safety Warning card (the
            /// <see cref="ArSafetyGateState.BlockingFastCard"/> screen) — shorter, still plain, still
            /// mandatory.</summary>
            public const string ArWarningFast =
                "Keep one eye on the real world. Mind your surroundings.";
        }
    }
}
