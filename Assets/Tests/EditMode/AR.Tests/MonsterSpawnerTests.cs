using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="MonsterSpawner"/> (Story 3.4, AC-3 / NFR-1) — the concurrent-spawn
    /// cap (REFUSAL past the cap, no over-spawn) and object pooling (REUSE a freed instance, do NOT
    /// re-instantiate). A <see cref="FakeSpawnSink"/> counts instantiate/activate/deactivate so both
    /// instruments are provable headlessly.
    /// <para>
    /// Anti-tautology: every assertion checks the PRODUCTION spawner's bookkeeping + the fake's SEAM CALL
    /// COUNTS (InstantiateCalls/ActivateCalls/DeactivateCalls), never a literal recomputed from the same
    /// input. The cap "InstantiateCalls stops at N" and the pooling "reuse does not re-instantiate" are
    /// the falsifiable, mutation-testable pins.
    /// </para>
    /// </summary>
    public sealed class MonsterSpawnerTests
    {
        private static readonly Pose AnyPose = new Pose(Vector3.zero, Quaternion.identity);

        // ---- ctor guards ----

        [Test]
        public void Ctor_rejects_a_null_sink()
        {
            Assert.Throws<ArgumentNullException>(() => new MonsterSpawner(null));
        }

        [Test]
        public void Ctor_rejects_a_cap_below_one()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonsterSpawner(new FakeSpawnSink(), 0));
        }

        // ---- AC-3: the cap is enforced (no over-spawn) ----

        [Test]
        public void Spawns_up_to_the_cap_then_refuses_without_over_instantiating()
        {
            var sink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(sink, maxConcurrent: 3);

            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out _));
            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out _));
            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out _));
            Assert.AreEqual(3, spawner.ActiveCount, "Three spawns succeeded up to the cap.");
            Assert.AreEqual(3, sink.InstantiateCalls, "Three instances were instantiated.");

            // The cap refusal is a logged Info (not a warning) — no LogAssert needed; assert behavior.
            bool fourth = spawner.TrySpawn(in AnyPose, out int handle);

            Assert.IsFalse(fourth, "The 4th spawn is refused at the cap.");
            Assert.AreEqual(-1, handle, "A refused spawn yields no handle.");
            Assert.AreEqual(3, spawner.ActiveCount, "ActiveCount stays at the cap.");
            Assert.AreEqual(3, sink.InstantiateCalls,
                "No instantiate past the cap — the cap is a REFUSAL, not a queue (NFR-1).");
        }

        // ---- AC-3: pooling = reuse, not re-instantiate ----

        [Test]
        public void Spawn_release_spawn_reuses_the_pooled_instance_instead_of_instantiating_again()
        {
            var sink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(sink, maxConcurrent: 4);

            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out int handle));
            Assert.AreEqual(1, sink.InstantiateCalls, "First spawn instantiates.");
            Assert.AreEqual(0, sink.ActivateCalls, "First spawn does not reuse.");
            Assert.AreEqual(1, spawner.ActiveCount);

            spawner.Release(handle);
            Assert.AreEqual(1, sink.DeactivateCalls, "Release deactivates the instance.");
            Assert.AreEqual(0, spawner.ActiveCount, "ActiveCount drops on release.");
            Assert.AreEqual(1, spawner.PooledCount, "The freed instance is pooled for reuse.");

            // Spawn again → must REUSE the pooled instance (Activate), not Instantiate a second time.
            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out _));
            Assert.AreEqual(1, sink.InstantiateCalls,
                "Pooling: a spawn after a release REUSES the freed instance — it must NOT instantiate again (NFR-1).");
            Assert.AreEqual(1, sink.ActivateCalls, "The reuse went through Activate.");
            Assert.AreEqual(1, spawner.ActiveCount);
        }

        // ---- NFR-3: release safety (out-of-range, never-spawned, double-release) ----

        [Test]
        public void Release_of_an_out_of_range_handle_is_a_warned_no_op()
        {
            var sink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(sink, maxConcurrent: 2);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("out of range"));

            spawner.Release(99);

            Assert.AreEqual(0, spawner.ActiveCount, "ActiveCount is not driven negative.");
            Assert.AreEqual(0, sink.DeactivateCalls, "No deactivate on an out-of-range release.");
        }

        [Test]
        public void Release_of_a_never_spawned_slot_is_a_warned_no_op()
        {
            var sink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(sink, maxConcurrent: 2);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not active"));

            spawner.Release(0);

            Assert.AreEqual(0, spawner.ActiveCount);
            Assert.AreEqual(0, sink.DeactivateCalls);
        }

        [Test]
        public void Double_release_of_the_same_handle_is_a_warned_no_op()
        {
            var sink = new FakeSpawnSink();
            var spawner = new MonsterSpawner(sink, maxConcurrent: 2);

            Assert.IsTrue(spawner.TrySpawn(in AnyPose, out int handle));
            spawner.Release(handle);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not active"));

            spawner.Release(handle);

            Assert.AreEqual(0, spawner.ActiveCount, "ActiveCount stays at 0 — not driven negative.");
            Assert.AreEqual(1, sink.DeactivateCalls, "Only the first release deactivated.");
        }
    }
}
