using Veilwalkers.AR;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Maps each EXISTING system-state enum to its on-brand <see cref="StateTreatment"/> (Story 6.5;
    /// UX-DR15). A pure-logic, DEPENDENCY-FREE switch-on-state map (the
    /// <see cref="MaterializationPresenter"/> precedent) — each method takes the state VALUE
    /// (<see cref="ArSessionState"/>, <see cref="PlacementResult"/>, <see cref="AnchorRestoreResult"/>,
    /// <see cref="CameraPermissionState"/>, the Codex discovery flags, <see cref="ArSafetyGateState"/>)
    /// and reads the static <see cref="VeilVoice"/> copy. It RENDERS the owned state into a treatment;
    /// it never re-classifies or re-owns the state. The deferred Epic-6 views construct it directly
    /// (no Bootstrap registration — the <c>MaterializationView</c> precedent) and bind the result to
    /// the actual wipe/coaching/restore/re-grant/flip/overlay render.
    /// <para>
    /// <b>The safety firewall (AC-2).</b> <see cref="ForSafetyGate"/> is the ONLY method that produces a
    /// <see cref="StateTreatmentKind.SafetyWarning"/>, and it sources copy ONLY from
    /// <see cref="VeilVoice.Safety"/>. Every other method produces a non-safety treatment. A diegetic
    /// Veil-voice line can never reach a safety moment, and a plain safety string never reaches a
    /// diegetic moment.
    /// </para>
    /// <para>
    /// <b>NFR-3 graceful.</b> An out-of-range / undefined enum value degrades to
    /// <see cref="StateTreatment.None"/> with a <see cref="GameLog.Warn"/> (the
    /// <c>MaterializationPresenter</c> default-to-calmest + <c>DreadScaleTokens.ForTier</c> graceful
    /// precedent) — never a throw.
    /// </para>
    /// </summary>
    public sealed class StateTreatmentPresenter
    {
        /// <summary>The slow-device cold-start ritual copy (AC-3) — MORE ritual, never a percentage.
        /// A state-treatment ritual string, distinct from the <see cref="VeilVoice"/> system-moment
        /// table (which is about discrete diegetic moments, not the load ritual's coaching line).</summary>
        public const string RitualThickVeil = "The Veil is thick here. Hold steady…";

        /// <summary>The normal cold-start ritual copy (AC-3) — the veil-parting wipe's quiet line.</summary>
        public const string RitualParting = "Parting the Veil…";

        /// <summary>The camera-denied re-grant copy (AC-4) — plain-but-friendly; points the player at
        /// the existing 3.1 Settings round-trip (<see cref="CameraPermissionFlow.RetryFromSettings"/>).
        /// Kept legible (this borders the safety exception), never buried.</summary>
        public const string CameraReGrantCopy =
            "Veilwalkers needs the camera to reveal the Veil. Open Settings to allow it.";

        /// <summary>
        /// Cold-start / AR-warmup treatment (AC-3). <see cref="ArSessionState.Prewarming"/> is the
        /// warmup moment ⇒ the veil-parting wipe ritual. <see cref="ArSessionState.Cold"/> is the resting
        /// / back-out state, NOT a warmup moment, so it gets no ritual here (the wipe is owned by the
        /// cold→prewarm ENTRY transition, which surfaces as <see cref="ArSessionState.Prewarming"/>);
        /// <see cref="ArSessionState.Ready"/>/<see cref="ArSessionState.Running"/>/
        /// <see cref="ArSessionState.Paused"/> get the calm <see cref="StateTreatment.None"/>.
        /// <paramref name="slowDevice"/> selects the "more ritual" fallback copy — never a percentage.
        /// </summary>
        public StateTreatment ForArSession(ArSessionState state, bool slowDevice)
        {
            switch (state)
            {
                case ArSessionState.Prewarming:
                    return StateTreatment.VeilPartingWipe(
                        slowDevice ? RitualThickVeil : RitualParting, slowDevice);
                case ArSessionState.Cold:
                case ArSessionState.Ready:
                case ArSessionState.Running:
                case ArSessionState.Paused:
                    return StateTreatment.None();
                default:
                    GameLog.Warn($"StateTreatmentPresenter.ForArSession: unmapped ArSessionState '{state}' — degrading to None.");
                    return StateTreatment.None();
            }
        }

        /// <summary>
        /// Plane placement treatment (AC-4). <see cref="PlacementOutcome.NeedsCoaching"/> ⇒ friendly
        /// coaching carrying the placement's own <see cref="PlacementResult.Message"/>;
        /// <see cref="PlacementOutcome.Placed"/>/<see cref="PlacementOutcome.Failed"/> ⇒ no coaching.
        /// </summary>
        public StateTreatment ForPlacement(PlacementResult result)
        {
            switch (result.Outcome)
            {
                case PlacementOutcome.NeedsCoaching:
                    return StateTreatment.PlaneCoaching(result.Message);
                case PlacementOutcome.Placed:
                case PlacementOutcome.Failed:
                    return StateTreatment.None();
                default:
                    GameLog.Warn($"StateTreatmentPresenter.ForPlacement: unmapped PlacementOutcome '{result.Outcome}' — degrading to None.");
                    return StateTreatment.None();
            }
        }

        /// <summary>
        /// Anchor-restore treatment (AC-4). <see cref="AnchorRestoreResult.RelocatedToPlane"/> ⇒ the
        /// "Pulled back through the Veil" beat (never vanish). <see cref="AnchorRestoreResult.Restored"/>
        /// (re-anchored in place — silent) and <see cref="AnchorRestoreResult.Failed"/> (guidance,
        /// handled elsewhere — never a crash) ⇒ no beat.
        /// </summary>
        public StateTreatment ForAnchorRestore(AnchorRestoreResult result)
        {
            switch (result)
            {
                case AnchorRestoreResult.RelocatedToPlane:
                    return StateTreatment.AnchorRestoreBeat(VeilVoice.AnchorRestored);
                case AnchorRestoreResult.Restored:
                case AnchorRestoreResult.Failed:
                    return StateTreatment.None();
                default:
                    GameLog.Warn($"StateTreatmentPresenter.ForAnchorRestore: unmapped AnchorRestoreResult '{result}' — degrading to None.");
                    return StateTreatment.None();
            }
        }

        /// <summary>
        /// Camera-permission treatment (AC-4). <see cref="CameraPermissionState.Denied"/> ⇒ the re-grant
        /// path (surfaces the existing 3.1 Settings round-trip). Every other state ⇒ no treatment.
        /// </summary>
        public StateTreatment ForCameraPermission(CameraPermissionState state)
        {
            switch (state)
            {
                case CameraPermissionState.Denied:
                    return StateTreatment.CameraReGrant(CameraReGrantCopy);
                case CameraPermissionState.Disclosure:
                case CameraPermissionState.Requesting:
                case CameraPermissionState.Granted:
                    return StateTreatment.None();
                default:
                    GameLog.Warn($"StateTreatmentPresenter.ForCameraPermission: unmapped CameraPermissionState '{state}' — degrading to None.");
                    return StateTreatment.None();
            }
        }

        /// <summary>
        /// Codex first-discovery treatment (AC-4). A first discovery ⇒ the slot flip + count-tick
        /// treatment; when <paramref name="revealsNewTier"/> is true it carries
        /// <see cref="VeilVoice.NewTier"/> ("The Veil shows you more…", the new-tier silhouette
        /// fade-in signal). A non-first-discovery ⇒ no treatment.
        /// </summary>
        public StateTreatment ForCodexDiscovery(bool isFirstDiscovery, bool revealsNewTier)
        {
            if (!isFirstDiscovery)
            {
                return StateTreatment.None();
            }

            // The flip + count-tick always happens on a first discovery; the new-tier copy is the
            // silhouette-fade-in signal, present ONLY when a tier was newly revealed.
            return StateTreatment.CodexFirstDiscovery(revealsNewTier ? VeilVoice.NewTier : string.Empty);
        }

        /// <summary>
        /// The PLAIN safety-warning treatment (AC-2; the firewall). Maps the
        /// <see cref="ArSafetyGateState"/> blocking screens to plain <see cref="VeilVoice.Safety"/> copy —
        /// the deliberate-read full warning vs the fast card. NON-blocking states
        /// (<see cref="ArSafetyGateState.Inactive"/>/<see cref="ArSafetyGateState.Acknowledged"/>) ⇒ no
        /// treatment. This is the ONLY method that sets <see cref="StateTreatment.IsSafetyException"/>;
        /// it NEVER sources a diegetic line.
        /// </summary>
        public StateTreatment ForSafetyGate(ArSafetyGateState state)
        {
            switch (state)
            {
                case ArSafetyGateState.BlockingFull:
                    return StateTreatment.SafetyWarning(VeilVoice.Safety.ArWarningFull);
                case ArSafetyGateState.BlockingFastCard:
                    return StateTreatment.SafetyWarning(VeilVoice.Safety.ArWarningFast);
                case ArSafetyGateState.Inactive:
                case ArSafetyGateState.Acknowledged:
                    return StateTreatment.None();
                default:
                    GameLog.Warn($"StateTreatmentPresenter.ForSafetyGate: unmapped ArSafetyGateState '{state}' — degrading to None.");
                    return StateTreatment.None();
            }
        }
    }
}
