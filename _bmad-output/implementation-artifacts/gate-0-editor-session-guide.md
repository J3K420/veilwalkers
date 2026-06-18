# Epic 8 — Gate 0 Editor Session Guide

The one Editor session that unblocks the whole MVP. Everything in Gate 0 needs the Unity Editor (correct `.unity` scene YAML, imported sprite art, inspector drags) — it cannot be done headlessly. Work it **top to bottom**; the order respects the dependency chain. When you're done, close the Editor and hand back to me — I do the headless test-wiring + commit.

**Why this is the critical path:** authoring `MonsterDatabase.asset` (step A) is the single root blocker. It gates the `CodexService` + `EncounterService` Bootstrap-seam closure (step D) that **6 stories** (2.3, 4.1–4.6) are waiting on. Nothing downstream renders until Gate 0 is closed.

**Verified facts (read off the live code 2026-06-16, not assumed):**
- `EconomyConfig.asset` already exists: `Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset`, guid `b02bccce98eae3042ac3f1552e01e8b8`.
- `Bootstrap.unity` already exists; its Bootstrap MonoBehaviour (`&705507994`) has **no `_economyConfig` field serialized yet** → real latent boot-failure.
- Constructor signatures below are copied from the live `.cs` — they compile as written.

---

## A. Author `MonsterDatabase.asset` + 3–5 monster assets  *(root blocker)*

