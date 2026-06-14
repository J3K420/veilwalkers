using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.AR;

namespace Veilwalkers.AR.Tests
{
    /// <summary>
    /// Test double for <see cref="ISpawnSink"/> (Story 3.4). Counts instantiate / activate / deactivate
    /// calls and tracks which ids are live, so a test can prove the cap (no <see cref="Instantiate"/> past
    /// N) and pooling (a spawn-after-release REUSES via <see cref="Activate"/> rather than instantiating
    /// again — the NFR-1 instruments). No Unity scene types.
    /// </summary>
    internal sealed class FakeSpawnSink : ISpawnSink
    {
        private int _nextId;

        /// <summary>How many NEW instances were created. The cap pin asserts this stops at N; the pooling
        /// pin asserts a reuse does NOT grow it.</summary>
        public int InstantiateCalls { get; private set; }

        /// <summary>How many times a pooled instance was re-shown (the reuse path).</summary>
        public int ActivateCalls { get; private set; }

        /// <summary>How many times an instance was returned to the pool.</summary>
        public int DeactivateCalls { get; private set; }

        /// <summary>Currently-active sink ids (activated/instantiated and not yet deactivated).</summary>
        public readonly HashSet<int> LiveIds = new HashSet<int>();

        public int Instantiate(in Pose pose)
        {
            InstantiateCalls++;
            int id = _nextId++;
            LiveIds.Add(id);
            return id;
        }

        public void Activate(int id, in Pose pose)
        {
            ActivateCalls++;
            LiveIds.Add(id);
        }

        public void Deactivate(int id)
        {
            DeactivateCalls++;
            LiveIds.Remove(id);
        }
    }
}
