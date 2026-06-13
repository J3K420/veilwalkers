# Story 2.2 Task 3 — Editor Authoring Guide (MVP Monster assets)

**Goal:** create 3–5 real `MonsterDefinition` assets + one `MonsterDatabase` asset in the Unity Editor, so the MVP Codex has real content and the on-disk audit can be wired. **~10–15 min.** Nothing is blocking — all 3 ACs already pass via in-memory tests; this adds the real content + lets the AC-4 on-disk audit run.

**Hard rule:** at least ONE monster must be **Rarity ≥ Rare** (Veil Pack validity, AC-3). Every monster needs a non-null **Art** sprite, non-empty **Display Name** and **Lore** (the `Validate()` content layer rejects nulls).

---

## Step 1 — Get 3–5 sprite images in the project (placeholders are fine)

1. In the OS file explorer, drop 3–5 small PNGs into `Assets/Veilwalkers/Art/` (create the folder in Unity's Project window if it doesn't exist: right-click `Veilwalkers` → **Create → Folder** → name it `Art`). Any small image works as a placeholder.
2. Select each imported image in the Project window. In the Inspector, set **Texture Type = Sprite (2D and UI)** → click **Apply**. (If you used the URP/2D template they may already import as sprites.)

> No art handy? Any PNG/JPG works for MVP — the validation only requires the reference to be non-null, not "final art." Flag real art as a later polish pass.

## Step 2 — Create the MonsterDefinition assets (×3–5)

For each monster:

1. In the Project window, open `Assets/Veilwalkers/ScriptableObjects/` (this is where `EconomyConfig.asset` already lives — same asset home).
2. Right-click → **Create → Veilwalkers → Monster Definition**.
3. **Rename the asset file** to `MonNN_Name` — capital M, two-digit number, underscore, PascalCase name, e.g. `Mon01_AntleredShade`. (This `<Id>_<Name>.asset` convention is what the on-disk audit will check.)
4. Select it and fill the Inspector fields:

   | Inspector field | What to enter | Rule |
   |---|---|---|
   | **Id** | `mon01`, `mon02`, … | lowercase `mon` + two digits, **01–67**, unique per asset (never reuse) |
   | **Display Name** | e.g. `Antlered Shade` | non-empty |
   | **Rarity** | dropdown: Common / Uncommon / Rare / Epic / Nightmare | **≥1 asset must be Rare or higher** |
   | **Capture Difficulty** | any int, e.g. `5` | provisional (OQ-9); no bound yet |
   | **Slay Difficulty** | any int, e.g. `7` | provisional (OQ-9) |
   | **Lore** | a sentence of wry-eerie flavor | non-empty |
   | **Art** | drag a Step-1 sprite into the slot | **non-null** |

> Keep the asset filename's `MonNN` matching the **Id** field, and the `_Name` matching the **Display Name** (spaces removed). e.g. file `Mon01_AntleredShade.asset` ↔ Id `mon01`, Display Name `Antlered Shade`.

**Suggested MVP set (5 monsters, spread across tiers, ≥1 Rare+):**

| File | Id | Display Name | Rarity |
|---|---|---|---|
| `Mon01_AntleredShade.asset` | `mon01` | Antlered Shade | Common |
| `Mon02_HollowChoir.asset` | `mon02` | Hollow Choir | Uncommon |
| `Mon03_VeilCrawler.asset` | `mon03` | Veil Crawler | **Rare** ✅ |
| `Mon04_GraveLantern.asset` | `mon04` | Grave Lantern | Epic |
| `Mon05_NightmareMaw.asset` | `mon05` | Nightmare Maw | Nightmare |

(3 is the minimum; 5 gives a nicer Codex sample. As long as one is Rare/Epic/Nightmare you're valid.)

## Step 3 — Create the MonsterDatabase asset

1. In `Assets/Veilwalkers/ScriptableObjects/`, right-click → **Create → Veilwalkers → Monster Database**.
2. Leave its filename `MonsterDatabase.asset`.
3. Select it. In the Inspector, expand **Monsters**, set its size to your count (3–5), and drag each `MonNN_*` definition into a slot.

## Step 4 — Sanity check in the Editor

- No console errors on save.
- Each `MonNN_*` asset's filename matches its Id/Name.
- The `MonsterDatabase` Monsters list has 3–5 entries, no empty slots, at least one Rare+.

## Step 5 — Hand back to me

Close the Editor (so the headless test run isn't locked) and tell me **"2.2 assets are authored."** I'll then (all headless, no Editor needed):
- add the on-disk audit test (`AssetDatabase`-load the `MonsterDatabase.asset`, assert `Validate()` returns empty, filenames match `<Id>_<Name>`, `PopulatedCount ∈ [3..5]`, ≥1 Rare+);
- delete Story 2.1's old tautological `Asset_file_name_convention_is_Id_underscore_Name` test (its real replacement now exists);
- run the headless gate, update `deferred-work.md` (close the AC-4 on-disk-audit item), and commit.

## Notes
- **Commit the `.asset` + `.meta` files** — they're static design data, tracked in git (not gitignored). If you author via the Editor, Unity generates the `.meta`s automatically; I'll stage them when I commit.
- The art images also get committed (placeholders are fine to commit; swap for real art later).
