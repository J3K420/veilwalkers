using UnityEngine;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The minimal instantiate / activate / deactivate seam under <see cref="MonsterSpawner"/> (Story
    /// 3.4, AC-3) — the OS/scene-touching edge that does the actual <c>GameObject</c> work. It exists so
    /// the pooling + concurrent-spawn-cap ACCOUNTING (the NFR-1 decision genuinely worth testing) is
    /// headless-testable: <see cref="MonsterSpawner"/> owns the pool bookkeeping (free list, active count,
    /// the cap) and a fake sink counts these calls, while the production
    /// <see cref="GameObjectSpawnSink"/> wraps the real URP <c>GameObject</c> pool.
    /// <para>
    /// <b>Thin glue — ZERO branching (architecture.md:466):</b> this seam holds NO cap/pool decisions.
    /// Those all live in <see cref="MonsterSpawner"/>. This interface is the adapter the "AR contains no
    /// branching logic worth testing — thin adapter" mandate targets.
    /// </para>
    /// </summary>
    public interface ISpawnSink
    {
        /// <summary>
        /// Create a NEW instance at <paramref name="pose"/> and return its sink-assigned instance id.
        /// Called by <see cref="MonsterSpawner"/> only when no pooled instance is free and the cap has
        /// room — pooling reuses via <see cref="Activate"/> instead, so repeated spawn/release cycles do
        /// NOT keep instantiating (the NFR-1 pooling proof).
        /// </summary>
        int Instantiate(in Pose pose);

        /// <summary>Re-show + reposition a pooled (previously <see cref="Deactivate"/>d) instance at
        /// <paramref name="pose"/> — the reuse path.</summary>
        void Activate(int id, in Pose pose);

        /// <summary>Hide + return an instance to the pool (it becomes free for reuse via
        /// <see cref="Activate"/>).</summary>
        void Deactivate(int id);
    }
}
