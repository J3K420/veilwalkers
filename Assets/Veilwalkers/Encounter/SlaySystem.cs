using System;
using Veilwalkers.Core;
using Veilwalkers.Economy;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The Slay success-roll decision + cost accessor (Story 4.5, FR-9; architecture.md:416 names this file as a
    /// sibling of <see cref="LureSystem"/>/<see cref="CaptureSystem"/>). Pure C# (NO MonoBehaviour, NO Unity
    /// types), constructor-injected (AR-4 — NEVER <c>GameServices.Get&lt;T&gt;()</c>). It makes ONLY the
    /// decision: does a Slay attempt SUCCEED? — a draw on <see cref="IRandom.NextDouble"/> against the Slay
    /// success chance (tap-to-slay "light combat", AC-1). It does NOT spend Credits, persist, or stage a
    /// discovery — those are the <see cref="EncounterService"/> composed write (the same altitude split as
    /// <see cref="LureSystem"/>/<see cref="CaptureSystem"/>: the system rolls, the service composes). Free of
    /// side effects, so the roll is trivially headless-testable against a scripted <see cref="IRandom"/>.
    /// <para>
    /// <b>The success roll (AC-1/AC-3).</b> A draw below the chance is a success:
    /// <c>_random.NextDouble() &lt; SlaySuccessChance</c> (the <see cref="CaptureSystem"/> precedent). A MISS
    /// leaves the encounter live for a free Retry (AC-3) and deducts NOTHING (the spend is roll-gated — see
    /// <see cref="EncounterService.TrySlayAsync"/>). The exact percentage is PROVISIONAL (OQ-9 balancing).
    /// </para>
    /// <para>
    /// <b>The cost (AC-1 — "shown before spend").</b> The Slay Credit cost is read from the injected
    /// <see cref="EconomyConfig.SlayCost"/> (canon 3) via <see cref="Cost"/> — the data-driven economy value
    /// (AR-16), not a magic number. The composed write reads it the same way the Capture write reads
    /// <c>XpPerCapture</c>. The XP reward (<see cref="EconomyConfig.XpPerSlay"/>, more than Capture — FR-9) is
    /// read by the service from the same config; <see cref="SlaySystem"/> owns only the roll + the cost.
    /// </para>
    /// </summary>
    public sealed class SlaySystem
    {
        // Slay success probability — PROVISIONAL (OQ-9 balancing). Kept as a local const rather than an
        // EconomyConfig field, exactly as CaptureSystem.BaseCaptureChance/StrongCaptureChance and
        // LureSystem.BasicRareChance/PremiumRareChance are: the AC pins the BEHAVIOR (a failed Slay leaves the
        // encounter live + free Retry; rewards superior to Capture), not the success value, and the number is a
        // pure balancing knob the OQ-9 pass will tune (and may promote into EconomyConfig then — see the story
        // deferral). Tap-to-slay "light combat" (AC-1) → lands between base-Capture (0.50) and Strong (0.85).
        /// <summary>Slay-attempt success probability (provisional, OQ-9).</summary>
        public const double SlaySuccessChance = 0.65;

        // Story 4.6 (FR-10) — the Stability Boost ease bonus added to the Slay success chance for the remainder
        // of an encounter in which a StabilityBoost charge was applied (AC-1). The MAGNITUDE lives on ExtrasSystem
        // (the single extras-tunables home — CR patch: it was duplicated here AND on CaptureSystem and could
        // silently diverge; one Stability Boost must ease Capture and Slay by the SAME amount). Clamped < 1.0 so
        // a Slay is never a certainty. The AC pins the BEHAVIOR — eased STRICTLY greater than un-eased — NOT the magnitude.

        private readonly IRandom _random;
        private readonly EconomyConfig _config;

        public SlaySystem(EconomyConfig config, IRandom random)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>The Slay Credit cost (AC-1, canon 3) — read from <see cref="EconomyConfig.SlayCost"/>, the
        /// data-driven economy value (AR-16). Surfaced on the <see cref="SlayResult"/> so the HUD can show the
        /// cost before the spend.</summary>
        public int Cost => _config.SlayCost;

        /// <summary>The Slay success probability, optionally EASED by an active Stability Boost (Story 4.6,
        /// AC-1). <paramref name="eased"/> = true adds <see cref="ExtrasSystem.StabilityBoostEase"/> (clamped just below
        /// certainty), so the eased chance is STRICTLY greater than the un-eased one (the mutation-testable AC).
        /// The un-eased value is byte-identical to the pre-4.6 behavior.</summary>
        public double SuccessChanceOf(bool eased) =>
            eased ? Math.Min(SlaySuccessChance + ExtrasSystem.StabilityBoostEase, 0.999) : SlaySuccessChance;

        /// <summary>
        /// Roll a Slay attempt: true if it SUCCEEDS (the Monster is slain — the 3-Credit spend + discovery + XP
        /// commit), false if it MISSES (a free Retry is offered, no Credits lost — AC-3). One draw on
        /// <see cref="IRandom.NextDouble"/> against <see cref="SlaySuccessChance"/>.
        /// </summary>
        public bool RollSlay() => RollSlay(eased: false);

        /// <summary>
        /// Roll a Slay attempt, optionally EASED by an active Stability Boost (Story 4.6, AC-1). One draw on
        /// <see cref="IRandom.NextDouble"/> against <see cref="SuccessChanceOf(bool)"/> — an eased roll uses the
        /// strictly-higher eased chance, so the SAME draw that misses un-eased may succeed eased.
        /// </summary>
        public bool RollSlay(bool eased) => _random.NextDouble() < SuccessChanceOf(eased);
    }
}
