using System.Runtime.CompilerServices;
using UnityEngine;

// The EditMode test assembly sets the private serialized fields through the
// internal SetForTests seam below. The Economy.Tests asmdef already references
// Veilwalkers.Economy, so this attribute is all that is required — no asmdef edit.
[assembly: InternalsVisibleTo("Veilwalkers.Economy.Tests")]

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The single data-driven home for every economy number (AR-16): action Credit
    /// costs, the XP-per-action amounts, the level/charge curve, and the ad/telemetry
    /// caps. A <see cref="ScriptableObject"/> so a designer tunes it in the Inspector
    /// and the change takes effect on next run with NO recompile of service logic —
    /// the numbers live as serialized data here, never as constants in services.
    /// <para>
    /// <b>Static data only.</b> An <see cref="EconomyConfig"/> is configuration, never
    /// runtime/player state (architecture "ScriptableObjects hold static data only").
    /// Fields are private <c>[SerializeField]</c> exposed through read-only accessors;
    /// nothing here is mutated at runtime.
    /// </para>
    /// <para>
    /// <b>OQ-9 — PROVISIONAL VALUES.</b> Only the four action Credit costs
    /// (Basic 1 / Premium 4 / Multi 5 / Slay 3) are canon. Every other number — XP
    /// amounts, level thresholds, per-level-up grants, ad/telemetry caps — is a
    /// placeholder carried from Story 1.5's Bootstrap literal, pending the OQ-9
    /// balancing pass. The PRD's original economy table is superseded by this asset.
    /// </para>
    /// <para>
    /// <b>Progression seam (Story 1.5 → 1.6).</b> <see cref="BuildProgressionRules"/>
    /// constructs the immutable <see cref="ProgressionRules"/> that
    /// <see cref="ProgressionService"/> consumes. The rules stay a plain injected type
    /// (not an SO); this asset is the tunable source Bootstrap reads to build them.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "Veilwalkers/Economy Config")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Header("Action Credit costs (CANON)")]
        [SerializeField] private int _basicLureCost = 1;
        [SerializeField] private int _premiumLureCost = 4;
        [SerializeField] private int _multiLureCost = 5;
        [SerializeField] private int _slayCost = 3;

        [Header("XP per action (PROVISIONAL — OQ-9)")]
        [SerializeField] private int _xpPerCapture = 10;
        [SerializeField] private int _xpPerSlay = 25;

        [Header("Progression curve (PROVISIONAL — OQ-9)")]
        [SerializeField] private int[] _levelXpThresholds = { 100, 250, 450, 700, 1000 };
        [SerializeField] private int _strongCapturePerLevelUp = 1;
        [SerializeField] private int _stabilityBoostPerLevelUp = 1;
        [SerializeField] private int _nightveilFilterPerLevelUp = 1;

        [Header("Daily reward (PROVISIONAL — OQ-9)")]
        [SerializeField] private int _dailyRewardCredits = 5;

        [Header("Ad / telemetry caps (fields only — Story 1.9 owns the logic)")]
        [SerializeField] private int _adDailyCap = 3;
        [SerializeField] private int _telemetryRetentionDays = 30;

        /// <summary>Basic Lure cost in Credits (canon: 1).</summary>
        public int BasicLureCost => _basicLureCost;

        /// <summary>Premium Lure cost in Credits (canon: 4).</summary>
        public int PremiumLureCost => _premiumLureCost;

        /// <summary>Multi-Lure cost in Credits (canon: 5).</summary>
        public int MultiLureCost => _multiLureCost;

        /// <summary>Slay cost in Credits (canon: 3).</summary>
        public int SlayCost => _slayCost;

        /// <summary>
        /// XP granted by a successful Capture. Provisional; MUST stay below
        /// <see cref="XpPerSlay"/> (FR-9: Slay earns more XP than Capture).
        /// </summary>
        public int XpPerCapture => _xpPerCapture;

        /// <summary>
        /// XP granted by a successful Slay. Provisional; MUST exceed
        /// <see cref="XpPerCapture"/> (FR-9).
        /// </summary>
        public int XpPerSlay => _xpPerSlay;

        /// <summary>
        /// Per-level-up Strong Capture charge grant (consumed by
        /// <see cref="BuildProgressionRules"/>). Provisional.
        /// </summary>
        public int StrongCapturePerLevelUp => _strongCapturePerLevelUp;

        /// <summary>Per-level-up Stability Boost charge grant. Provisional.</summary>
        public int StabilityBoostPerLevelUp => _stabilityBoostPerLevelUp;

        /// <summary>Per-level-up Nightveil Filter charge grant. Provisional.</summary>
        public int NightveilFilterPerLevelUp => _nightveilFilterPerLevelUp;

        /// <summary>
        /// Credits granted by the once-per-UTC-day daily login reward (Story 1.8,
        /// <c>DailyRewardService</c>). Provisional (OQ-9) — unlike the fixed 20-credit
        /// first-launch grant (a one-time onboarding constant kept OUT of this asset),
        /// the daily reward IS a balancing knob, so it lives here (AR-16). Must be
        /// positive: <c>DailyRewardService</c> treats a non-positive value as a
        /// misconfiguration and refuses to grant it.
        /// </summary>
        public int DailyRewardCredits => _dailyRewardCredits;

        /// <summary>
        /// Maximum ad-reward grants per calendar day. Field only — Story 1.9's AdHook
        /// reads it; this story never enforces it.
        /// </summary>
        public int AdDailyCap => _adDailyCap;

        /// <summary>
        /// How many days the local telemetry ring retains entries. Field only —
        /// Story 1.9's telemetry sink reads it; this story never enforces it.
        /// </summary>
        public int TelemetryRetentionDays => _telemetryRetentionDays;

        /// <summary>
        /// Build the immutable <see cref="ProgressionRules"/> from the configured curve
        /// — the seam Story 1.5 reserved. Deliberately does NOT re-validate: the
        /// <see cref="ProgressionRules"/> constructor is the single validation gate
        /// (non-null/non-empty/positive/strictly-increasing thresholds, non-negative
        /// grants), so a misconfigured asset throws there with a precise message.
        /// </summary>
        public ProgressionRules BuildProgressionRules() =>
            new ProgressionRules(
                _levelXpThresholds,
                _strongCapturePerLevelUp,
                _stabilityBoostPerLevelUp,
                _nightveilFilterPerLevelUp);

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only ergonomics: warn on the two fastest-to-misconfigure cases — the
        /// FR-9 XP ordering (<c>XpPerSlay &gt; XpPerCapture</c>) and a null/empty
        /// threshold curve — as console messages when a designer edits the asset. This
        /// is a partial aid, NOT the validation gate, and deliberately does not
        /// re-implement the full <see cref="ProgressionRules"/> contract: positive,
        /// strictly-increasing thresholds and non-negative grants are enforced
        /// authoritatively by the <see cref="ProgressionRules"/> constructor invoked
        /// from <see cref="BuildProgressionRules"/> (single validation source —
        /// duplicating those checks here would diverge over time). A curve that is
        /// non-positive, non-increasing, or has a negative grant therefore passes
        /// <see cref="OnValidate"/> silently but throws at construction — by design.
        /// </summary>
        private void OnValidate()
        {
            if (_xpPerSlay <= _xpPerCapture)
            {
                Debug.LogWarning(
                    $"EconomyConfig: XpPerSlay ({_xpPerSlay}) should exceed XpPerCapture " +
                    $"({_xpPerCapture}) — FR-9 requires Slay to earn more XP than Capture.",
                    this);
            }

            if (_levelXpThresholds == null || _levelXpThresholds.Length == 0)
            {
                Debug.LogWarning(
                    "EconomyConfig: LevelXpThresholds must have at least one entry.", this);
            }

            if (_dailyRewardCredits <= 0)
            {
                Debug.LogWarning(
                    $"EconomyConfig: DailyRewardCredits ({_dailyRewardCredits}) must be " +
                    "positive — DailyRewardService refuses to grant a non-positive daily reward.",
                    this);
            }
        }
