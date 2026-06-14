using System;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The plane-anchor placement + coaching decision owner (Story 3.4, FR-4 — the "anchoring + guidance"
    /// home, architecture.md:408). Plain C# (NO <c>MonoBehaviour</c>, no Unity types beyond what the seam
    /// exposes) so the placement/coaching DECISION is headless-tested against a fake
    /// <see cref="IArPlaneAnchorProvider"/>. Mirrors the pure-logic <see cref="ArSessionService"/>/
    /// <see cref="CameraPermissionFlow"/>/<see cref="ArSafetyGate"/> shape.
    /// <para>
    /// <b>Decision #2 — pure-logic decision + thin adapter.</b> The placement/coaching DECISIONS live
    /// here; the OS-touching plane/anchor glue lives in <see cref="IArPlaneAnchorProvider"/>/
    /// <see cref="ArcorePlaneAnchorProvider"/> (no branching beyond the platform <c>#if</c>). This
    /// reconciles "PlaneAnchorService owns anchoring + guidance" with the AR "thin adapter, zero branching
    /// logic" mandate (architecture.md:466) — identical to how 3.3 split <see cref="ArSessionService"/>
    /// from <see cref="ArcoreSession"/>.
    /// </para>
    /// <para>
    /// <b>Decision #3 — "no object is spawned into empty space" (AC-2) is a load-bearing guard.</b>
    /// <see cref="TryPlace"/> checks <see cref="IArPlaneAnchorProvider.HasTrackablePlane"/> FIRST and
    /// refuses to even ASK for a placement pose / create an anchor when no plane is tracked. The pin is
    /// that the provider's pose/anchor methods are NOT called when there is no plane — see
    /// <c>PlaneAnchorServiceTests</c>.
    /// </para>
    /// <para>
    /// <b>NFR-3 — never throws.</b> Every path returns a typed <see cref="PlacementResult"/>; an
    /// anchor-creation failure is <see cref="PlacementOutcome.Failed"/> + a logged warning + guidance, not
    /// an exception.
    /// </para>
    /// <para>
    /// <b>AR-4 — pure-logic, ctor-injected.</b> Only a MonoBehaviour view (<c>ArPlacementView</c>) reads
    /// <c>GameServices</c>; this service receives its seam by constructor.
    /// </para>
    /// </summary>
    public sealed class PlaneAnchorService
    {
        /// <summary>
        /// The AC-2 coaching copy, verbatim from the epic. Held as a named const so tests assert the
        /// PRODUCTION constant rather than a re-typed literal (anti-tautology), and so the (Epic-6)
        /// coaching-banner binder reads one source of truth.
        /// </summary>
        public const string CoachingMessage = "Move your phone slowly across a textured surface";

        private readonly IArPlaneAnchorProvider _provider;

        /// <summary>
        /// Raised when the coaching message changes: a non-null/empty message when entering
        /// <see cref="PlacementOutcome.NeedsCoaching"/>, and <c>null</c> when leaving it (a successful
        /// placement clears the banner). Lets the (Epic-6) AR-HUD coaching banner bind without polling
        /// (the event-driven UI convention, architecture.md:514 — a binder subscribing must unsubscribe
        /// symmetrically in OnDisable). Story 3.4 RAISES it; its consumer is Epic 6.
        /// </summary>
        public event Action<string> OnCoachingChanged;

        // The last coaching message published via OnCoachingChanged, so the event only fires on an actual
        // change (entering coaching from placed, or vice-versa), not on every repeated coaching frame.
        private string _lastCoaching;

        public PlaneAnchorService(IArPlaneAnchorProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// The AC-1 / AC-2 placement decision:
        /// <list type="bullet">
        /// <item>No trackable plane → <see cref="PlacementResult.NeedsCoaching"/> (and the provider's
        /// pose/anchor methods are NOT called — AC-2 "no object spawned into empty space").</item>
        /// <item>Plane tracked but no placement pose in front of the player right now →
        /// <see cref="PlacementResult.NeedsCoaching"/> (still no anchor creation).</item>
        /// <item>Pose found but anchor creation fails → <see cref="PlacementResult.Failed"/> + warn
        /// (guidance, no crash — NFR-3).</item>
        /// <item>Pose found + anchor created → <see cref="PlacementResult.Placed"/> carrying the
        /// <c>AnchorToken</c> (AC-1).</item>
        /// </list>
        /// Never throws (NFR-3).
        /// </summary>
        public PlacementResult TryPlace()
        {
            // AC-2 load-bearing guard: with NO plane, refuse to even ask for a pose or create an anchor.
            // No object is spawned into empty space. (Mutation-test: drop this guard → TryGetPlacementPose
            // is called with no plane → red.)
            if (!_provider.HasTrackablePlane)
            {
                return Coach();
            }

            if (!_provider.TryGetPlacementPose(out var pose, out var planeId))
            {
                // A plane is tracked, but the player is not aiming at a placeable surface right now —
                // still coach, still no empty-space spawn.
                return Coach();
            }

            if (!_provider.TryCreateAnchor(in pose, planeId, out var token))
            {
                GameLog.Warn(
                    "PlaneAnchorService.TryPlace: a placement pose was found but anchor creation failed " +
                    "— degrading to guidance (NFR-3). No object placed.");
                // Reconcile the coaching banner: Failed is a guidance state, NOT a coaching state. If a
                // prior coaching frame left the "Move your phone..." banner up, clear it now (the same way
                // the Placed path does) so a stale coaching banner does not linger over a Failed outcome —
                // the Epic-6 binder shows failure guidance for Failed, not the coaching copy.
                PublishCoaching(null);
                return PlacementResult.Failed();
            }

            // Placed: clear any standing coaching banner.
            PublishCoaching(null);
            return PlacementResult.Placed(token);
        }

        private PlacementResult Coach()
        {
            PublishCoaching(CoachingMessage);
            return PlacementResult.NeedsCoaching(CoachingMessage);
        }

        // Fire OnCoachingChanged only when the message actually changes (entering/leaving coaching),
        // so repeated coaching calls don't spam subscribers. A throwing subscriber cannot corrupt the
        // service (its state is already settled) and must not crash the AR flow (NFR-3).
        private void PublishCoaching(string message)
        {
            if (string.Equals(_lastCoaching, message, StringComparison.Ordinal))
            {
                return;
            }

            _lastCoaching = message;

            try
            {
                OnCoachingChanged?.Invoke(message);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    "PlaneAnchorService.OnCoachingChanged: a subscriber threw — swallowed to keep the " +
                    $"placement flow crash-free (NFR-3). {ex}");
            }
        }
    }
}
