using System;
using UnityEngine;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The NFR-1 object-pooling + concurrent-spawn-cap decision owner (Story 3.4, AC-3;
    /// architecture.md:410, :229-230, :509). Plain C# (no <c>GameObject</c>/<c>Instantiate</c> here) so
    /// the pool bookkeeping is headless-tested against a fake <see cref="ISpawnSink"/>; the actual
    /// instantiate/activate/deactivate <c>GameObject</c> glue lives in <see cref="GameObjectSpawnSink"/>
    /// (the device/scene adapter). Mirrors the pure-logic + ctor-injected (AR-4) shape of the other AR
    /// services.
    /// <para>
    /// <b>Two NFR-1 instruments (decision #4):</b>
    /// <list type="bullet">
    /// <item><b>Pooling = REUSE.</b> A spawn after a <see cref="Release"/> reuses the freed pooled slot
    /// via <see cref="ISpawnSink.Activate"/> — it does NOT <see cref="ISpawnSink.Instantiate"/> again.</item>
    /// <item><b>The cap = REFUSAL.</b> A <see cref="TrySpawn"/> while
    /// <see cref="ActiveCount"/> == <see cref="MaxConcurrent"/> is refused (returns <c>false</c>,
    /// warns) — no over-spawn past the cap.</item>
    /// </list>
    /// The ≥30 FPS-on-mid-range device measurement itself is an AR-21 device / Firebase-Test-Lab
    /// release-gate, NOT a unit test (mirrors 3.3's "CI asserts the staging CONTRACT, not the millisecond
    /// budget" — here CI asserts the cap + pooling contract). Never throws (NFR-3): an out-of-range /
    /// double <see cref="Release"/> is a warned no-op.
    /// </para>
    /// </summary>
    public sealed class MonsterSpawner
    {
        // One pool slot. Once a slot has been instantiated, _sinkId is the sink's instance id for it; a
        // freed (inactive) slot keeps its _sinkId so the next spawn REUSES it via Activate (no re-
        // Instantiate). A slot that was never instantiated has _instantiated == false.
        private struct Slot
        {
            public int SinkId;
            public bool Instantiated;
            public bool Active;
        }

        private readonly ISpawnSink _sink;
        private readonly Slot[] _slots;

        /// <summary>The concurrent-spawn cap (AC-3 / NFR-1). Spawns past this are refused.</summary>
        public int MaxConcurrent { get; }

        /// <summary>How many instances are currently active (spawned, not released).</summary>
        public int ActiveCount { get; private set; }

        /// <summary>How many slots have been instantiated and are currently free (released, available
        /// for reuse). <see cref="ActiveCount"/> + <see cref="PooledCount"/> never exceeds
        /// <see cref="MaxConcurrent"/>.</summary>
        public int PooledCount
        {
            get
            {
                int pooled = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Instantiated && !_slots[i].Active)
                    {
                        pooled++;
                    }
                }

                return pooled;
            }
        }

        /// <param name="sink">The instantiate/activate/deactivate seam (the device/scene glue).</param>
        /// <param name="maxConcurrent">
        /// The concurrent-spawn cap. <b>Provisional starter default: 8</b> — a tunable subject to the
        /// NFR-1 device-perf pass / OQ balancing (mid-range ≥30 FPS is the real arbiter; 8 is a
        /// conservative starting point, not a balanced value). Must be ≥ 1.
        /// </param>
        public MonsterSpawner(ISpawnSink sink, int maxConcurrent = 8)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (maxConcurrent < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxConcurrent), maxConcurrent, "MonsterSpawner cap must be at least 1.");
            }

            MaxConcurrent = maxConcurrent;
            _slots = new Slot[maxConcurrent];
        }

        /// <summary>
        /// Spawn a monster at <paramref name="pose"/>, respecting the cap + reusing a pooled instance
        /// when one is free. Returns <c>false</c> (handle = -1) when <see cref="ActiveCount"/> is already
        /// at <see cref="MaxConcurrent"/> (the cap refusal — no over-spawn). On success
        /// <paramref name="handle"/> is the pool-slot index to pass to <see cref="Release"/>.
        /// </summary>
        public bool TrySpawn(in Pose pose, out int handle)
        {
            if (ActiveCount >= MaxConcurrent)
            {
                GameLog.Info(
                    $"MonsterSpawner.TrySpawn refused — at the concurrent cap ({MaxConcurrent} active). " +
                    "No over-spawn (NFR-1).");
                handle = -1;
                return false;
            }

            // Prefer reusing a freed pooled slot (the NFR-1 pooling path: Activate, not Instantiate).
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Instantiated && !_slots[i].Active)
                {
                    _sink.Activate(_slots[i].SinkId, in pose);
                    _slots[i].Active = true;
                    ActiveCount++;
                    handle = i;
                    return true;
                }
            }

            // No free pooled slot — instantiate into the first never-instantiated slot. The cap check
            // above guarantees there is one (ActiveCount < MaxConcurrent and every active/pooled slot is
            // instantiated, so a free index exists).
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Instantiated)
                {
                    int sinkId = _sink.Instantiate(in pose);
                    _slots[i].SinkId = sinkId;
                    _slots[i].Instantiated = true;
                    _slots[i].Active = true;
                    ActiveCount++;
                    handle = i;
                    return true;
                }
            }

            // Unreachable given the cap invariant, but never throw (NFR-3).
            GameLog.Error(
                "MonsterSpawner.TrySpawn: no free slot despite being under the cap — this is a bug in the " +
                "pool invariant. Refusing rather than crashing (NFR-3).");
            handle = -1;
            return false;
        }

        /// <summary>
        /// Return the instance behind <paramref name="handle"/> to the pool (deactivate via the sink,
        /// decrement <see cref="ActiveCount"/>, free the slot for reuse). Out-of-range, never-spawned, or
        /// double-<see cref="Release"/>d handles are a warned no-op — never a throw, never a negative
        /// <see cref="ActiveCount"/> (NFR-3).
        /// </summary>
        public void Release(int handle)
        {
            if (handle < 0 || handle >= _slots.Length)
            {
                GameLog.Warn(
                    $"MonsterSpawner.Release ignored — handle {handle} is out of range [0, {_slots.Length}). " +
                    "No-op (NFR-3).");
                return;
            }

            if (!_slots[handle].Instantiated || !_slots[handle].Active)
            {
                GameLog.Warn(
                    $"MonsterSpawner.Release ignored — slot {handle} is not active (never spawned, or " +
                    "already released). No-op (NFR-3).");
                return;
            }

            _sink.Deactivate(_slots[handle].SinkId);
            _slots[handle].Active = false;
            ActiveCount--;
        }
    }
}
