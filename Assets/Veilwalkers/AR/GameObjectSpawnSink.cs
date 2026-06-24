using UnityEngine;
using Veilwalkers.Core;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
#endif

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
        // Story 8.3 device body. Instantiates the placeholder monster prefab (Gate 1.4) and pools instances
        // by the sink-assigned id. MonsterSpawner owns the cap/free-list DECISIONS; this seam only does the
        // GameObject work. The prefab is loaded LAZILY from Resources on first spawn (scene-independent, so
        // the adapter Bootstrap constructs before the rig is live never touches a missing asset). Never
        // throws (NFR-3): a missing prefab degrades to a logged no-op returning a synthetic id.
        private const string MonsterPrefabResourcePath = "Monster";
        private readonly Dictionary<int, GameObject> _instances = new Dictionary<int, GameObject>();
        private GameObject _prefab;
        private bool _prefabResolveAttempted;

        private GameObject Prefab()
        {
            if (_prefab == null && !_prefabResolveAttempted)
            {
                _prefabResolveAttempted = true;
                _prefab = Resources.Load<GameObject>(MonsterPrefabResourcePath);
                if (_prefab == null)
                {
                    GameLog.Warn($"GameObjectSpawnSink: no monster prefab at Resources/{MonsterPrefabResourcePath}; degrading to no-op (NFR-3).");
                }
            }

            return _prefab;
        }

        public int Instantiate(in Pose pose)
        {
            int id = _nextId++;

            GameObject prefab = Prefab();
            if (prefab == null)
            {
                return id;
            }

            try
            {
                GameObject go = Object.Instantiate(prefab, pose.position, pose.rotation);
                _instances[id] = go;
            }
            catch (System.Exception ex)
            {
                GameLog.Warn("GameObjectSpawnSink.Instantiate failed; degrading (NFR-3). " + ex.Message);
            }

            return id;
        }

        public void Activate(int id, in Pose pose)
        {
            if (_instances.TryGetValue(id, out GameObject go) && go != null)
            {
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                go.SetActive(true);
            }
        }

        public void Deactivate(int id)
        {
            if (_instances.TryGetValue(id, out GameObject go) && go != null)
            {
                go.SetActive(false);
            }
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
