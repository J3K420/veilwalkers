using System;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App
{
    /// <summary>
    /// First-launch policy (Story 1.7): grant the new player their 20 starting Credits
    /// exactly once per install. The Credit ledger is deliberately dumb
    /// (<see cref="ICreditService.GrantCreditsAsync"/> just grants atomically); the
    /// "when to grant" decision lives here in App, keeping Economy free of onboarding
    /// policy (AR-4/AR-11). Bootstrap runs this once after the save loads.
    /// <para>
    /// Once-per-install is gated by <see cref="SaveModel.StartingCreditsGranted"/>: a
    /// fresh model has it false (grant), a granted/migrated model has it true (skip).
    /// A wiped/reinstalled save is a fresh model again, so the grant repeats — the
    /// local-only MVP behavior (no server grant-once).
    /// </para>
    /// </summary>
    public sealed class FirstLaunchGrant
    {
        /// <summary>
        /// The canon first-launch grant amount. A fixed onboarding constant, NOT an
        /// <c>EconomyConfig</c> balancing knob — the OQ-9 tunable set is costs/XP/curve/
        /// caps, and the starting grant is not among them.
        /// </summary>
        public const int StartingCredits = 20;

        private readonly SaveService _saveService;
        private readonly ICreditService _creditService;
        private readonly SaveMutationLock _mutationLock;

        public FirstLaunchGrant(
            SaveService saveService, ICreditService creditService, SaveMutationLock mutationLock)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _creditService = creditService ?? throw new ArgumentNullException(nameof(creditService));
            _mutationLock = mutationLock ?? throw new ArgumentNullException(nameof(mutationLock));
        }

        /// <summary>
        /// Grant the starting Credits if this install has not been granted yet.
        /// Idempotent: a no-op when the model is unloaded/corrupt (null
        /// <see cref="SaveService.Current"/>) or the marker is already set.
        /// <para>
        /// Two persists, both serialized through the shared <see cref="SaveMutationLock"/>
        /// and both rollback-safe, exactly like every other Economy mutate-and-persist:
        /// the grant (the atomic <see cref="ICreditService.GrantCreditsAsync"/> pipeline)
        /// lands the +20 first, then a SECOND lock-held write sets the marker. Holding the
        /// same lock the credit/progression services use means a concurrent mutator can
        /// never interleave between the two writes (AR-8). Ordering: grant before marker,
        /// so any failure leaves the marker false → the next launch re-grants (a benign
        /// duplicate, the same outcome as a reinstall) rather than credits-granted-but-
        /// marker-lost. Both the grant-persist failure (returned <see cref="Result"/>) and
        /// the marker-persist failure (caught + rolled back below) leave the marker false
        /// and simply retry next launch; neither faults the caller for an expected,
        /// recoverable state.
        /// </para>
        /// </summary>
        public async Task RunAsync()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                // Save not loaded or corrupt: the recovery flow owns that state; never
                // grant onto a null model. The next successful load re-runs this path.
                return;
            }

            if (model.StartingCreditsGranted)
            {
                // Already granted on this install (or migrated from a pre-1.7 save):
                // the once-per-install guarantee.
                return;
            }

            Result grant = await _creditService.GrantCreditsAsync(StartingCredits).ConfigureAwait(false);
            if (!grant.Success)
            {
                // Transient persist failure: do NOT set the marker, so the grant retries
                // on the next launch. Warn (not Error) — a recoverable, expected state.
                GameLog.Warn(
                    $"FirstLaunchGrant: starting-credit grant did not persist ({grant.Message}); " +
                    "will retry next launch.");
                return;
            }

            // The grant landed and persisted. Record the marker in a SECOND write, held
            // under the SAME mutation lock the grant used, with the same rollback + swap
            // guard as GrantCreditsAsync: set in memory → persist → on a persist fault OR
            // a recovery swap (Current no longer the model we mutated), undo the in-memory
            // marker so memory matches disk and the next launch retries.
            await _mutationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SaveModel current = _saveService.Current;
                if (current == null)
                {
                    // Corruption raced the grant: skip the marker write; the next launch
                    // re-grants (benign per the ordering rule).
                    return;
                }

                current.StartingCreditsGranted = true;
                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (!ReferenceEquals(_saveService.Current, current))
                    {
                        // A recovery swap replaced the model mid-persist: what was written
                        // is not the model we marked. Undo the marker on our instance.
                        current.StartingCreditsGranted = false;
                        GameLog.Warn(
                            "FirstLaunchGrant: marker write rolled back — the save model was " +
                            "swapped mid-operation; will retry next launch.");
                        return;
                    }

                    GameLog.Info($"FirstLaunchGrant: granted {StartingCredits} starting Credits.");
                }
                catch (Exception ex)
                {
                    // Marker persist failed: roll the in-memory marker back so memory and
                    // disk agree (both false). The +20 stays granted; the next launch
                    // re-grants — the benign duplicate the ordering rule accepts.
                    current.StartingCreditsGranted = false;
                    GameLog.Warn(
                        $"FirstLaunchGrant: marker did not persist ({ex.Message}); " +
                        "will retry next launch.");
                }
            }
            finally
            {
                _mutationLock.Release();
            }
        }
    }
}