### A1 — Get 3–5 sprite images into the project
1. In the Project window, right-click `Assets/Veilwalkers` → **Create → Folder** → name it `Art` (if it doesn't exist).
2. Drop 3–5 small PNGs into `Assets/Veilwalkers/Art/` via the OS file explorer. Placeholders are fine — validation only needs the `Art` ref **non-null**, not final art.
3. Select each image → Inspector → **Texture Type = Sprite (2D and UI)** → **Apply**. (URP/2D template may already import as sprites.)

### A2 — Create the MonsterDefinition assets (×3–5)
For each monster:
1. In `Assets/Veilwalkers/ScriptableObjects/` (same home as `EconomyConfig.asset`), right-click → **Create → Veilwalkers → Monster Definition**.
2. **Rename the file** `MonNN_Name` — capital `M`, two digits, underscore, PascalCase name (e.g. `Mon01_AntleredShade`). This `<Id>_<Name>` convention is what my on-disk audit checks.
3. Fill the Inspector — **every rule below is enforced by `MonsterDatabase.Validate()`; a violation surfaces as a failing content test, not a crash:**

   | Field | Enter | Rule (from `Validate()` / `IsValidMonsterId`) |
   |---|---|---|
   | **Id** | `mon01`, `mon02`, … | lowercase `mon` + exactly two digits, **01–67**, unique. (`mon 1`/`mon+1` are rejected — exactly two ASCII digits.) |
   | **Display Name** | `Antlered Shade` | non-empty |
   | **Rarity** | Common/Uncommon/Rare/Epic/Nightmare | **≥1 asset must be Rare or higher** (Veil-Pack validity) |
   | **Capture Difficulty** | any int, e.g. `5` | provisional (OQ-9), unbounded |
   | **Slay Difficulty** | any int, e.g. `7` | provisional (OQ-9), unbounded |
   | **Lore** | a wry-eerie sentence | non-empty |
   | **Art** | drag an A1 sprite into the slot | **non-null** |

   Keep the filename's `MonNN` matching the **Id** field and `_Name` matching **Display Name** (spaces removed).

**Suggested MVP set (5, spread across tiers, ≥1 Rare+):**

| File | Id | Display Name | Rarity |
|---|---|---|---|
| `Mon01_AntleredShade.asset` | `mon01` | Antlered Shade | Common |
| `Mon02_HollowChoir.asset` | `mon02` | Hollow Choir | Uncommon |
| `Mon03_VeilCrawler.asset` | `mon03` | Veil Crawler | **Rare** ✅ |
| `Mon04_GraveLantern.asset` | `mon04` | Grave Lantern | Epic |
| `Mon05_NightmareMaw.asset` | `mon05` | Nightmare Maw | Nightmare |

(3 is the minimum. As long as one is Rare+ you're valid. `PopulatedCount` must land in **[3..5]**.)

### A3 — Create `MonsterDatabase.asset`
1. In `Assets/Veilwalkers/ScriptableObjects/`, right-click → **Create → Veilwalkers → Monster Database**. Leave the filename `MonsterDatabase.asset`.
2. Select it → Inspector → expand **Monsters** → set size to your count (3–5) → drag each `MonNN_*` definition into a slot. **No empty slots.**

### A4 — Sanity check
- No console errors on save.
- Each `MonNN_*` filename matches its Id/Name.
- `MonsterDatabase` Monsters list: 3–5 entries, no empty slots, ≥1 Rare+.

---

## B. Author the 5 `.unity` game scenes

Create each via **File → New Scene** (or duplicate an empty scene) and save into `Assets/Veilwalkers/Scenes/`:

- `Onboarding.unity`
- `Home.unity`
- `ARHunt.unity`  *(minimal placeholder — Story 8.3 fills in the AR rig later)*
- `Codex.unity`
- `Shop.unity`

For Gate 0 these can be **near-empty** scenes (the thin `*View` MonoBehaviours + UGUI are Gate 2 / Story 8.4). The point now is that the files **exist on disk** so the build-order registration (step C) points at real assets — the `Every_enabled_scene_resolves_to_a_real_unity_asset_on_disk` guard fails on any phantom path.

---

## C. Register the six-slot build order

**File → Build Settings → Scenes In Build.** Add the scenes and order them **exactly**:

```
0  Assets/Veilwalkers/Scenes/Bootstrap.unity     (enabled)
1  Assets/Veilwalkers/Scenes/Onboarding.unity    (enabled)
2  Assets/Veilwalkers/Scenes/Home.unity          (enabled)
3  Assets/Veilwalkers/Scenes/ARHunt.unity        (enabled)
4  Assets/Veilwalkers/Scenes/Codex.unity         (enabled)
5  Assets/Veilwalkers/Scenes/Shop.unity          (enabled)
```

- **Bootstrap MUST be index 0** (the composition root loads first — `Bootstrap_is_the_first_enabled_scene` pins this).
- All six **enabled**.
- Do this **only after** the scenes exist on disk (step B) — otherwise the on-disk guard fails on the phantom path.
- SampleScene must **not** appear here (already de-registered in Story 8.2; `SampleScene_is_not_in_the_enabled_build_list` pins it).

---

## D. Assign `_economyConfig` + close the Bootstrap seams

### D1 — Assign `_economyConfig` (latent boot-failure until done)
1. Open `Assets/Veilwalkers/Scenes/Bootstrap.unity`.
2. Select the **Bootstrap** GameObject → in the Inspector, find the **Bootstrap** component's **Economy Config** field (it's the `[SerializeField] private EconomyConfig _economyConfig`).
3. Drag `Assets/Veilwalkers/ScriptableObjects/EconomyConfig.asset` into that slot.
4. Save the scene.

> This writes `_economyConfig: {fileID: 11400000, guid: b02bccce98eae3042ac3f1552e01e8b8, type: 2}` into the scene YAML. Until you do this, `WireServices` throws `InvalidOperationException` at boot ("EconomyConfig is not assigned"). After it's assigned, tell me and I'll **un-ignore** `BuildSettingsGuardTests.Bootstrap_scene_assigns_the_EconomyConfig_reference`.

### D2 — Close the CodexService + EncounterService seams in `Bootstrap.cs`
This is a **code** edit (I can do this part headlessly once `MonsterDatabase.asset` exists — your choice, see "Division of labor" below). The exact steps, verified against the live constructor signatures:

1. **Add the using + serialized field** (top of `Bootstrap.cs` / the field block near `_economyConfig`):
   ```csharp
   using Veilwalkers.Monsters;
   // …
   [SerializeField] private MonsterDatabase _monsterDatabase;
   ```
   Then in `Bootstrap.unity`, drag `MonsterDatabase.asset` onto the new **Monster Database** slot (same inspector-drag as D1).

2. **Null-check it** alongside the `_economyConfig` guard:
   ```csharp
   if (_monsterDatabase == null)
       throw new System.InvalidOperationException(
           "Bootstrap: MonsterDatabase is not assigned. Assign MonsterDatabase.asset to the Bootstrap component.");
   ```

3. **Construct + register CodexService** (replace the seam comment at ~310–333):
   ```csharp
   var codexService = new CodexService(saveService, _monsterDatabase, clock);
   ```
   *(signature confirmed: `CodexService(SaveService, MonsterDatabase, IClock)`)*

4. **Construct the Encounter systems + EncounterService** (replace the seam comment at ~341–367):
   ```csharp
   var lureSystem    = new LureSystem(_economyConfig, _monsterDatabase, random);
   var captureSystem = new CaptureSystem(random);
   var slaySystem    = new SlaySystem(_economyConfig, random);
   var encounterService = new EncounterService(
       saveService, creditService, progressionService, codexService,
       economyMutationLock,                 // ← the SHARED lock — CRITICAL, never a private one
       anchorRestoreService, arSessionService,
       lureSystem, planeAnchorService, monsterSpawner,
       captureSystem, slaySystem);
   ```
   *(signatures confirmed: `LureSystem(EconomyConfig, MonsterDatabase, IRandom)`, `CaptureSystem(IRandom)`, `SlaySystem(EconomyConfig, IRandom)`, and the 12-arg `EncounterService(...)` in exactly this order)*

5. **Register them** (in the `GameServices.Register<…>` block):
   ```csharp
   GameServices.Register<CodexService>(codexService);
   GameServices.Register<EncounterService>(encounterService);
   ```

6. **Wire EncounterService into AppStateMachine** — replace the `IEncounterSnapshotPort.NoEncounter` no-op + null second source. Build a thin adapter exposing `{ HasActiveEncounter => State == EncounterState.Lured, SnapshotActiveEncounter, RehydrateFromSnapshot }` onto `IEncounterSnapshotPort`, and pass `encounterService` (as `IInsufficientCreditsSource`) as the third ctor arg of `AppStateMachine`. *(This is the one part with a small design choice in the adapter shape — I'll handle it with you when we close the seam.)*

> **`economyMutationLock` is the single most important detail here.** EncounterService MUST share the same lock instance as CreditService/ProgressionService — a private lock would let a composed encounter write interleave with a credit spend and durably persist each other's uncommitted deltas. The code above passes the shared `economyMutationLock` already in scope.

---

## Division of labor

| Step | Who | Why |
|---|---|---|
| A (MonsterDatabase + monster assets + art) | **You, in Editor** | imported sprites + SO refs need the Editor |
| B (5 scenes) | **You, in Editor** | correct `.unity` YAML is Editor-authored |
| C (build order) | **You, in Editor** | Build Settings UI |
| D1 (`_economyConfig` drag) | **You, in Editor** | inspector drag |
| D2 (seam code in `Bootstrap.cs`) | **Me, headless** (after A lands) | pure C#, compiles + `AcyclicDependencyTests`-guarded headlessly; only the `MonsterDatabase` **drag** in D2.1 is yours |

## When you finish A–C + D1
Close the Editor (so the headless test run isn't locked) and tell me **"Gate 0 assets are authored"**. Then I (all headless):
- write the on-disk audit test (`AssetDatabase`-load `MonsterDatabase.asset`; assert `Validate()` empty, filenames `<Id>_<Name>`, `PopulatedCount ∈ [3..5]`, ≥1 Rare+);
- delete the old tautological `Asset_file_name_convention` test;
- un-ignore `Bootstrap_scene_assigns_the_EconomyConfig_reference`;
- write the D2 seam code in `Bootstrap.cs` (the registration + AppStateMachine adapter);
- run the headless gate (read `test-results.xml`, grep `error CS` — never trust exit 0), update `deferred-work.md` + the device-gate checklist, and commit.

## Notes
- **Commit `.asset` + `.meta` + the art PNGs** — static design data, tracked in git (not gitignored). Unity generates the `.meta`s; I stage them at commit.
- The D2.6 runtime claim ("`GameServices` actually resolves `CodexService`/`EncounterService` post-boot") is a **device/PlayMode** check — it lives in the Gate 5 on-device smoke, not a headless pin. The seam **code** is headless-compilable + acyclicity-guarded; resolution at runtime is verified on device.
