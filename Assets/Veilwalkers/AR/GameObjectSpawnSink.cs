using UnityEngine;
using Veilwalkers.Core;

namespace Veilwalkers.AR
{
    /// <summary>
    /// Production <see cref="ISpawnSink"/> over the URP <c>GameObject</c> instantiate / activate /
    /// deactivate work behind <see cref="MonsterSpawner"/> (Story 3.4) — the untestable adapter edge. It
    /// holds NO branching beyond the platform <c>#if</c> (the AR "thin adapter" mandate,
    /// architecture.md:466); the pooling + cap DECISIONS live in <see cref="MonsterSpawner"/>.
    /// <para>
    /// <b>Logic-complete-but-subsystem-stub (decision #2).</b> The real monster prefab + its URP
    /// <c>GameObject</c> pool live in the AR rig scene, and Story 3.4 builds no scene. So the <c>#else</c>
    /// editor path is a logged no-op stub (it returns a synthetic incrementing id so the spawner's
    /// bookkeeping is reachable in-editor), and the <c>#if UNITY_ANDROID</c> device path is a documented
    /// <c>TODO</c> wired to the real monster prefab pool when the AR rig scene lands — Story 8.3 (author
    /// the AR rig + the device bodies + the monster prefab). The <see cref="MonsterSpawner"/> pooling/cap
    /// accounting is proven against
    /// <c>FakeSpawnSink</c>; this <c>GameObject</c> glue is the deferred, device/scene-only edge.
    /// </para>
    /// <para>
    /// <b>Never throws (NFR-3).</b>
    /// </para>
    /// </summary>
    public sealed class GameObjectSpawnSink : ISpawnSink
    {
        // A monotonically increasing synthetic id for the stub paths. The real device adapter (Story 8.3)
        // returns a stable pool-slot/instance id instead.
        private int _nextId;

#if UNITY_ANDROID && !UNITY_EDITOR
        // TODO(Story 8.3): wire to the real monster prefab pool placed in the AR rig scene. Instantiate →
        // Object.Instantiate(monsterPrefab, pose.position, pose.rotation) into the AR session origin,
        // return a stable instance id; Activate → SetActive(true) + transform.SetPositionAndRotation(pose);
        // Deactivate → SetActive(false) (return to the pool). Until the prefab + scene land there is no
        // GameObject to drive, so these are conservative no-op stubs that never crash (NFR-3). The
        // MonsterSpawner pooling/cap is proven against FakeSpawnSink; this GameObject glue is the deferred,
        // device/scene-only edge.
        public int Instantiate(in Pose pose)
        {
            GameLog.Info("GameObjectSpawnSink.Instantiate: device-path stub — the monster prefab/AR rig is not placed yet (TODO Story 8.3).");
            return _nextId++;
        }

        public void Activate(int id, in Pose pose)
        {
            GameLog.Info($"GameObjectSpawnSink.Activate({id}): device-path stub (TODO Story 8.3).");
        }

        public void Deactivate(int id)
        {
            GameLog.Info($"GameObjectSpawnSink.Deactivate({id}): device-path stub (TODO Story 8.3).");
        }
#else
        // Editor / non-Android: no monster prefab / AR rig scene exists. Logged no-op stubs that return a
        // synthetic id so the MonsterSpawner bookkeeping is reachable in-editor. The spawner LOGIC never
        // depends on these side effects — it is proven entirely against FakeSpawnSink in AR.Tests.
        public int Instantiate(in Pose pose)
        {
            GameLog.Info("GameObjectSpawnSink.Instantiate: no-op off-device (no monster prefab / AR rig scene).");
            return _nextId++;
        }

        public void Activate(int id, in Pose pose)
        {
            GameLog.Info($"GameObjectSpawnSink.Activate({id}): no-op off-device.");
        }

        public void Deactivate(int id)
        {
            GameLog.Info($"GameObjectSpawnSink.Deactivate({id}): no-op off-device.");
        }
#endif
    }
}
