namespace Veilwalkers.App
{
    /// <summary>
    /// How AR Hunt is being entered (Story 6.3; architecture.md:482-486). The distinction is
    /// load-bearing: "AR entry" that triggers the mandatory FR-3 safety warning means COLD-entry
    /// into AR mode only. Resuming AR after a Shop round-trip does NOT re-fire the full warning —
    /// otherwise the warning and the re-anchoring beat stack into two interruptions for one monster.
    /// </summary>
    public enum ArEntryKind
    {
        /// <summary>A fresh entry into AR Hunt from Home. Requires onboarding complete (Story 6.4
        /// gate) and fires the FR-3 safety warning (Story 3.2, already built — 6.3 must not suppress
        /// it on cold entry). No encounter is rehydrated.</summary>
        ColdEntry,

        /// <summary>Re-entry into AR Hunt after a Shop round-trip (UJ-2). Does NOT re-fire the safety
        /// warning and rehydrates the snapshotted encounter (architecture.md:483-484, 524-525). The
        /// re-anchor scene render is the AR rig (deferred); 6.3 owns the logical rehydrate call.</summary>
        ShopResume,
    }
}
