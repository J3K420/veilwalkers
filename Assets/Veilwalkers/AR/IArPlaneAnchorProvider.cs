using UnityEngine;
using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The platform seam over the ARCore / AR Foundation plane-detection + anchor-creation surface
    /// (Story 3.4, FR-4) — the <b>OS-touching edge</b>. It exists so the placement + coaching DECISIONS
    /// (<see cref="PlaneAnchorService"/>'s "rest on a detected plane, else coach, never spawn into empty
    /// space") are headless-testable: the service is ctor-injected with this interface and a fake drives
    /// it in EditMode, while the production <see cref="ArcorePlaneAnchorProvider"/> is the thin,
    /// untestable adapter over the real subsystem (the same shape as <see cref="IArSession"/>/
    /// <see cref="ArcoreSession"/> in this area, and <see cref="ICameraPermission"/>/
    /// <c>AndroidCameraPermission</c>).
    /// <para>
    /// <b>There is no AR Foundation plane subsystem in EditMode (architecture.md:591-592):</b> the same
    /// rule that justified <see cref="IArSession"/>. This seam is exactly why the placement/coaching
    /// logic can be CI-tested without one.
    /// </para>
    /// <para>
    /// <b>Lives in AR, not Core:</b> this is an Android-platform behavior — like <c>IArAnchorProvider</c>
    /// it belongs in the AR area. Core stays platform-free. The serializable <see cref="AnchorToken"/> it
    /// hands back lives in <c>Core.Contracts</c> (Story 1.2), below AR, so higher-tier snapshots can
    /// store it without depending upward on AR.
    /// </para>
    /// <para>
    /// <b>Thin glue — ZERO branching (architecture.md:466):</b> this seam holds NO placement/coaching
    /// decisions. ALL of them (coach-vs-place, refuse-to-spawn-without-a-plane, degrade-on-failure) live
    /// in <see cref="PlaneAnchorService"/>. This interface is the adapter the "AR contains no branching
    /// logic worth testing — thin adapter" mandate targets.
    /// </para>
    /// <para>
    /// <b>Forward path only — the RESTORE path is Story 3.5.</b> 3.4 builds detect → place → create
    /// anchor. The architecture-named <c>IArAnchorProvider.TryRestoreAnchor(token, out
    /// AnchorRestoreResult)</c> surface (architecture.md:409) + the <c>RelocatedToPlane</c>
    /// frustum-nearest-plane relocation (architecture.md:471-473) are Story 3.5. See the note on
    /// <see cref="TryCreateAnchor"/> about how 3.5 ABSORBS this seam rather than adding a parallel one.
    /// </para>
    /// </summary>
    public interface IArPlaneAnchorProvider
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
        /// <b>Reinvention guard for Story 3.5 (decision #6):</b> the architecture names ONE anchor seam,
        /// <c>IArAnchorProvider</c> (architecture.md:409), owning BOTH save (→ <see cref="AnchorToken"/>)
        /// AND restore. Story 3.5's AC-1 (epics.md:574-576) is the SAVE/create-anchor half — the SAME
        /// native operation as this <see cref="TryCreateAnchor"/>. So Story 3.5 must ABSORB this seam into
        /// <c>IArAnchorProvider</c> (rename + add <c>TryRestoreAnchor</c>, OR treat
        /// <see cref="TryCreateAnchor"/> AS the save half and add only restore) — it must NOT define a
        /// second create-anchor surface. 3.4 leaves this note so 3.5 doesn't duplicate the create path.
        /// </para>
        /// </summary>
        bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token);
    }
}
