using System;
using System.Threading.Tasks;
using Veilwalkers.Economy;
using Veilwalkers.Encounter;

namespace Veilwalkers.App
{
    /// <summary>
    /// Thin App-tier adapters that bind the real <see cref="EncounterService"/> (the
    /// <c>Veilwalkers.Encounter</c> tier) onto the two App-owned ports the
    /// <see cref="AppStateMachine"/> depends on. They exist because the dependency graph is
    /// one-way <c>… ← Encounter ← App</c> (AR-5): <see cref="EncounterService"/> sits BELOW
    /// <c>Veilwalkers.App</c>, so it cannot itself implement an App-tier interface
    /// (<see cref="IEncounterSnapshotPort"/> / <see cref="IInsufficientCreditsSource"/>) —
    /// that would be an illegal Encounter → App edge the acyclic guard rejects. The adapter
    /// lives in App (which legally references Encounter) and forwards; it owns NO encounter
    /// logic, only the binding. This closes the deferred Bootstrap seam recorded in
    /// <c>Bootstrap.cs</c> + the Epic-8 Gate-0 checklist, replacing the
    /// <see cref="IEncounterSnapshotPort.NoEncounter"/> no-op + the null second shortfall
    /// source with the live service.
    /// </summary>
    internal static class EncounterServiceAppAdapters
    {
        /// <summary>
        /// Adapts <see cref="EncounterService"/> onto <see cref="IEncounterSnapshotPort"/>.
        /// <see cref="HasActiveEncounter"/> maps to <c>State == EncounterState.Lured</c> — the
        /// exact condition <see cref="EncounterService.SnapshotActiveEncounter"/> itself gates
        /// on (only a Lured encounter is snapshottable, per the port's own contract doc).
        /// Snapshot/rehydrate forward verbatim; neither re-decides anything.
        /// </summary>
        internal sealed class SnapshotPortAdapter : IEncounterSnapshotPort
        {
            private readonly EncounterService _encounter;

            internal SnapshotPortAdapter(EncounterService encounter)
            {
                _encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            }

            public bool HasActiveEncounter => _encounter.State == EncounterState.Lured;

            public Task<bool> SnapshotActiveEncounter() => _encounter.SnapshotActiveEncounter();

            public Task<bool> RehydrateFromSnapshot() => _encounter.RehydrateFromSnapshot();
        }

        /// <summary>
        /// Adapts <see cref="EncounterService"/>'s <c>OnInsufficientCredits</c> event onto the
        /// App-tier <see cref="IInsufficientCreditsSource"/> — the SECOND shortfall source the
        /// <see cref="AppStateMachine"/> arbiter listens to (composed Lure/Slay spends, AR-11).
        /// A pure event-forwarder: subscribing/unsubscribing on this adapter subscribes/
        /// unsubscribes on the underlying service, so the state machine's symmetric
        /// subscribe-in-ctor / unsubscribe-in-Dispose discipline reaches the real event.
        /// </summary>
        internal sealed class ShortfallAdapter : IInsufficientCreditsSource
        {
            private readonly EncounterService _encounter;

            internal ShortfallAdapter(EncounterService encounter)
            {
                _encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            }

            public event Action<InsufficientCreditsEvent> OnInsufficientCredits
            {
                add => _encounter.OnInsufficientCredits += value;
                remove => _encounter.OnInsufficientCredits -= value;
            }
        }
    }
}
