using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The materialization DECISION for one spawned Monster (Story 4.7) — the data the (Epic-6)
    /// view renders into the actual entrance animation, ambiance, and action-bar gating. A
    /// data-only <c>readonly struct</c> (the <see cref="CodexSlot"/> precedent): handed to the
    /// view, it can never alias or corrupt the presenter's state.
    /// <para>
    /// <b>Logic, not pixels.</b> This carries NO Unity types — no tween, no shader, no
    /// <c>Color</c>. The <see cref="MaterializationPresenter"/> computes it from the Monster's
    /// <see cref="Rarity"/> tier + the reduced-motion setting; Epic 6 maps it to the real
    /// entrance FX, the camera-glitch (for <see cref="BrandedGlitch"/>), the ambiance tokens, and
    /// the action-bar show/hide.
    /// </para>
    /// <para>
    /// <b>NFR-2:</b> <see cref="DurationSeconds"/> is the RENDER duration (how long the entrance
    /// plays). It is INDEPENDENT of the credit spend-ack — the ack is the synchronous
    /// <c>LureResult</c> returned by Story 4.2's <c>TryLureAsync</c> BEFORE any materialization
    /// plays, so the ack confirms sub-second even during a multi-second Breach.
    /// </para>
    /// </summary>
    public readonly struct MaterializationPlan
    {
        // Backing field for the null-safe MonsterId property. A struct's `default` value
        // zero-inits this to null (bypassing the factory); the MonsterId GETTER coalesces it to
        // empty so even a default-valued plan honors the "empty when not supplied" contract — a
        // render that does plan.MonsterId.Length never NREs (CR finding: default plan MonsterId).
        private readonly string _monsterId;

        /// <summary>The materializing Monster's rarity tier (T1 Common … T5 Nightmare).</summary>
        public readonly Rarity Tier;

        /// <summary>
        /// The <c>monNN</c> id of the Monster being materialized (the spawned id from
        /// <c>LureResult</c>). A label for the render — empty when not supplied (the
        /// <c>LureResult</c>/<c>ScanResult</c> empty-coalesce precedent); the tier, not the id,
        /// drives the plan. Null-safe even for <c>default(MaterializationPlan)</c>.
        /// </summary>
        public string MonsterId => _monsterId ?? string.Empty;

        /// <summary>The per-tier entrance variant (Pop-in … Breach).</summary>
        public readonly MaterializationVariant Variant;

        /// <summary>
        /// The render duration in seconds — scales with tier (~0.5s T1 … ~2.5–3s T5). The view's
        /// clock, never the spend-ack's (NFR-2). A nominal mid-band value; the exact timing is
        /// Epic-6/OQ-9 polish.
        /// </summary>
        public readonly float DurationSeconds;

        /// <summary>
        /// Whether this entrance carries the branded camera/UI glitch (UX-DR11) — TRUE only for
        /// the T5 <see cref="MaterializationVariant.Breach"/>, and FALSE for EVERY tier when
        /// <see cref="ReducedMotion"/> is on (glitch→static branded transition, UX-DR17/AC-3).
        /// The glitch is brief and never blocks input recovery (see
        /// <see cref="InputRecoveryBlocked"/>) — it reads as designed FX, never a crash (AC-2).
        /// </summary>
        public readonly bool BrandedGlitch;

        /// <summary>
        /// Whether this is the reduced-motion / photosensitivity-tamed plan (AC-3): no
        /// strobe/shake/glitch, low-motion — but the entrance + ambiance are PRESERVED so the
        /// dread still reads.
        /// </summary>
        public readonly bool ReducedMotion;

        /// <summary>
        /// Always <c>true</c> (UX-DR12): the action bar is HIDDEN during materialization and pops
        /// in only when the Monster settles. A constant PROPERTY (not a ctor-set field) so the
        /// invariant holds even for <c>default(MaterializationPlan)</c> — a view reading the gate
        /// off the plan never accidentally shows the bar (CR finding: default plan invariant).
        /// Exposed so the view never hard-codes the gate — pair with
        /// <see cref="MaterializationPresenter.ShouldShowActionBar"/>.
        /// </summary>
        public bool HideActionBarDuringMaterialization => true;

        /// <summary>
        /// Always <c>false</c> (AC-2): the materialization — even a T5 Breach — NEVER blocks input
        /// recovery. A constant PROPERTY (not a ctor-set field) so the invariant holds even for a
        /// default plan. The presenter is the logic-side guarantee that the FX reads as a designed
        /// brief beat, not a crash; the view must keep input recoverable throughout.
        /// </summary>
        public bool InputRecoveryBlocked => false;

        /// <summary>The ambiance shift toward the active tier (AC-1; a discrete dread scale, not a bar).</summary>
        public readonly MaterializationAmbiance Ambiance;

        // Private ctor — the ONLY way to build a plan is MaterializationPresenter (via the
        // internal Create factory below), which derives Variant/Duration/Ambiance/BrandedGlitch
        // from the tier so they are always mutually consistent. This follows the LureResult
        // private-ctor + factory precedent and closes the CR finding that a future external caller
        // could otherwise construct a contradictory plan (e.g. Tier=Common + Variant=Breach).
        private MaterializationPlan(
            Rarity tier,
            string monsterId,
            MaterializationVariant variant,
            float durationSeconds,
            bool brandedGlitch,
            bool reducedMotion,
            MaterializationAmbiance ambiance)
        {
            Tier = tier;
            _monsterId = monsterId ?? string.Empty;
            Variant = variant;
            DurationSeconds = durationSeconds;
            BrandedGlitch = brandedGlitch;
            ReducedMotion = reducedMotion;
            Ambiance = ambiance;
        }

        /// <summary>
        /// Build a plan. <c>internal</c> so only <see cref="MaterializationPresenter"/> (the sole
        /// authority for the tier→variant/duration/ambiance map) constructs plans — keeping every
        /// plan internally consistent (the <c>LureResult</c> factory precedent). The
        /// <see cref="HideActionBarDuringMaterialization"/> / <see cref="InputRecoveryBlocked"/>
        /// invariants are constant properties, not arguments.
        /// </summary>
        internal static MaterializationPlan Create(
            Rarity tier,
            string monsterId,
            MaterializationVariant variant,
            float durationSeconds,
            bool brandedGlitch,
            bool reducedMotion,
            MaterializationAmbiance ambiance)
        {
            return new MaterializationPlan(
                tier, monsterId, variant, durationSeconds, brandedGlitch, reducedMotion, ambiance);
        }
    }
}
