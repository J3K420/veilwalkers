using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Monsters
{
    /// <summary>
    /// Read model over owned-Monster state (<see cref="SaveModel.Codex"/>) + the catalog
    /// (<see cref="MonsterDatabase"/>). Owns the single atomic Codex-discovery write
    /// (<see cref="RecordDiscoveryAsync"/>). Read model only — it NEVER mutates
    /// credits/XP/charges.
    /// <para>
    /// <b>Lock ownership (decision-of-record, Story 2.3).</b> This service holds its OWN
    /// private <see cref="SemaphoreSlim"/>, NOT the Economy <c>SaveMutationLock</c>. That
    /// lock lives in the <c>Veilwalkers.Economy</c> assembly; <c>CodexService</c> is in the
    /// sibling <c>Veilwalkers.Monsters</c> assembly (AR-5), so injecting it would force an
    /// illegal <c>Monsters → Economy</c> sideways edge that the acyclic guard rejects. A
    /// private lock is correct, not a regression: the shared lock's job — stop two DIFFERENT
    /// services durably persisting each other's uncommitted delta on the same SaveModel —
    /// does not apply here, because CodexService is the LONE codex writer and no single
    /// action mutates both economy state AND codex until Epic 4's Capture/Slay. That
    /// composed multi-delta write lives in <c>Veilwalkers.Encounter</c> (which legally
    /// references BOTH Economy and Monsters) and serializes under the shared Economy lock
    /// THERE — the composed-action lock belongs to Encounter, not to CodexService. AR-8
    /// ("one atomic write per action") is fully honored by this service's self-contained
    /// locked single-<c>SaveAsync</c> write. Gameplay is single-threaded, so a 2.3 codex
    /// write and a concurrent Economy write cannot actually interleave today.
    /// </para>
    /// <para>
    /// Pipeline (mirrors <c>DailyRewardService</c>): validate id → acquire the private gate →
    /// idempotency short-circuit → capture rollback state → mutate in memory → ONE
    /// <c>SaveAsync</c> → recovery-swap guard → rollback on fault → release the lock → raise
    /// events. Events fire AFTER the lock releases and ONLY on a committed first discovery, so
    /// a subscriber that re-enters a read method cannot deadlock and a rolled-back write never
    /// raises.
    /// </para>
    /// </summary>
    public sealed class CodexService
    {
        private readonly SaveService _saveService;
        private readonly MonsterDatabase _database;

        // CodexService's OWN lock — NOT the Economy SaveMutationLock (see class doc). A bare
        // SemaphoreSlim(1,1); the Economy lock is itself only such a wrapper.
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>Raised after a committed FIRST discovery, with the discovered
        /// <c>monNN</c> id. Never raised on a re-discovery, a flag-flip, or a rolled-back
        /// write. Fired after the lock releases (AR-6).</summary>
        public event Action<string> OnMonsterDiscovered;

        /// <summary>Raised exactly once, when the discovery that just committed brought the
        /// collection to <see cref="UniverseCount"/> (67/67). Never on re-discovery after
        /// completion. Fired after <see cref="OnMonsterDiscovered"/>.</summary>
        public event Action OnCodexCompleted;

        public CodexService(SaveService saveService, MonsterDatabase database)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>The number of discovered Monsters — the "X" in "X / 67". A key's
        /// presence in <see cref="SaveModel.Codex"/> means discovered.</summary>
        public int DiscoveredCount => RequireModel().Codex.Count;

        /// <summary>The size of the Monster universe — the "/ 67". Sourced from the catalog
        /// so the lore constant has one home; never hardcoded.</summary>
        public int UniverseCount => MonsterDatabase.UniverseCount;

        /// <summary>True when <paramref name="id"/> has a Codex entry (is discovered).</summary>
        public bool IsDiscovered(string id) => RequireModel().Codex.ContainsKey(id);

        /// <summary>
        /// A read-only, defensively-copied view of <paramref name="id"/>'s entry, or the
        /// not-discovered sentinel when the id has no entry. The returned view never aliases
        /// the persisted entry (its variant flags are a fresh copy) — reads cannot corrupt
        /// persisted state (the 2.2 <c>Populated</c> CR lesson).
        /// </summary>
        public CodexEntryView GetEntry(string id)
        {
            SaveModel model = RequireModel();
            if (id != null && model.Codex.TryGetValue(id, out CodexEntryData data) && data != null)
            {
                return CodexEntryView.From(id, data);
            }

            return CodexEntryView.NotDiscovered(id);
        }

        /// <summary>
        /// The discovered ids, as a fresh read-only snapshot (never the live
        /// <c>Dictionary.Keys</c> bound to internal state). Forward-leaning convenience for
        /// the grid view (2.4).
        /// </summary>
        public IReadOnlyCollection<string> DiscoveredIds => new List<string>(RequireModel().Codex.Keys);

        /// <summary>
        /// Record that <paramref name="id"/> was discovered <paramref name="via"/> Capture or
        /// Slay. The single Codex write seam (Epic 4's Capture &amp; Slay both funnel through
        /// here). Sets ONLY the flag matching <paramref name="via"/>; never implies the other
        /// flag. Idempotent on re-discovery (no count increment, no event); a newly-flipped
        /// flag on an already-discovered Monster persists but does NOT re-discover.
        /// <para>
        /// One atomic, rollback-safe persist under this service's private lock. Throws
        /// <see cref="ArgumentException"/> for an invalid/out-of-universe id (programmer
        /// error); reports an expected persist failure as
        /// <see cref="CodexOutcome.PersistenceFailed"/> (never a faulted task to gameplay).
        /// </para>
        /// </summary>
        public async Task<CodexResult> RecordDiscoveryAsync(string id, DiscoverySource via)
        {
            // Input validation BEFORE the lock — an invalid id is a caller bug, not expected
            // bad input (mirrors CreditService throwing on cost <= 0). A valid-format but
            // unpopulated id (e.g. mon40, reserved in MVP) is still a legal universe id.
            if (string.IsNullOrEmpty(id) || !MonsterDatabase.IsValidMonsterId(id))
            {
                throw new ArgumentException(
                    $"'{id}' is not a valid Monster id (expected mon01..mon{MonsterDatabase.UniverseCount:00}).",
                    nameof(id));
            }

            CodexResult result;
            bool committed = false;
            bool isFirstDiscovery = false;
            int newCount = 0;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Capture the model reference ONCE and use it for the check, mutation, and
                // rollback: a recovery swap mid-persist must never make the rollback write
                // onto a NEW model.
                SaveModel model = RequireModel();

                // "Discovered" == the KEY is present (the canonical rule, mirrored by
                // DiscoveredCount/IsDiscovered/GetEntry). Use ContainsKey, NOT `existing != null`:
                // a key present with a null VALUE (crafted/corrupt input — production's
                // CoerceNullCollections repairs these on load) is still DISCOVERED, so it must
                // not be misclassified as a first discovery (which would re-fire OnMonsterDiscovered
                // and double-count). Such a null value is repaired in place below.
                bool keyPreExisted = model.Codex.TryGetValue(id, out CodexEntryData existing);
                isFirstDiscovery = !keyPreExisted;
                bool flagAlreadySet = existing != null && IsFlagSet(existing, via);

                if (keyPreExisted && existing != null && flagAlreadySet)
                {
                    // Pure no-op: entry present and the requested flag already set. No persist,
                    // no event, no increment. (The authoritative idempotency gate — AC-2.)
                    return CodexResult.AlreadyRecorded(model.Codex.Count);
                }

                // Capture exactly the pre-mutation state needed to restore on fault: whether the
                // key pre-existed, and the prior entry object so a fault restores it byte-for-byte
                // (including the rare null-valued-key case). After the no-op short-circuit, the
                // flag we are about to set is necessarily currently unset, so the only state that
                // can change is "key absent → present" or "flag false → true" (or a null value
                // repaired to a real entry) — all reverted by restoring the captured prior entry.
                CodexEntryData priorEntry = existing;

                CodexEntryData entry = existing;
                if (entry == null)
                {
                    // First discovery (key absent) OR a null-valued key being repaired in place.
                    entry = new CodexEntryData(); // VariantFlags defaults to empty
                    model.Codex[id] = entry;
                }

                SetFlag(entry, via, true);

                try
                {
                    await _saveService.SaveAsync().ConfigureAwait(false);

                    if (ReferenceEquals(_saveService.Current, model))
                    {
                        committed = true;
                        newCount = model.Codex.Count;
                        result = isFirstDiscovery
                            ? CodexResult.Discovered(newCount)
                            : CodexResult.FlagUpdated(newCount);
                    }
                    else
                    {
                        // A recovery swap replaced the model mid-persist: SaveAsync did NOT throw —
                        // it durably persisted whatever _saveService.Current pointed at, which is
                        // NOT this discovery. Roll back onto the captured (now-detached) ref so it
                        // matches what we believe; report the count from the AUTHORITATIVE live
                        // model (_saveService.Current), not the detached one — the DailyRewardService
                        // swap-branch contract (report Current's state, fall back to the detached
                        // ref only if recovery also nulled Current).
                        RollBack(model, id, keyPreExisted, priorEntry, entry, via);
                        GameLog.Error(
                            $"CodexService: discovery of '{id}' rolled back — the save model was " +
                            "swapped mid-operation (recovery raced a mutation).");
                        result = CodexResult.PersistenceFailed(
                            _saveService.Current?.Codex.Count ?? model.Codex.Count);
                    }
                }
                catch (Exception ex)
                {
                    // Restore the exact pre-mutation state onto the captured ref, don't recompute.
                    RollBack(model, id, keyPreExisted, priorEntry, entry, via);
                    GameLog.Error(
                        $"CodexService: discovery of '{id}' rolled back — persist failed. {ex.Message}");
                    result = CodexResult.PersistenceFailed(model.Codex.Count);
                }
            }
            finally
            {
                _gate.Release();
            }

            // Events AFTER the lock releases, ONLY on a committed first discovery. Order:
            // OnMonsterDiscovered, then OnCodexCompleted (completion is a consequence of the
            // discovery). A flag-flip commit is NOT a discovery — no events.
            if (committed && isFirstDiscovery)
            {
                RaiseMonsterDiscovered(id);
                if (newCount == UniverseCount)
                {
                    RaiseCodexCompleted();
                }
            }

            return result;
        }

        private static bool IsFlagSet(CodexEntryData entry, DiscoverySource via) =>
            via == DiscoverySource.Capture ? entry.Captured : entry.Slain;

        private static void SetFlag(CodexEntryData entry, DiscoverySource via, bool value)
        {
            if (via == DiscoverySource.Capture)
            {
                entry.Captured = value;
            }
            else
            {
                entry.Slain = value;
            }
        }

        /// <summary>
        /// Restore the EXACT pre-mutation state on the captured model reference. Three cases,
        /// covering every path that reaches here (the flag we set was necessarily false before,
        /// per the no-op short-circuit, so reverting it means clearing it):
        /// <list type="bullet">
        /// <item>key did not pre-exist → remove it (undo the create);</item>
        /// <item>key pre-existed with a REAL entry (a flag flip) → clear the flag we set on that
        ///   same live instance, back to its prior false;</item>
        /// <item>key pre-existed with a NULL value (crafted/corrupt input we repaired in place) →
        ///   re-seat the captured null, NOT delete the key (it was present as null before).</item>
        /// </list>
        /// Never recompute from <c>_saveService.Current</c> (the 2.1/1.8 rollback-fidelity lesson).
        /// </summary>
        private static void RollBack(
            SaveModel model, string id, bool keyPreExisted, CodexEntryData priorEntry,
            CodexEntryData mutatedEntry, DiscoverySource via)
        {
            if (!keyPreExisted)
            {
                model.Codex.Remove(id);
            }
            else if (priorEntry == null)
            {
                // The key was present as null and we replaced it with a fresh entry; restore null.
                model.Codex[id] = null;
            }
            else
            {
                // A real prior entry whose flag we flipped on the same instance: clear it.
                SetFlag(mutatedEntry, via, false);
            }
        }

        private void RaiseMonsterDiscovered(string id)
        {
            try
            {
                OnMonsterDiscovered?.Invoke(id);
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"CodexService: an OnMonsterDiscovered subscriber threw — the discovery is already committed. {ex.Message}");
            }
        }

        private void RaiseCodexCompleted()
        {
            try
            {
                OnCodexCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                GameLog.Error(
                    $"CodexService: an OnCodexCompleted subscriber threw — the discovery is already committed. {ex.Message}");
            }
        }

        /// <summary>
        /// The loaded model, or an <see cref="InvalidOperationException"/> when it is not
        /// loaded yet (Bootstrap fire-and-forgets the initial load, so this service is
        /// resolvable before the model exists) or the save is corrupt and unrecovered — a
        /// bare read there would null-ref, not a typed failure. Mirrors the Economy services'
        /// before-load contract; reads do NOT silently return a benign default.
        /// </summary>
        private SaveModel RequireModel()
        {
            SaveModel model = _saveService.Current;
            if (model == null)
            {
                throw new InvalidOperationException(
                    "CodexService has no loaded save model — await SaveService.InitializeAsync " +
                    "(or recover the corrupt save) before reading or recording Codex discoveries.");
            }

            return model;
        }
    }
}
