using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Story 8.2 (the HEADLESS slice) — the build-settings + Bootstrap-scene invariants, pinned as
    /// falsifiable on-disk text reads (the 8.1 <see cref="BuildConfigGuardTests"/> / Pit precedent).
    /// <para>
    /// 8.2's full scope (authoring the 5 game scenes, the 6-slot build order, the MonsterDatabase
    /// asset, and the CodexService/EncounterService Bootstrap seam closure) is Editor/device-bound
    /// and emitted as a release-gate checklist (deferred-work.md). What is SAFELY HEADLESS, and what
    /// this guard pins, is: (a) the dead SampleScene is de-registered from the build; (b) every
    /// ENABLED scene path resolves to a real <c>.unity</c> asset on disk (no phantom/missing-GUID
    /// scene ships — the guard grows automatically as real scenes are authored + registered); and
    /// (c) Bootstrap is enabled at build index 0 (the composition root must load first).
    /// </para>
    /// <para>
    /// The <c>_economyConfig</c> + <c>_monsterDatabase</c> assignment pins were once <c>[Ignore]</c>d-pending
    /// (the refs are Editor inspector assignments, unsafe to hand-author as scene YAML); the Gate-0 Editor
    /// session that assigned both refs (Bootstrap.unity has them serialized now) un-ignored both pins, so they
    /// are plain active <c>[Test]</c>s today and must stay green — a future scene edit that drops either ref
    /// is a real latent boot-failure (<c>WireServices</c> requires both), and these pins catch it.
    /// </para>
    /// <para>
    /// [Source: docs/epics.md#Story-8.2; deferred-work.md (the "Story 8.2 — Editor/device authoring
    /// checklist" section + the Story 2.2 Task-3 MonsterDatabase.asset blocker entry);
    /// ProjectSettings/EditorBuildSettings.asset]
    /// </para>
    /// </summary>
    public sealed class BuildSettingsGuardTests
    {
        private const string SampleSceneGuid = "99c9720ab356a0642a771bea13969a05";
        private const string BootstrapScenePath = "Assets/Veilwalkers/Scenes/Bootstrap.unity";

        private static string ResolveExistingProjectRootFile(string relativePath, string humanName)
        {
            string path = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, "..", relativePath));

            Assert.That(
                File.Exists(path),
                Is.True,
                $"{humanName} is missing ('{path}'). Story 8.2's build-settings guard depends on it.");

            return path;
        }

        // Parses the enabled scene entries from EditorBuildSettings.asset as TEXT. Each m_Scenes
        // entry is `- enabled: <0|1>` then `  path: <path>` then `  guid: <guid>`. Returns the
        // (enabled, path, guid) tuples in order.
        private static (bool enabled, string path, string guid)[] ReadBuildScenes()
        {
            string file = ResolveExistingProjectRootFile(
                Path.Combine("ProjectSettings", "EditorBuildSettings.asset"), "EditorBuildSettings.asset");
            string text = File.ReadAllText(file);

            // Each scene block: "- enabled: N\n    path: P\n    guid: G"
            var rx = new Regex(
                @"-\s*enabled:\s*(\d+)\s*\r?\n\s*path:\s*(\S+)\s*\r?\n\s*guid:\s*(\S+)",
                RegexOptions.Multiline);

            return rx.Matches(text)
                .Cast<Match>()
                .Select(m => (m.Groups[1].Value == "1", m.Groups[2].Value, m.Groups[3].Value))
                .ToArray();
        }

        [Test]
        public void SampleScene_is_not_in_the_enabled_build_list()
        {
            var scenes = ReadBuildScenes();
            bool sampleEnabled = scenes.Any(s => s.enabled && s.guid == SampleSceneGuid);

            Assert.That(
                sampleEnabled,
                Is.False,
                "The dead template SampleScene (guid " + SampleSceneGuid + ") must NOT be in the " +
                "enabled build list — it ships dead content + never runs the composition root " +
                "(the long-standing SampleScene-removal deferral, finally closed by Story 8.2). " +
                "De-register it from EditorBuildSettings.asset.");
        }

        [Test]
        public void Every_enabled_scene_resolves_to_a_real_unity_asset_on_disk()
        {
            var scenes = ReadBuildScenes();
            string projectRoot = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, ".."));

            string[] missing = scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .Where(p => !File.Exists(Path.Combine(projectRoot, p)))
                .ToArray();

            Assert.That(
                missing,
                Is.Empty,
                "Every ENABLED build scene must resolve to a real .unity asset on disk (no phantom " +
                "/ missing-GUID scene may ship). Missing: " + string.Join(", ", missing) + ". " +
                "Register a scene in the build list only AFTER it is authored (Story 8.2 — the 5 " +
                "game scenes are an Editor-authoring checklist item).");
        }

        [Test]
        public void Bootstrap_is_the_first_enabled_scene()
        {
            var enabled = ReadBuildScenes().Where(s => s.enabled).ToArray();

            Assert.That(enabled, Is.Not.Empty,
                "No enabled build scenes — Bootstrap (the composition root) must load first at index 0.");
            Assert.That(
                enabled[0].path,
                Is.EqualTo(BootstrapScenePath),
                "Bootstrap.unity must be the FIRST enabled build scene (index 0) so the composition " +
                "root runs before any other scene. First enabled is '" + enabled[0].path + "'.");
        }

        [Test]
        public void Bootstrap_scene_assigns_the_EconomyConfig_reference()
        {
            AssertBootstrapAssignsSerializedAsset(
                "_economyConfig", "EconomyConfig",
                "WireServices requires it; an unassigned config fails boot.");
        }

        [Test]
        public void Bootstrap_scene_assigns_the_MonsterDatabase_reference()
        {
            AssertBootstrapAssignsSerializedAsset(
                "_monsterDatabase", "MonsterDatabase",
                "WireServices requires it (CodexService + LureSystem take it non-null); " +
                "an unassigned registry fails boot.");
        }

        // Asserts the Bootstrap MonoBehaviour serializes `<field>: {fileID: N(!=0), …}` —
        // i.e. the inspector drag assigned a real asset, not the unassigned `{fileID: 0}`/absent.
        private static void AssertBootstrapAssignsSerializedAsset(
            string fieldName, string assetName, string whyRequired)
        {
            string file = ResolveExistingProjectRootFile(BootstrapScenePath, "Bootstrap.unity");
            string text = File.ReadAllText(file);

            var rx = new Regex(Regex.Escape(fieldName) + @":\s*\{fileID:\s*(\d+)");
            Match m = rx.Match(text);

            Assert.That(m.Success, Is.True,
                $"Bootstrap.unity has no {fieldName} serialized field — assign the {assetName} asset " +
                $"on the Bootstrap component ({whyRequired}).");
            Assert.That(m.Groups[1].Value, Is.Not.EqualTo("0"),
                $"Bootstrap.unity's {fieldName} is unassigned (fileID 0) — assign the {assetName} asset.");
        }
    }
}
