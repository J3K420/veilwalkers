using System;
using Veilwalkers.Core;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The Capture success-roll decision (Story 4.4, FR-8; architecture.md:416 names this file as a sibling of
    /// <see cref="LureSystem"/>/<c>SlaySystem</c>). Pure C# (NO MonoBehaviour, NO Unity types),
    /// constructor-injected (AR-4 — NEVER <c>GameServices.Get&lt;T&gt;()</c>). It makes ONLY the decision:
    /// does a Capture attempt SUCCEED? — a draw on <see cref="IRandom.NextDouble"/> against the chance for the
    /// chosen variant (base vs Strong). It does NOT consume a charge, persist, or stage a discovery — those are
    /// the <see cref="EncounterService"/> composed write (the same altitude split as <see cref="LureSystem"/>:
    /// the system rolls, the service composes). Free of side effects, so the roll is trivially headless-testable
    /// against a scripted <see cref="IRandom"/>.
    /// <para>
    /// <b>The success roll (AC-1/AC-2).</b> A draw below the variant's chance is a success:
    /// <c>_random.NextDouble() &lt; chance</c> (the <see cref="LureSystem"/> rarity-roll precedent). The HARD
    /// invariant (AC-2): <see cref="StrongCaptureChance"/> &gt; <see cref="BaseCaptureChance"/> STRICTLY, so the
    /// Strong attempt's success probability strictly exceeds the free base attempt's. The exact percentages are
    /// PROVISIONAL (OQ-9 balancing) — only the strict inequality is the AC.
    /// </para>
    /// </summary>
    public sealed class CaptureSystem
    {
        // Capture success probabilities — PROVISIONAL (OQ-9 balancing). Kept as local consts rather than
        // EconomyConfig fields for now, exactly as LureSystem.BasicRareChance/PremiumRareChance are: the AC
        // pins the STRICT inequality (Strong > Base), not the values, and the numbers are pure balancing knobs
        // the OQ-9 pass will tune (and may promote into EconomyConfig then — see the story deferral). The ONE
        // invariant that must hold: Strong > Base, strictly.
        /// <summary>Free base-Capture success probability (provisional, OQ-9).</summary>
        public const double BaseCaptureChance = 0.50;

        /// <summary>Strong-Capture success probability — STRICTLY greater than <see cref="BaseCaptureChance"/>
        /// (AC-2, provisional OQ-9). The earned charge buys these improved odds.</summary>
        public const double StrongCaptureChance = 0.85;

        // Story 4.6 (FR-10) — the Stability Boost ease bonus added to the success chance for the remainder of an
        // encounter in which a StabilityBoost charge was applied. The MAGNITUDE lives on ExtrasSystem (the single
        // extras-tunables home — CR patch: it was duplicated on CaptureSystem AND SlaySystem and could silently
        // diverge). The eased chance is clamped to < 1.0 so a boost never makes a capture a certainty (a
        // degenerate config + an already-high base would otherwise reach/exceed 1.0). The AC pins the BEHAVIOR —
        // an eased chance STRICTLY greater than the un-eased one — NOT the magnitude.

        private readonly IRandom _random;

        public CaptureSystem(IRandom random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>The success probability for the chosen Capture variant (AC-2). Strong &gt; base, strictly.</summary>
        public double SuccessChanceOf(bool strong) => SuccessChanceOf(strong, eased: false);

        /// <summary>
        /// The success probability for the chosen Capture variant, optionally EASED by an active Stability Boost
        /// (Story 4.6, AC-1). <paramref name="eased"/> = true adds <see cref="ExtrasSystem.StabilityBoostEase"/> (clamped just
        /// below 1.0 so a capture is never a certainty), so for ANY variant the eased chance is STRICTLY greater
        /// than the un-eased one (the directly mutation-testable AC — a tuner can never invert it). The base
        /// (un-eased) values are byte-identical to the pre-4.6 behavior.
        /// </summary>
        public double SuccessChanceOf(bool strong, bool eased)
        {
            double chance = strong ? StrongCaptureChance : BaseCaptureChance;
            if (!eased)
            {
                return chance;
            }

            // Strictly greater than the un-eased chance, but capped just under certainty (never 1.0).
            return Math.Min(chance + ExtrasSystem.StabilityBoostEase, 0.999);
        }

        /// <summary>
        /// Roll a Capture attempt: true if it SUCCEEDS (the Monster is captured), false if it MISSES (a free
        /// Retry is offered — AC-3). One draw on <see cref="IRandom.NextDouble"/> against
        /// <see cref="SuccessChanceOf(bool)"/>. <paramref name="strong"/> selects the higher Strong chance.
        /// </summary>
        public bool RollCapture(bool strong) => RollCapture(strong, eased: false);

        /// <summary>
        /// Roll a Capture attempt, optionally EASED by an active Stability Boost (Story 4.6, AC-1). One draw on
        /// <see cref="IRandom.NextDouble"/> against <see cref="SuccessChanceOf(bool, bool)"/> — an eased roll
        /// uses the strictly-higher eased chance, so the SAME draw that misses un-eased may succeed eased.
        /// </summary>
        public bool RollCapture(bool strong, bool eased) => _random.NextDouble() < SuccessChanceOf(strong, eased);
    }
}