#endif

        /// <summary>
        /// Test-only initializer. Lets EditMode tests populate an in-memory instance
        /// (<c>ScriptableObject.CreateInstance&lt;EconomyConfig&gt;()</c>) without a
        /// serialized <c>.asset</c>, mirroring how a designer-authored asset arrives.
        /// Internal (never public): an <see cref="EconomyConfig"/> is static data and
        /// is never mutated by production code.
        /// </summary>
        internal void SetForTests(
            int basicLureCost,
            int premiumLureCost,
            int multiLureCost,
            int slayCost,
            int xpPerCapture,
            int xpPerSlay,
            int[] levelXpThresholds,
            int strongCapturePerLevelUp,
            int stabilityBoostPerLevelUp,
            int nightveilFilterPerLevelUp,
            int dailyRewardCredits,
            int adDailyCap,
            int telemetryRetentionDays)
        {
            _basicLureCost = basicLureCost;
            _premiumLureCost = premiumLureCost;
            _multiLureCost = multiLureCost;
            _slayCost = slayCost;
            _xpPerCapture = xpPerCapture;
            _xpPerSlay = xpPerSlay;
            _levelXpThresholds = levelXpThresholds;
            _strongCapturePerLevelUp = strongCapturePerLevelUp;
            _stabilityBoostPerLevelUp = stabilityBoostPerLevelUp;
            _nightveilFilterPerLevelUp = nightveilFilterPerLevelUp;
            _dailyRewardCredits = dailyRewardCredits;
            _adDailyCap = adDailyCap;
            _telemetryRetentionDays = telemetryRetentionDays;
        }
    }
}
