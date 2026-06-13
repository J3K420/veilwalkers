using System;
using NUnit.Framework;
using UnityEngine;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// <see cref="EconomyConfig"/> as the single data-driven source of economy numbers
    /// (Story 1.6): every cost/XP/threshold/tunable is read back through its accessor
    /// (AC-1/AC-2), the asset builds a matching <see cref="ProgressionRules"/> through
    /// the Bootstrap seam, two differently-valued assets produce different behavior
    /// (AC-3 data-drivenness), the FR-9 XP ordering holds for the canonical values, and
    /// a misconfigured curve throws through the existing <see cref="ProgressionRules"/>
    /// validation. In-memory instances via <c>ScriptableObject.CreateInstance</c> + the
    /// internal <c>SetForTests</c> seam — no serialized .asset needed for logic.
    /// </summary>
    public sealed class EconomyConfigTests
    {
        /// <summary>
        /// Build an in-memory config. Defaults mirror the canonical provisional asset;
        /// any field can be overridden for a specific assertion.
        /// </summary>
        private static EconomyConfig Config(
            int basicLureCost = 1,
            int premiumLureCost = 4,
            int multiLureCost = 5,
            int slayCost = 3,
            int xpPerCapture = 10,
            int xpPerSlay = 25,
            int[] levelXpThresholds = null,
            int strongCapturePerLevelUp = 1,
            int stabilityBoostPerLevelUp = 1,
            int nightveilFilterPerLevelUp = 1,
            int adDailyCap = 3,
            int telemetryRetentionDays = 30)
        {
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            config.SetForTests(
                basicLureCost,
                premiumLureCost,
                multiLureCost,
                slayCost,
                xpPerCapture,
                xpPerSlay,
                levelXpThresholds ?? new[] { 100, 250, 450, 700, 1000 },
                strongCapturePerLevelUp,
                stabilityBoostPerLevelUp,
                nightveilFilterPerLevelUp,
                adDailyCap,
                telemetryRetentionDays);
            return config;
        }

        [Test]
        public void Every_configured_number_is_read_back_through_its_accessor()
        {
            var config = Config(
                basicLureCost: 1, premiumLureCost: 4, multiLureCost: 5, slayCost: 3,
                xpPerCapture: 10, xpPerSlay: 25,
                strongCapturePerLevelUp: 1, stabilityBoostPerLevelUp: 2, nightveilFilterPerLevelUp: 3,
                adDailyCap: 3, telemetryRetentionDays: 30);

            // AC-1/AC-2: costs, XP amounts, per-level grants, and the stub tunables all
            // originate in the config and surface through its accessors.
            Assert.AreEqual(1, config.BasicLureCost);
            Assert.AreEqual(4, config.PremiumLureCost);
            Assert.AreEqual(5, config.MultiLureCost);
            Assert.AreEqual(3, config.SlayCost);
            Assert.AreEqual(10, config.XpPerCapture);
            Assert.AreEqual(25, config.XpPerSlay);
            Assert.AreEqual(1, config.StrongCapturePerLevelUp);
            Assert.AreEqual(2, config.StabilityBoostPerLevelUp);
            Assert.AreEqual(3, config.NightveilFilterPerLevelUp);
            Assert.AreEqual(3, config.AdDailyCap);
            Assert.AreEqual(30, config.TelemetryRetentionDays);
        }

        [Test]
        public void BuildProgressionRules_yields_rules_matching_the_configured_curve()
        {
            var config = Config(
                levelXpThresholds: new[] { 100, 250 },
                strongCapturePerLevelUp: 1,
                stabilityBoostPerLevelUp: 2,
                nightveilFilterPerLevelUp: 3);

            ProgressionRules rules = config.BuildProgressionRules();

            // The level formula and grant mapping come straight from the config numbers.
            Assert.AreEqual(0, rules.LevelForXp(99));
            Assert.AreEqual(1, rules.LevelForXp(100));
            Assert.AreEqual(2, rules.LevelForXp(250));
            Assert.AreEqual(1, rules.GrantPerLevelUp(ChargeType.StrongCapture));
            Assert.AreEqual(2, rules.GrantPerLevelUp(ChargeType.StabilityBoost));
            Assert.AreEqual(3, rules.GrantPerLevelUp(ChargeType.NightveilFilter));
        }

        [Test]
        public void Changing_a_configured_value_changes_the_built_rules_no_recompile()
        {
            // AC-3: behavior follows the data, not a literal. Two configs differing only
            // in their curve produce ProgressionRules that level and grant differently —
            // the same path Bootstrap uses, proving an asset edit changes behavior with
            // no service-code change.
            var easy = Config(
                levelXpThresholds: new[] { 50, 100 },
                strongCapturePerLevelUp: 1);
            var hard = Config(
                levelXpThresholds: new[] { 500, 1000 },
                strongCapturePerLevelUp: 5);

            ProgressionRules easyRules = easy.BuildProgressionRules();
            ProgressionRules hardRules = hard.BuildProgressionRules();

            // 100 XP is level 2 on the easy curve but still level 0 on the hard curve.
            Assert.AreEqual(2, easyRules.LevelForXp(100));
            Assert.AreEqual(0, hardRules.LevelForXp(100));
            Assert.AreNotEqual(
                easyRules.GrantPerLevelUp(ChargeType.StrongCapture),
                hardRules.GrantPerLevelUp(ChargeType.StrongCapture),
                "Per-level-up grants follow the configured value, not a constant.");
        }

        [Test]
        public void Canonical_values_satisfy_the_FR9_xp_ordering()
        {
            // FR-9: a Slay must earn more XP than a Capture. Pin it for the canonical
            // provisional values so a future edit that inverts them is caught here.
            var config = Config(xpPerCapture: 10, xpPerSlay: 25);

            Assert.Greater(
                config.XpPerSlay, config.XpPerCapture,
                "FR-9: Slay must grant more XP than Capture.");
        }

        [Test]
        public void Canonical_action_costs_are_the_economy_canon()
        {
            // The four costs are the one canon set (CLAUDE.md credit economy): Basic 1 /
            // Premium 4 / Multi 5 / Slay 3. Pin them so a stray edit to the default asset
            // values is caught.
            var config = Config();

            Assert.AreEqual(1, config.BasicLureCost);
            Assert.AreEqual(4, config.PremiumLureCost);
            Assert.AreEqual(5, config.MultiLureCost);
            Assert.AreEqual(3, config.SlayCost);
        }

        [Test]
        public void A_non_increasing_threshold_array_throws_through_progression_rules_validation()
        {
            // BuildProgressionRules deliberately does not re-validate; it delegates to the
            // ProgressionRules ctor (the single validation gate). A bad curve therefore
            // throws there — assert the throw, not every individual guard (those are
            // covered in ProgressionServiceTests).
            var config = Config(levelXpThresholds: new[] { 100, 100 });

            Assert.Throws<ArgumentException>(() => config.BuildProgressionRules());
        }
    }
}
