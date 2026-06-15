using System;
using Veilwalkers.Economy;

namespace Veilwalkers.Encounter
{
    /// <summary>
    /// The extras decision/charge-mapping accessor (Story 4.6, FR-10; architecture.md:416 names this file as a
    /// sibling of <see cref="LureSystem"/>/<see cref="CaptureSystem"/>/<see cref="SlaySystem"/>). Pure C# (NO
    /// MonoBehaviour, NO Unity types).
    /// <para>
    /// <b>Why this is a STATIC helper (not an injected instance — Decision A').</b> The three other systems own
    /// their <i>decision</i> — a probability ROLL (Lure rarity, Capture/Slay success) — so they hold an injected
    /// <see cref="Veilwalkers.Core.IRandom"/> and are instances. Applying an extra has NO roll: it is a
    /// deterministic <see cref="ExtraKind"/> → <see cref="ChargeType"/> mapping. So <see cref="ExtrasSystem"/> is
    /// state-free and dependency-free, and is a STATIC mapping helper exactly like
    /// <see cref="ChargeInventory"/> (the single charge-field mapping point) — which means it needs NO ctor
    /// injection and adds NO <see cref="EncounterService"/> ctor parameter / harness churn. (The lower-friction
    /// option the story sanctions; the alternative injected-instance form bought nothing here — there is no
    /// randomness/config to inject and nothing to mock.)
    /// </para>
    /// <para>
    /// <b>It DELEGATES the charge mapping — never duplicates it.</b> The <see cref="ExtraKind"/> → backing
    /// <c>SaveModel</c> field path goes through <see cref="ChargeInventory.GetCount"/>/<see cref="ChargeInventory.SetCount"/>
    /// (the ONE charge-field mapping point) via the <see cref="ChargeType"/> this returns — <see cref="ExtrasSystem"/>
    /// never touches the three charge fields directly (a duplicated switch is the defect <see cref="ChargeInventory"/>
    /// exists to prevent).
    /// </para>
    /// <para>
    /// The ease-bonus / rarity-boost MAGNITUDES are NOT owned here — they are provisional OQ-9 balancing knobs
    /// kept as local <c>const</c>s on the roll systems (<see cref="CaptureSystem"/>/<see cref="SlaySystem"/>/
    /// <see cref="LureSystem"/>), the 4.2/4.4/4.5 "the AC is the behavior, not the value" precedent.
    /// </para>
    /// </summary>
    public static class ExtrasSystem
    {
        // ---- The extras' effect MAGNITUDES — the SINGLE home (Story 4.6 CR patch) ----
        // PROVISIONAL (OQ-9 balancing). These were previously duplicated as same-named consts on the roll
        // systems (StabilityBoostEase on BOTH CaptureSystem and SlaySystem), which could silently diverge: a
        // balancing change to one would not change the other, and the "strictly greater" tests pin only the
        // BEHAVIOR (eased > un-eased), not the magnitude, so the drift would be invisible. Centralizing them
        // here — beside the ExtraKind→ChargeType mapping this class already owns — gives ONE place the OQ-9 pass
        // tunes (the ChargeInventory "single mapping point" discipline, applied to the magnitudes). The roll
        // systems reference these; they keep their own SuccessChanceOf/RareChanceOf clamping (< 1.0) since the
        // clamp ceiling is a per-roll concern, not a shared magnitude.
        /// <summary>Stability Boost additive ease bonus to the Capture/Slay success chance (provisional, OQ-9).
        /// One Stability Boost applies the SAME ease to both rolls — hence one shared const, not two.</summary>
        public const double StabilityBoostEase = 0.10;

        /// <summary>Nightveil Filter additive rarity boost to the Lure rare-tier gate (provisional, OQ-9).</summary>
        public const double NightveilRarityBoost = 0.20;

        /// <summary>
        /// The <see cref="ChargeType"/> an <see cref="ExtraKind"/> consumes (the tiny pure mapping — the apply
        /// path feeds this into <see cref="ChargeInventory"/>). An undefined enum value throws
        /// <see cref="ArgumentOutOfRangeException"/> (a programmer error — the only valid inputs are the defined
        /// members; <c>ChargeType.StrongCapture</c> is intentionally NOT reachable here — it has no
        /// <see cref="ExtraKind"/> member, being the 4.4 Capture path's charge).
        /// </summary>
        public static ChargeType ChargeTypeOf(ExtraKind kind)
        {
            switch (kind)
            {
                case ExtraKind.StabilityBoost:
                    return ChargeType.StabilityBoost;
                case ExtraKind.NightveilFilter:
                    return ChargeType.NightveilFilter;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "Unknown ExtraKind.");
            }
        }
    }
}
