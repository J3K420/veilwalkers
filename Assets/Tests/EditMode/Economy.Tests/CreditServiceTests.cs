using System;
using System.Collections.Generic;
using NUnit.Framework;
using Veilwalkers.Core;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy.Tests
{
    /// <summary>
    /// <see cref="CreditService"/> ledger behavior: typed-result spend paths
    /// (happy / exact-balance / insufficient), grant, the post-persist event
    /// contract, and the programmer-error guards — negative paths written in the
    /// first pass, per the 1.3 lesson.
    /// </summary>
    public sealed class CreditServiceTests
    {
        private static (FakeProgressStore store, SaveService save, CreditService credits)
            CreateInitialized(int credits)
        {
            var store = new FakeProgressStore { Stored = new SaveModel { Credits = credits } };
            var save = new SaveService(store);
            save.InitializeAsync().GetAwaiter().GetResult();
            return (store, save, new CreditService(save));
        }

        [Test]
        public void Spend_happy_path_deducts_persists_and_raises_changed_after_persist()
        {
            var (store, save, credits) = CreateInitialized(5);

            var order = new List<string>();
            var changedPayloads = new List<int>();
            save.OnSaveCompleted += () => order.Add("persisted");
            credits.OnCreditsChanged += newBalance =>
            {
                order.Add("changed");
                changedPayloads.Add(newBalance);
            };

            SpendResult result = credits.TrySpendCreditsAsync(3).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.NewBalance);
            Assert.AreEqual(SpendFailureReason.None, result.FailureReason);
            Assert.AreEqual(2, credits.Balance);
            Assert.AreEqual(1, store.SaveCalls, "The spend must persist exactly once.");
            CollectionAssert.AreEqual(new[] { "persisted", "changed" }, order,
                "OnCreditsChanged fires exactly once, AFTER the committed persist.");
            CollectionAssert.AreEqual(new[] { 2 }, changedPayloads);
        }

        [Test]
        public void Exact_balance_spend_succeeds_to_zero_without_insufficient_event()
        {
            var (store, save, credits) = CreateInitialized(3);

            int insufficientCount = 0;
            credits.OnInsufficientCredits += _ => insufficientCount++;

            SpendResult result = credits.TrySpendCreditsAsync(3).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "balance == cost must succeed (spend condition is balance >= cost).");
            Assert.AreEqual(0, result.NewBalance);
            Assert.AreEqual(0, credits.Balance);
            Assert.AreEqual(0, insufficientCount);
            Assert.AreEqual(1, store.SaveCalls);
        }

        [Test]
        public void Insufficient_balance_returns_typed_failure_without_mutation_or_persist()
        {
            var (store, save, credits) = CreateInitialized(2);

            int changedCount = 0;
            var insufficientEvents = new List<InsufficientCreditsEvent>();
            credits.OnCreditsChanged += _ => changedCount++;
            credits.OnInsufficientCredits += e => insufficientEvents.Add(e);

            SpendResult result = credits.TrySpendCreditsAsync(3).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SpendFailureReason.InsufficientCredits, result.FailureReason);
            Assert.AreEqual(2, result.NewBalance, "NewBalance reports the unchanged balance on failure.");
            Assert.AreEqual(2, save.Current.Credits, "The balance must be untouched.");
            Assert.AreEqual(0, store.SaveCalls, "A failed affordability check must not persist.");
            Assert.AreEqual(0, changedCount, "OnCreditsChanged must not fire for a failed spend.");
            Assert.AreEqual(1, insufficientEvents.Count);
            Assert.AreEqual(3, insufficientEvents[0].Cost);
            Assert.AreEqual(2, insufficientEvents[0].Balance);
        }

        [Test]
        public void Grant_happy_path_adds_persists_and_raises_changed()
        {
            var (store, save, credits) = CreateInitialized(0);

            var changedPayloads = new List<int>();
            credits.OnCreditsChanged += newBalance => changedPayloads.Add(newBalance);

            Result result = credits.GrantCreditsAsync(20).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(20, credits.Balance);
            Assert.AreEqual(1, store.SaveCalls);
            CollectionAssert.AreEqual(new[] { 20 }, changedPayloads);
        }

        [Test]
        public void Non_positive_cost_or_amount_throws_ArgumentOutOfRange()
        {
            var (store, save, credits) = CreateInitialized(5);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => credits.TrySpendCreditsAsync(0).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => credits.TrySpendCreditsAsync(-1).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => credits.GrantCreditsAsync(0).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => credits.GrantCreditsAsync(-5).GetAwaiter().GetResult());

            Assert.AreEqual(5, credits.Balance, "Guard throws must leave the balance untouched.");
            Assert.AreEqual(0, store.SaveCalls);
        }

        [Test]
        public void Use_before_initialize_throws_InvalidOperation_from_spend_grant_and_Balance()
        {
            // SaveService never initialized: Current is null — the window Bootstrap
            // documents (service resolvable before the model loads).
            var store = new FakeProgressStore();
            var credits = new CreditService(new SaveService(store));

            int eventCount = 0;
            credits.OnCreditsChanged += _ => eventCount++;
            credits.OnInsufficientCredits += _ => eventCount++;

            Assert.Throws<InvalidOperationException>(() => _ = credits.Balance);
            Assert.Throws<InvalidOperationException>(
                () => credits.TrySpendCreditsAsync(1).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(
                () => credits.GrantCreditsAsync(1).GetAwaiter().GetResult());

            Assert.AreEqual(0, store.SaveCalls, "An uninitialized service must never persist.");
            Assert.AreEqual(0, eventCount, "An uninitialized service must never raise events.");
        }

        [Test]
        public void Grant_overflowing_the_balance_throws_and_leaves_state_untouched()
        {
            var (store, save, credits) = CreateInitialized(int.MaxValue);

            int eventCount = 0;
            credits.OnCreditsChanged += _ => eventCount++;

            Assert.Throws<OverflowException>(
                () => credits.GrantCreditsAsync(1).GetAwaiter().GetResult());

            Assert.AreEqual(int.MaxValue, save.Current.Credits,
                "checked arithmetic must reject the overflow before any mutation lands.");
            Assert.AreEqual(0, store.SaveCalls);
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void Constructing_with_null_save_service_throws_ArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new CreditService(null));
        }
    }
}
