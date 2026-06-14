using UnityEngine;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The single architecture-named anchor seam (<c>IArAnchorProvider</c>, architecture.md:409) over the
    /// ARCore / AR Foundation plane-detection + anchor-create + anchor-RESTORE surface — the
    /// <b>OS-touching edge</b>. It owns BOTH the forward path (Story 3.4: detect → place → create anchor)
    /// AND the restore path (Story 3.5: re-acquire / relocate a lost anchor). It exists so the DECISIONS
    /// (<see cref="PlaneAnchorService"/>'s "rest on a plane, else coach, never spawn into empty space";
    /// <see cref="AnchorRestoreService"/>'s "re-anchor / relocate-to-nearest-in-frustum / fail") are
    /// headless-testable: the services are ctor-injected with this interface and a fake drives them in
    /// EditMode, while the production <see cref="ArcoreAnchorProvider"/> is the thin, untestable adapter
    /// over the real subsystem (the same shape as <see cref="IArSession"/>/<see cref="ArcoreSession"/>).
    /// <para>
    /// <b>Absorbed, not duplicated (Story 3.4 decision #6, acted on in 3.5):</b> 3.4 built the forward
    /// members on a <c>IArPlaneAnchorProvider</c> seam and flagged that 3.5 must absorb it into THIS
    /// single architecture-named seam rather than define a second create-anchor surface. 3.5 renamed the
    /// seam and ADDED the restore members; <see cref="TryCreateAnchor"/> IS the "save" half
    /// architecture.md:409 names (there is no separate <c>SaveAnchor</c>).
    /// </para>
    /// <para>
    /// <b>There is no AR Foundation plane/anchor subsystem in EditMode (architecture.md:591-592):</b> the
    /// same rule that justified <see cref="IArSession"/>. This seam is exactly why the placement + restore
    /// DECISIONS can be CI-tested without one.
    /// </para>
    /// <para>
    /// <b>Lives in AR, not Core:</b> an Android-platform behavior. Core stays platform-free; the
    /// serializable <see cref="AnchorToken"/> it hands back lives in <c>Core.Contracts</c> (Story 1.2),
    /// below AR, so higher-tier snapshots can store it without depending upward on AR.
    /// </para>
    /// <para>
    /// <b>Thin glue — ZERO branching (architecture.md:466):</b> this seam holds NO decisions. The
    /// coach-vs-place decision lives in <see cref="PlaneAnchorService"/>; the
    /// Restored-vs-RelocatedToPlane-vs-Failed + nearest-in-frustum decision lives in
    /// <see cref="AnchorRestoreService"/>. This interface is the adapter the "AR contains no branching
    /// logic worth testing — thin adapter" mandate targets — it exposes re-acquire + candidate-plane
    /// primitives, not the restore decision.
    /// </para>
    /// </summary>
    public interface IArAnchorProvider
    {
        /// <summary>
        /// Whether at least one plane is currently detected / tracked — the live OS truth
        /// <see cref="PlaneAnchorService"/> reads to decide coaching (no plane → coach, AC-2) vs.
        /// placement (plane → place, AC-1). Off-device this reports a documented editor default so the
        /// placement logic is reachable in-editor (mirroring <see cref="IArSession.IsSupported"/>).
        /// </summary>
        bool HasTrackablePlane { get; }

        /// <summary>
        /// Ask the subsystem for a placement pose on a detected plane in front of the player (the
        /// forward placement path AC-1 needs). Returns <c>false</c> (out-pose undefined) when no plane
        /// supports a placement right now — even when <see cref="HasTrackablePlane"/> is <c>true</c>, the
        /// player may not be aiming at a placeable surface, and the service must still coach rather than
        /// spawn into empty space (AC-2). <paramref name="pose"/> is a plain <see cref="Pose"/>
        /// (position + rotation, Unity-but-not-AR-Foundation) so the service/fake build poses headlessly;
        /// <paramref name="planeId"/> is the trackable id the resulting <see cref="AnchorToken"/> carries.
        /// </summary>
        bool TryGetPlacementPose(out Pose pose, out string planeId);

        /// <summary>
        /// Create a native anchor at <paramref name="pose"/> on the plane named by
        /// <paramref name="planeId"/> and hand back a serializable <see cref="AnchorToken"/>
        /// (<c>Core.Contracts</c>, Story 1.2 — <c>{ trackableId, position, rotation }</c>). Returns
        /// <c>false</c> (default token) on failure — never throws; <see cref="PlaneAnchorService"/>
        /// degrades to guidance (NFR-3).
        /// <para>
        /// <b>This IS the "save" half (architecture.md:409).</b> The architecture's prose names the seam
        /// <c>SaveAnchor→AnchorToken</c>; <see cref="TryCreateAnchor"/> is that surface (it produces the
        /// serializable <see cref="AnchorToken"/> storable in <c>EncounterSnapshot</c>/<c>SaveModel</c>).
        /// There is no separate <c>SaveAnchor</c> method.
        /// </para>
        /// </summary>
        bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token);

        /// <summary>
        /// Restore path (Story 3.5): ask the subsystem to RE-ACQUIRE the same native trackable named by
        /// <paramref name="token"/> — the <see cref="AnchorRestoreResult.Restored"/> "re-anchor in place"
        /// case (AC-2). Returns <c>true</c> with the re-acquired <paramref name="pose"/> on success;
        /// <c>false</c> (out-pose undefined) when the trackable is lost and cannot be re-acquired (the
        /// caller then relocates via <see cref="TryGetRelocationCandidates"/>). Never throws — a junk /
        /// <c>!IsValid</c> token is the caller's concern (<see cref="AnchorRestoreService"/> does not even
        /// call this for an invalid token); a subsystem failure returns <c>false</c> (NFR-3).
        /// </summary>
        bool TryReacquireAnchor(in AnchorToken token, out Pose pose);

        /// <summary>
        /// Restore path (Story 3.5): hand back the currently-detected planes the lost anchor could
        /// relocate ONTO — the <see cref="AnchorRestoreResult.RelocatedToPlane"/> case (AC-3). Returns
        /// <c>false</c> (empty candidates) when no plane is available (the caller then returns
        /// <see cref="AnchorRestoreResult.Failed"/>). Each <see cref="PlaneCandidate"/> carries a
        /// <see cref="Pose"/>, an <see cref="PlaneCandidate.InCameraFrustum"/> flag, and a
        /// camera-relative <see cref="PlaneCandidate.DistanceFromCamera"/> so the pure-logic
        /// <see cref="AnchorRestoreService"/> picks "nearest in-frustum, else absolute-nearest" headlessly
        /// — the DECISION stays out of this thin seam (architecture.md:466, :471-473).
        /// </summary>
        bool TryGetRelocationCandidates(out PlaneCandidate[] candidates);
    }
}
