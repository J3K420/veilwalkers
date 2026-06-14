using System;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The anchor-RESTORE decision owner (Story 3.5, FR-4 — the mirror of <see cref="PlaneAnchorService"/>'s
    /// forward placement). Plain C# (no <c>MonoBehaviour</c>, no Unity types beyond <see cref="Pose"/>) so
    /// the <see cref="AnchorRestoreResult.Restored"/> / <see cref="AnchorRestoreResult.RelocatedToPlane"/> /
    /// <see cref="AnchorRestoreResult.Failed"/> decision — especially the nearest-in-frustum-else-absolute
    /// relocation (AC-3) — is headless-tested against a fake <see cref="IArAnchorProvider"/>. Mirrors the
    /// pure-logic <see cref="PlaneAnchorService"/>/<see cref="ArSessionService"/> shape.
    /// <para>
    /// <b>Separate from <see cref="PlaneAnchorService"/> (decision #4):</b> forward placement (Story 3.4)
    /// and restore (Story 3.5) are distinct decisions with distinct result types
    /// (<c>PlacementResult</c> vs <see cref="AnchorRestoreResult"/>); both consume the SAME
    /// <see cref="IArAnchorProvider"/> seam. Keeping them separate mirrors the 3.1/3.2/3.3 "one pure-logic
    /// class per decision" pattern.
    /// </para>
    /// <para>
    /// <b>The decision (AC-2/3/4):</b>
    /// <list type="bullet">
    /// <item><b>Invalid token</b> (<c>!token.IsValid</c>, the settled 1.2 deferral): do NOT attempt a
    /// native re-acquire — go straight to the relocation path (a None/corrupt token has no trackable to
    /// re-acquire).</item>
    /// <item><b><see cref="AnchorRestoreResult.Restored"/> (AC-2):</b> the subsystem re-acquired the same
    /// trackable → re-anchor in place.</item>
    /// <item><b><see cref="AnchorRestoreResult.RelocatedToPlane"/> (AC-3):</b> the anchor was lost →
    /// re-place on the nearest IN-FRUSTUM plane; fall back to the absolute-nearest ONLY if nothing is in
    /// view. The object never vanishes.</item>
    /// <item><b><see cref="AnchorRestoreResult.Failed"/> (AC-4):</b> re-acquire failed AND no plane is
    /// available → guidance (a warned, typed result), never a crash, never a vanished object.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>NFR-3 — never throws.</b> Every path returns a typed <see cref="AnchorRestoreResult"/>; a
    /// None/corrupt token or a lost anchor is relocation/guidance, not an exception.
    /// <b>AR-4 — pure-logic, ctor-injected.</b> Only a MonoBehaviour view reads <c>GameServices</c>.
    /// </para>
    /// <para>
    /// <b>The recovery CALL SITE is Epic 4.</b> 3.5 builds this DECISION; the
    /// <c>EncounterStateMachine.Suspended</c> state + the code that CALLS <see cref="TryRestore"/> on
    /// session recovery (after <c>ArSessionService.OnArSessionInterrupted</c>, already raised by Story
    /// 3.3) are Epic 4 (architecture.md:650-652).
    /// </para>
    /// </summary>
    public sealed class AnchorRestoreService
    {
        private readonly IArAnchorProvider _provider;

        public AnchorRestoreService(IArAnchorProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Attempt to restore the anchor named by <paramref name="token"/>, returning the typed outcome
        /// (AC-2/3/4) and the <paramref name="pose"/> the object should occupy: the re-acquired pose for
        /// <see cref="AnchorRestoreResult.Restored"/>, the chosen relocation pose for
        /// <see cref="AnchorRestoreResult.RelocatedToPlane"/>, and the token's last-known pose for
        /// <see cref="AnchorRestoreResult.Failed"/> (the object stays where it was — it never vanishes).
        /// The Epic-4 recovery caller re-anchors the encounter's object to <paramref name="pose"/>. Never
        /// throws (NFR-3).
        /// </summary>
        public AnchorRestoreResult TryRestore(in AnchorToken token, out Pose pose)
        {
            // A valid token → try to re-acquire the SAME trackable first (Restored, AC-2). An invalid /
            // None / corrupt token (the settled 1.2 deferral) has no trackable to re-acquire, so skip
            // straight to relocation — never call the native re-acquire on junk (and never crash, NFR-3).
            if (token.IsValid && _provider.TryReacquireAnchor(in token, out Pose reacquired))
            {
                pose = reacquired;
                return AnchorRestoreResult.Restored;
            }

            // Re-acquire failed (or the token was invalid): the anchor is lost — relocate onto a plane so
            // the object never vanishes (AC-3). Prefer the nearest IN-FRUSTUM candidate; fall back to the
            // absolute-nearest only if nothing is in view. The chosen pose is what the device adapter /
            // Epic-4 caller re-anchors the object to.
            if (_provider.TryGetRelocationCandidates(out PlaneCandidate[] candidates)
                && candidates != null
                && candidates.Length > 0)
            {
                pose = SelectRelocationTarget(candidates).Pose;
                return AnchorRestoreResult.RelocatedToPlane;
            }

            // No re-acquire and no plane to relocate onto → Failed (AC-4): guidance, never a crash, never
            // a vanished object. The object stays at its last-known pose (from the token); the Epic-4
            // EncounterStateMachine.Suspended consumer reads this to pause the encounter + show guidance
            // pending recovery.
            GameLog.Warn(
                "AnchorRestoreService.TryRestore: anchor could not be re-acquired and no plane is available " +
                "to relocate onto — returning Failed (guidance, NFR-3). The object is not lost; the " +
                "encounter suspends pending recovery.");
            pose = new Pose(token.position, token.rotation);
            return AnchorRestoreResult.Failed;
        }

        /// <summary>
        /// The AC-3 selection: the nearest candidate with <see cref="PlaneCandidate.InCameraFrustum"/>;
        /// if NONE are in frustum, the absolute-nearest candidate (by
        /// <see cref="PlaneCandidate.DistanceFromCamera"/>). Caller guarantees a non-empty array.
        /// <para>
        /// <b>NaN/Infinity-safe (CR):</b> a candidate with a non-finite <see cref="PlaneCandidate.DistanceFromCamera"/>
        /// (a degenerate camera-to-plane projection the device adapter could produce) is SKIPPED for
        /// distance ranking — otherwise a NaN seed would become a sticky "best" no later candidate could
        /// beat (every <c>&lt;</c> against NaN is false). If EVERY candidate is non-finite, fall back to
        /// the first one rather than relocate to a NaN pose (still never throws — NFR-3).
        /// </para>
        /// </summary>
        private static PlaneCandidate SelectRelocationTarget(PlaneCandidate[] candidates)
        {
            bool haveInFrustum = false;
            bool haveOverall = false;
            PlaneCandidate bestInFrustum = default;
            PlaneCandidate bestOverall = default;

            for (int i = 0; i < candidates.Length; i++)
            {
                PlaneCandidate c = candidates[i];

                // Skip non-finite distances (NaN/Infinity) so they can never win the nearest ranking.
                if (!IsFinite(c.DistanceFromCamera))
                {
                    continue;
                }

                if (!haveOverall || c.DistanceFromCamera < bestOverall.DistanceFromCamera)
                {
                    haveOverall = true;
                    bestOverall = c;
                }

                if (c.InCameraFrustum
                    && (!haveInFrustum || c.DistanceFromCamera < bestInFrustum.DistanceFromCamera))
                {
                    haveInFrustum = true;
                    bestInFrustum = c;
                }
            }

            // Every candidate had a non-finite distance — fall back to the first rather than a NaN pose.
            if (!haveOverall)
            {
                return candidates[0];
            }

            // Nearest in-frustum wins; absolute-nearest is the fallback only when nothing is in view.
            return haveInFrustum ? bestInFrustum : bestOverall;
        }

        // Finite = not NaN and not +/-Infinity. Hand-rolled (rather than float.IsFinite) so it compiles on
        // the project's .NET Standard target regardless of BCL version.
        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

