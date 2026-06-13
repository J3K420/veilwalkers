using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.Economy;
using Veilwalkers.Persistence;

namespace Veilwalkers.App.Tests
{
    /// <summary>
    /// <see cref="FirstLaunchGrant"/> policy (Story 1.7): a fresh install grants exactly
    /// 20 once (AC-1), a granted/migrated model is never re-granted (AC-1 once-per-install
    /// + AC-3 existing player), a wiped save re-grants (AC-2 reinstall), a failed persist
    /// leaves the grant retryable, and a null/corrupt model is a safe no-op.
    /// <para>
    /// Uses a local in-memory <see cref="IProgressStore"/> that round-trips the scalar
    /// fields these tests assert on — including the new <c>StartingCreditsGranted</c>
    /// marker — with the real <see cref="SaveService"/> and <see cref="CreditService"/>,
    /// so the grant path is exercised end to end, not mocked. (Scalar fidelity, like the
    /// Economy.Tests fake: no test here mutates the collections, and a shallow share
    /// would re-introduce aliasing.)
    /// </para>
    /// </summary>
    public sealed class FirstLaunchGrantTests
    {
        /// <summary>
        /// In-memory store local to App.Tests (the Economy.Tests fake is internal to that
        /// assembly and predates the marker). Clones the full model so persisted reads are
        /// real, not aliased; supports a one-shot save failure for the retry test.
        /// </summary>
        private sealed class InMemoryStore : IProgressStore
        {
            public SaveModel Stored;
            public bool FailNextSave;

            /// <summary>1-based index of a SaveAsync call to fail (0 = none). One-shot.</summary>
            public int FailOnSaveNumber;
            private int _saveCalls;

            public bool Exists() => Stored != null;

            public Task<SaveModel> LoadAsync()
            {
                if (Stored == null)
                {
                    throw new FileNotFoundException("InMemoryStore: no save exists.");
                }

                return Task.FromResult(Clone(Stored));
            }

            public Task SaveAsync(SaveModel model)
            {
                _saveCalls++;
                if (FailNextSave || _saveCalls == FailOnSaveNumber)
                {
                    FailNextSave = false;
                    FailOnSaveNumber = 0;
                    throw new IOException("InMemoryStore: simulated save failure.");
                }

                Stored = Clone(model);
                return Task.CompletedTask;
            }

            public Task DeleteAsync()
            {
                Stored = null;
                return Task.CompletedTask;
            }

            // Scalar-fidelity copy (every field these tests assert on); collections reset
            // to fresh defaults — no test here touches them, and a shallow share would
            // re-introduce aliasing. Matches the Economy.Tests FakeProgressStore.
            private static SaveModel Clone(SaveModel model) => new SaveModel
            {
                SchemaVersion = model.SchemaVersion,
                Credits = model.Credits,
                StartingCreditsGranted = model.StartingCreditsGranted,
                Xp = model.Xp,
                Level = model.Level,
                StrongCaptureCharges = model.StrongCaptureCharges,
                StabilityBoostCharges = model.StabilityBoostCharges,
                NightveilFilterCharges = model.NightveilFilterCharges,
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
            };
        }

        private static (InMemoryStore store, SaveService save, CreditService credit, FirstLaunchGrant grant)
            CreateInitialized(SaveModel seed)
        {
            var store = new InMemoryStore { Stored = seed };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            // One shared lock across the credit service and the grant — the same wiring
            // Bootstrap uses, so the grant's marker write serializes with credit mutations.
            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            var grant = new FirstLaunchGrant(save, credit, mutationLock);
            return (store, save, credit, grant);
        }

        [Test]
        public void Fresh_install_grants_exactly_20_and_marks_granted()
        {
            // Fresh model: Credits 0, marker false.
            var (store, save, credit, grant) = CreateInitialized(new SaveModel());

            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(FirstLaunchGrant.StartingCredits, credit.Balance, "Balance is the 20-credit grant.");
            Assert.AreEqual(20, credit.Balance, "StartingCredits is canon 20.");
            Assert.IsTrue(save.Current.StartingCreditsGranted, "Marker set after a successful grant.");
            Assert.AreEqual(20, store.Stored.Credits, "Grant persisted.");
            Assert.IsTrue(store.Stored.StartingCreditsGranted, "Marker persisted.");
        }

        [Test]
        public void Running_twice_does_not_re_grant()
        {
            var (_, save, credit, grant) = CreateInitialized(new SaveModel());

            grant.RunAsync().GetAwaiter().GetResult();
            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(20, credit.Balance, "Second run is a no-op; balance stays 20, not 40.");
            Assert.IsTrue(save.Current.StartingCreditsGranted);
        }

