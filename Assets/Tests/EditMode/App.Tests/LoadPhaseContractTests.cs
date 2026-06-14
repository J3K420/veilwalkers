using System;
using NUnit.Framework;
using Veilwalkers.App;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// The AC-2 staged cold-start CONTRACT (Story 3.3; architecture.md:592-594, AR-14). CI asserts the
    /// staging CONTRACT, not a millisecond budget (there is no <c>ARSession</c> in EditMode). This pins
    /// the <see cref="LoadPhase"/> enum's members + their order — the shape Bootstrap stages against
    /// (essentials sync; AR warmup on the async path, NOT awaited before Home is interactive).
    /// <para>
    /// The companion guard — "<c>ArSessionService</c> construction does NOT warm (no <c>StartAsync</c>
    /// at wire-time)" — lives in <c>AR.Tests/ArSessionServiceTests</c> because <c>App.Tests</c> does not
    /// reference <c>Veilwalkers.AR</c> (and must not, per the story's no-asmdef-change rule). Together
    /// they cover AC-CS-2: the enum-shape here + the does-not-warm structural guard there.
    /// </para>
    /// </summary>
    public sealed class LoadPhaseContractTests
    {
        [Test]
        public void LoadPhase_has_exactly_the_three_staging_members()
        {
            string[] names = Enum.GetNames(typeof(LoadPhase));

            CollectionAssert.AreEquivalent(
                new[] { "EssentialSync", "WarmupAsync", "Ready" },
                names,
                "LoadPhase must declare exactly { EssentialSync, WarmupAsync, Ready } (architecture.md:592-594).");
        }

        [Test]
        public void LoadPhase_members_are_in_staging_order_essentials_then_warmup_then_ready()
        {
            // The order encodes the staging sequence: essentials wire synchronously, THEN AR warmup runs
            // on the async path, and only THEN is Home Ready/interactive (warmup not awaited before Ready).
            Assert.Less((int)LoadPhase.EssentialSync, (int)LoadPhase.WarmupAsync,
                "EssentialSync must precede WarmupAsync.");
            Assert.Less((int)LoadPhase.WarmupAsync, (int)LoadPhase.Ready,
                "WarmupAsync must precede Ready (AR warmup is staged before Home-interactive, on the async path).");
        }
    }
}
