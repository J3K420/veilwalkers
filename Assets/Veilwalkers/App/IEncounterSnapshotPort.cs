using System.Threading.Tasks;

namespace Veilwalkers.App
{
    /// <summary>
    /// The NARROW seam the <see cref="AppStateMachine"/> needs for the Shop round-trip (Story 6.3;
    /// architecture.md:524-525) — deliberately just three members, NOT the whole
    /// <c>Veilwalkers.Encounter.EncounterService</c>. Three reasons it lives here in
    /// <c>Veilwalkers.App</c> rather than referencing Encounter directly:
    /// <list type="number">
    /// <item><c>Veilwalkers.App.Tests</c> does NOT reference <c>Veilwalkers.Encounter</c> (the
    ///   <see cref="LoadPhase"/> no-AR-ref precedent), so a port defined here is fakeable from the
    ///   App tests deterministically — no real Encounter, no <c>MonsterDatabase.asset</c>.</item>
    /// <item><c>EncounterService</c> is STILL a deferred Bootstrap seam (its <c>MonsterDatabase.asset</c>
    ///   is unauthored — Bootstrap.cs:286-343), so the state machine must tolerate it being absent.
    ///   <see cref="NoEncounter"/> is the graceful no-op the state machine uses when Encounter is
    ///   unregistered (the CodexService-consumer-degrade precedent).</item>
    /// <item>It keeps the state machine's dependency surface honest: 6.3 wires the snapshot CALL SITES
    ///   only; it owns no encounter logic.</item>
    /// </list>
    /// Bootstrap adapts the real <c>EncounterService</c> onto this port once it is registered (a thin
    /// adapter, NOT widening the seam). The activeness predicate the adapter implements is
    /// <c>State == EncounterState.Lured</c> — the exact condition <c>SnapshotActiveEncounter()</c>
    /// itself gates on (only a Lured encounter is snapshottable).
    /// </summary>
    public interface IEncounterSnapshotPort
    {
        /// <summary>True iff there is a live, snapshottable encounter (the adapter maps this to
        /// <c>EncounterState.Lured</c>). When false, <see cref="AppStateMachine.NavigateToShopAsync"/>
        /// (and the shortfall path) skip the snapshot entirely.</summary>
        bool HasActiveEncounter { get; }

        /// <summary>Persist the active encounter before a Shop trip (Story 5.4
        /// <c>EncounterService.SnapshotActiveEncounter</c>). Returns true only when a snapshot was
        /// written; never throws for expected cases.</summary>
        Task<bool> SnapshotActiveEncounter();

        /// <summary>Restore the logical encounter on Shop return (Story 5.4
        /// <c>EncounterService.RehydrateFromSnapshot</c>). Returns true only when an encounter was
        /// rehydrated; never throws. The scene re-anchor render is the AR rig (deferred).</summary>
        Task<bool> RehydrateFromSnapshot();

        /// <summary>
        /// The graceful no-op used when <c>EncounterService</c> is not registered (the deferred
        /// Bootstrap seam): never an active encounter, snapshot/rehydrate are no-op successes-as-false
        /// (nothing to snapshot, nothing to rehydrate). Lets Home/AR navigation work end-to-end while
        /// the Encounter wiring stays blocked on <c>MonsterDatabase.asset</c> (NFR-3 graceful).
        /// </summary>
        public static readonly IEncounterSnapshotPort NoEncounter = new NoEncounterPort();

        private sealed class NoEncounterPort : IEncounterSnapshotPort
        {
            public bool HasActiveEncounter => false;
            public Task<bool> SnapshotActiveEncounter() => Task.FromResult(false);
            public Task<bool> RehydrateFromSnapshot() => Task.FromResult(false);
        }
    }
}