        [Test]
        public void Already_granted_model_is_left_untouched()
        {
            // AC-3 existing player: marker already true (e.g. migrated v1 save), Credits 7.
            var seed = new SaveModel { Credits = 7, StartingCreditsGranted = true };
            var (_, save, credit, grant) = CreateInitialized(seed);

            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(7, credit.Balance, "An already-granted player is not re-granted.");
            Assert.IsTrue(save.Current.StartingCreditsGranted);
        }

        [Test]
        public void Reinstall_fresh_model_grants_again()
        {
            // AC-2: a wiped save is a fresh model again (marker false) → grant repeats.
            // The marker, not file history, gates the grant.
            var (_, save, credit, grant) = CreateInitialized(new SaveModel());

            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(20, credit.Balance);
            Assert.IsTrue(save.Current.StartingCreditsGranted);
        }

        [Test]
        public void A_failed_grant_persist_leaves_it_retryable()
        {
            var (store, save, credit, grant) = CreateInitialized(new SaveModel());
            store.FailNextSave = true; // the grant's own persist throws

            // The forced persist failure logs in SaveService's catch, then the rollback
            // logs in CreditService's — both LogType.Error on this thread (the fake's
            // faulted task continues synchronously). UTF fails on an unexpected Error, so
            // these are declared expected (the established 1.4 rollback-test pattern).
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));
            LogAssert.Expect(LogType.Error, new Regex("CreditService: grant of 20 rolled back"));

            grant.RunAsync().GetAwaiter().GetResult();

            // Grant rolled back (balance unchanged), marker not set → next run retries.
            Assert.AreEqual(0, credit.Balance, "A failed grant persist rolls back to 0.");
            Assert.IsFalse(save.Current.StartingCreditsGranted, "Marker stays false after a failed grant.");

            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(20, credit.Balance, "The retry on next run succeeds.");
            Assert.IsTrue(save.Current.StartingCreditsGranted);
        }

        [Test]
        public void A_failed_marker_persist_rolls_the_marker_back_and_retries()
        {
            // The grant's own persist (save #1) succeeds; the marker persist (save #2)
            // fails. The +20 stays granted, but the marker must roll back to false so
            // memory matches disk and the next launch completes the marker write.
            var (store, save, credit, grant) = CreateInitialized(new SaveModel());
            store.FailOnSaveNumber = 2; // fail the marker write, not the grant

            // The marker SaveAsync's failure logs LogType.Error in SaveService's catch
            // (FirstLaunchGrant then rolls back + logs Warn). Declare the expected Error
            // so UTF does not treat it as an unexpected failure.
            LogAssert.Expect(LogType.Error, new Regex("SaveService: save failed"));

            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(20, credit.Balance, "The grant persisted; credits are unaffected by the marker failure.");
            Assert.IsFalse(save.Current.StartingCreditsGranted, "Marker rolled back to false after its persist failed.");
            Assert.IsTrue(store.Stored.Credits == 20, "Disk holds the granted credits.");
            Assert.IsFalse(store.Stored.StartingCreditsGranted, "Disk marker is false (the write never landed).");

            // Next launch: marker is false, so RunAsync re-enters and re-grants before
            // marking — the accepted "benign duplicate = same as a reinstall" tradeoff
            // (AC-2) for a marker-write that never landed. The retry's marker write now
            // succeeds, so it never recurs beyond this one extra grant.
            grant.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(40, credit.Balance,
                "A lost marker write costs one benign re-grant on the next launch (AC-2 tradeoff).");
            Assert.IsTrue(save.Current.StartingCreditsGranted, "Retry completes the marker write.");
            Assert.IsTrue(store.Stored.StartingCreditsGranted, "Marker now persisted; no further re-grants.");
        }

        [Test]
        public void Null_model_is_a_safe_no_op()
        {
            // A corrupt/unloaded save has a null Current — never grant onto it.
            var store = new InMemoryStore { Stored = null };
            var save = new SaveService(store);
            // Deliberately NOT initialized → Current is null.
            var mutationLock = new SaveMutationLock();
            var credit = new CreditService(save, mutationLock);
            var grant = new FirstLaunchGrant(save, credit, mutationLock);

            Assert.IsNull(save.Current, "Precondition: no model loaded.");
            Assert.DoesNotThrow(() => grant.RunAsync().GetAwaiter().GetResult(), "Null model no-ops, never throws.");
        }
    }
}
