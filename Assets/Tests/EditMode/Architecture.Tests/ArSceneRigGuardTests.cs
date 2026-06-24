using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Story 8.3 (the HEADLESS slice, AC-7) — the AR-rig component-presence invariant, pinned as a
    /// falsifiable on-disk text read of <c>ARHunt.unity</c> (the 8.2 <see cref="BuildSettingsGuardTests"/>
    /// / 8.1 <see cref="BuildConfigGuardTests"/> / Pit text-read precedent — the Epic-8 verifiability
    /// invariant, option 1).
    /// <para>
    /// 8.3's real scope (authoring the AR rig GameObjects into <c>ARHunt.unity</c>, implementing the three
    /// adapters' <c>#if UNITY_ANDROID &amp;&amp; !UNITY_EDITOR</c> device bodies against that rig, the
    /// monster prefab, occlusion + lighting, and the on-device AR smoke) is Editor/GPU/Galaxy-S21+ bound
    /// and emitted as a release-gate checklist (deferred-work.md + docs/epic-8-device-release-gate.md
    /// Gate 1 / Gate 5). What is SAFELY HEADLESS, and what this guard pins, is the SHAPE of the authored
    /// scene: that the rig's component types are present in <c>ARHunt.unity</c> — six AR managers + one
    /// XR Origin.
    /// </para>
    /// <para>
    /// <b>Matched by serialized <c>m_Script</c> GUID, not by type name (CR fix, Story 8.3).</b> AR
    /// Foundation managers are precompiled-package <c>MonoBehaviour</c>s: a scene serializes them as
    /// <c>m_Script: {fileID: 11500000, guid: &lt;package-script-guid&gt;, type: 3}</c> with an EMPTY
    /// <c>m_EditorClassIdentifier</c> — the human-readable type name (<c>ARSession</c>, <c>ARPlaneManager</c>,
    /// …) NEVER appears as text in the YAML (confirmed: the URP <c>UniversalAdditional*Data</c> components
    /// already in <c>ARHunt.unity</c> leave no type-name text behind). So a type-name <c>Contains</c> match
    /// would be un-greenable even against a correct rig, AND would suffer substring collisions
    /// (<c>"ARSession"</c> ⊂ <c>"ARSessionOrigin"</c>). This guard instead resolves each component's
    /// script GUID at test time from its package <c>.cs.meta</c> (the installed AR Foundation version, so
    /// the guard tracks the package, not a hard-coded guid) and asserts that GUID appears in the scene —
    /// exactly the GUID/field-string mechanism the cited <see cref="BuildSettingsGuardTests"/> precedent
    /// actually uses (Unity writes guids + serialized field names verbatim). GUIDs are unique 32-hex
    /// strings, so there is no collision and removing any one component removes its guid → RED.
    /// </para>
    /// <para>
    /// <b>This pin is <c>[Ignore]</c>d-pending-Editor</b> (the 8.2 <c>_economyConfig</c> / Gate-0
    /// <c>_monsterDatabase</c> precedent). The on-disk <c>ARHunt.unity</c> authored in 8.2 is a
    /// placeholder — a Directional Light + a Main Camera only, ZERO AR rig components — so this guard
    /// (authored to assert the rig IS present) FAILS against it today. A correct rig (the AR managers +
    /// XR Origin wired with valid component fileIDs/guids) is Editor work, unsafe to hand-author as scene
    /// YAML. The <c>[Ignore]</c> is a VISIBLE pending pin (NEVER a faked green); the Editor session that
    /// authors the rig un-ignores it.
    /// </para>
    /// <para>
    /// <b>Mutation discipline (for the un-ignore session):</b> when the rig lands and this pin is
    /// un-ignored, removing ANY ONE of the seven rig components (the six AR managers OR the XR Origin)
    /// from <c>ARHunt.unity</c> must turn this test RED. That is the non-vacuity contract — the guard
    /// exists to catch a rig that silently loses (e.g.) its <c>AROcclusionManager</c> in a later edit.
    /// If the AR Foundation package scripts are not resolvable on disk (a fresh clone with no
    /// <c>Library/PackageCache</c>), the guard FAILS LOUDLY with that diagnostic rather than passing
    /// vacuously — run an Editor import/resolve first, then un-ignore.
    /// </para>
    /// <para>
    /// [Source: docs/epics.md#Story-8.3 (AC-1, AC-7); docs/epic-8-device-release-gate.md#Gate-1;
    /// deferred-work.md (the "Story 8.3 — AR rig + device-glue authoring checklist" section);
    /// Assets/Veilwalkers/Scenes/ARHunt.unity; the BuildSettingsGuardTests GUID-text-read precedent]
    /// </para>
    /// </summary>
    public sealed class ArSceneRigGuardTests
    {
        private const string ArHuntScenePath = "Assets/Veilwalkers/Scenes/ARHunt.unity";
        private const string PackageCacheRelative = "Library/PackageCache";

        // The six AR Foundation manager component types the device rig must place (AC-1). Each is a
        // precompiled-package MonoBehaviour, matched in the scene by its script GUID (resolved at test
        // time from its <type>.cs.meta — see RequireScriptGuid), NEVER by its type name (which is absent
        // from the serialized YAML). The XR Origin is asserted separately (it accepts either type — the
        // AR Foundation 5 XROrigin or the legacy ARSessionOrigin — so the rig is not forced onto one
        // AR Foundation minor version; D1).
        private static readonly string[] RequiredManagers =
        {
            "ARSession",
            "ARPlaneManager",
            "ARAnchorManager",
            "ARRaycastManager",
            "AROcclusionManager",
            "ARCameraManager",
        };

        // The XR Origin is satisfied by either component (AR Foundation 5 XROrigin or legacy
        // ARSessionOrigin). Distinct GUIDs, so no substring collision with the managers above.
        private static readonly string[] OriginAlternatives =
        {
            "XROrigin",
            "ARSessionOrigin",
        };

        private static string ProjectRoot()
            => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

        private static string ReadArHuntScene()
        {
            string path = Path.Combine(ProjectRoot(), ArHuntScenePath);

            Assert.That(
                File.Exists(path),
                Is.True,
                $"ARHunt.unity is missing ('{path}'). Story 8.3's AR-rig scene-presence guard depends on it " +
                "(8.2 authored the placeholder; 8.3 authors the rig into it).");

            return File.ReadAllText(path);
        }

        // Resolves the script GUID for an AR component type from its package <type>.cs.meta under
        // Library/PackageCache. This is what Unity writes into the scene's m_Script reference, so it is
        // the soundly-matchable token. Returns null if the package script is not resolvable on disk
        // (a fresh clone with no resolved PackageCache) — the caller fails loudly with that diagnostic
        // rather than passing vacuously.
        private static string TryResolveScriptGuid(string typeName)
        {
            string packageCache = Path.Combine(ProjectRoot(), PackageCacheRelative);
            if (!Directory.Exists(packageCache))
            {
                return null;
            }

            // CR fix (Story 8.3): Directory.EnumerateFiles matches its pattern CASE-INSENSITIVELY on
            // Windows, so "ARSession.cs.meta" also matches arcore's "ArSession.cs.meta" — a DIFFERENT
            // script with a DIFFERENT guid that is NOT the component in the scene. FirstOrDefault would
            // then pick the wrong meta and resolve a guid the scene never references → a false RED even
            // against a correct rig. So we re-filter the enumerator's hits to the meta whose ACTUAL file
            // name equals "<type>.cs.meta" EXACTLY (Ordinal, case-sensitive), the soundly-matchable token.
            string expectedFileName = typeName + ".cs.meta";
            string[] exactMatches = Directory
                .EnumerateFiles(packageCache, expectedFileName, SearchOption.AllDirectories)
                .Where(f => string.Equals(
                    Path.GetFileName(f), expectedFileName, System.StringComparison.Ordinal))
                .ToArray();

            // No EXACT-case match → the package script is not resolvable on disk (or only a wrong-case
            // homograph like arcore's ArSession exists). Stay null so the caller fails loudly, never
            // resolves a colliding guid.
            if (exactMatches.Length == 0)
            {
                return null;
            }

            // Our AR types each have a single exact-case meta, so this is normally exactMatches[0]. Be
            // defensive only as a TIEBREAKER (never as the primary filter, per the contract): if more than
            // one exact-case meta survives, prefer a Runtime/non-Editor, non-Tests path so an editor- or
            // test-only stub can never shadow the runtime component the scene actually serializes.
            string metaFile = exactMatches.Length == 1
                ? exactMatches[0]
                : (exactMatches.FirstOrDefault(f =>
                       f.IndexOf("/Editor/", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                       f.IndexOf("\\Editor\\", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                       f.IndexOf("/Tests/", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                       f.IndexOf("\\Tests\\", System.StringComparison.OrdinalIgnoreCase) < 0)
                   ?? exactMatches[0]);

            foreach (string line in File.ReadAllLines(metaFile))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("guid:"))
                {
                    return trimmed.Substring("guid:".Length).Trim();
                }
            }

            return null;
        }

        [Test]
        public void ARHunt_scene_contains_the_AR_rig_component_types()
        {
            string scene = ReadArHuntScene();

            // Each manager: resolve its package script GUID, then assert the scene references it. Failing
            // to resolve a GUID (no PackageCache) is itself a hard failure — never a vacuous pass.
            string[] unresolved = RequiredManagers
                .Where(name => TryResolveScriptGuid(name) == null)
                .ToArray();

            Assert.That(
                unresolved,
                Is.Empty,
                "Could not resolve the script GUID for AR manager(s): " + string.Join(", ", unresolved) +
                ". The AR Foundation package scripts are not on disk (no resolved Library/PackageCache). " +
                "Run an Editor package import/resolve before un-ignoring this guard — it matches by the " +
                "package script GUID, not by type name. [Source: docs/epic-8-device-release-gate.md#Gate-1]");

            string[] missingManagers = RequiredManagers
                .Where(name => !scene.Contains(TryResolveScriptGuid(name)))
                .ToArray();

            Assert.That(
                missingManagers,
                Is.Empty,
                "ARHunt.unity is missing required AR rig manager component(s): " +
                string.Join(", ", missingManagers) + " (matched by package script GUID). The device AR " +
                "rig (AC-1) must place all six AR managers so the three adapters' device bodies have a " +
                "live subsystem to drive. [Source: docs/epic-8-device-release-gate.md#Gate-1]");

            // XR Origin: either alternative whose GUID resolves AND appears in the scene satisfies it.
            bool hasOrigin = OriginAlternatives
                .Select(TryResolveScriptGuid)
                .Where(guid => guid != null)
                .Any(scene.Contains);

            Assert.That(
                hasOrigin,
                Is.True,
                "ARHunt.unity has no XR Origin component (neither '" +
                string.Join("' nor '", OriginAlternatives) + "', matched by package script GUID). The AR " +
                "rig (AC-1) must place an XR Origin (the AR Foundation 5 XROrigin or the legacy " +
                "ARSessionOrigin) carrying the AR camera + camera-background, so the spawned monster + " +
                "anchors live under a real session origin. [Source: docs/epic-8-device-release-gate.md#Gate-1]");
        }
    }
}
