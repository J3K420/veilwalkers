# Story 1.1 — Unity Setup Guide (human-in-the-loop steps)

> **Why this file exists:** Story 1.1 initializes the Unity project, which is an
> interactive **Unity Hub + Unity Editor** task (per CLAUDE.md: "initialized via
> Unity Hub, not by hand-writing project files"). Unity Hub/Editor was **not
> installed** on the dev machine when the dev-story workflow ran, so the editor
> steps below must be done by a human at the machine. This guide gives you the
> exact versions, modules, package pins, and Player Settings values so you can do
> it fast and correctly. Companion reference: `1-1-manifest-reference.json`.
>
> When you've completed the steps, re-run `bmad-dev-story` (or tell Amelia
> "resume story 1-1") and the automatable verification + git commit can finish.

---

## 0. Prerequisites (one-time install)

1. **Install Unity Hub** — https://unity.com/download
2. In Hub → **Installs** → **Install Editor** → choose **Unity 2022.3 LTS**
   (latest **2022.3.x** patch — e.g. 2022.3.62f1 or newer 2022.3.x; do **not**
   pick 2023.x or 6.x — AR Foundation 5 is pinned to 2022.3).
3. When prompted for **modules**, check **all** of these (critical for AC-2):
   - ✅ **Android Build Support**
     - ✅ **Android SDK & NDK Tools**
     - ✅ **OpenJDK**
   - ✅ **IL2CPP** scripting backend (usually bundled with the platform module)

> Without the Android module + IL2CPP you cannot satisfy AC-2 (IL2CPP / ARM64 /
> `.aab`). Verify they're checked before clicking Install.

---

## 1. Task 1 — Create the project (AC-1)

1. Hub → **Projects** → **New project**.
2. Editor version: the **2022.3 LTS** you installed.
3. Template: **3D (URP)**  ← (URP, *not* the AR template, *not* plain 3D).
4. **Project name / location:** create it so the Unity folders land at the repo
   root `C:\Users\james\Desktop\Veilwalkers\`. Two safe ways:
   - **Option A (recommended):** create the project in a temp location, then move
     the generated `Assets/`, `Packages/`, `ProjectSettings/` into the repo root.
   - **Option B:** name the project `Veilwalkers` and set the location to
     `C:\Users\james\Desktop\` so Hub writes into the existing `Veilwalkers\`
     folder. Confirm Hub does **not** wipe the existing `docs/`, `_bmad/`, etc.
     (it won't — it only adds `Assets/`, `Packages/`, `ProjectSettings/`).
5. Open the project. Confirm:
   - **Zero console errors.**
   - URP asset is assigned: **Edit → Project Settings → Graphics** shows a URP
     asset in "Scriptable Render Pipeline Settings", and **Quality** levels
     reference a URP asset.

✅ AC-1 (first half): project opens & compiles clean on 2022.3 LTS from URP template.

---

## 2. Task 2 — Pin AR + IAP packages (AC-1)

Open **Window → Package Manager**. Install/pin these (see
`1-1-manifest-reference.json` for the exact `manifest.json` shape):

| Package | Identifier | Target version |
|---|---|---|
| AR Foundation | `com.unity.xr.arfoundation` | latest **5.x** offered for 2022.3 (e.g. **5.2.2**) |
| ARCore (Android provider) | `com.unity.xr.arcore` | **same version number** as AR Foundation |
| Unity IAP | `com.unity.purchasing` | **5.0.0+** (bundles Google Play Billing **8.0.0**) |
| Newtonsoft JSON | `com.unity.nuget.newtonsoft-json` | latest 2022.3-compatible |

Steps:
1. Package Manager → **Packages: Unity Registry** → search & install AR
   Foundation; pick the latest **5.x** (NOT 6.x).
2. Install **ARCore XR Plugin** (`com.unity.xr.arcore`) at the **same version**.
3. Install **In-App Purchasing** (`com.unity.purchasing`) 5.0.0+. Open
   **Services → In-App Purchasing** if prompted; you do **not** need to wire a
   real product catalog in this story.
4. Install **Newtonsoft Json** (`com.unity.nuget.newtonsoft-json`).
5. Let Unity resolve, then **confirm `Packages/packages-lock.json` was written**
   (it must be committed — it's the reproducible lock).

> **Compliance check:** confirm `com.unity.purchasing` resolves to **5.0.0 or
> later** so the bundled **Google Play Billing Library is 8.0.0** (Google requires
> ≥7.0.0 since 2025-08-30). Older Unity IAP = non-compliant.

---

## 3. Task 3 — Import ARCore Extensions (arf5) (AC-1)

1. Package Manager → **+** → **Add package from git URL**.
2. Paste: `https://github.com/google-ar/arcore-unity-extensions.git#arf5`
   - (Or download the `arf5`-tagged tarball from
     https://github.com/google-ar/arcore-unity-extensions/releases and add it.)
3. Confirm it resolves against your AR Foundation 5 version with **no compile
   errors**.

---

## 4. Task 4 — Android Player Settings (AC-2)

1. **File → Build Settings → Android → Switch Platform.**
2. **Build Settings → check "Build App Bundle (Google Play)"** so output is `.aab`.
3. **Player Settings → Other Settings:**
   - **Scripting Backend = IL2CPP**
   - **Target Architectures = ARM64 only** (uncheck ARMv7).
   - **Minimum API Level = Android 7.0 (API level 24)** (or current ARCore floor).
4. **Project Settings → XR Plug-in Management → Android tab → enable ARCore.**
5. **ARCore settings → Requirement = Required** (AR Required, not Optional).
6. **Do NOT** create or commit a keystore here. Signing is a release-time concern
   (gitignored `*.jks` + `keystore.properties`), reserved by AR-21.

✅ AC-2: Android / AR Required / ARM64 / IL2CPP / Min API 24 / `.aab` output.

---

## 5. Task 5 — Repo hygiene (AC-3) — already verified by Amelia

- `.gitignore` at repo root **already satisfies AC-3** (Library/Temp/Logs, csproj/
  sln, keystores, keystore.properties, google-services.json, secrets; keeps
  `Assets/**/*.meta`). **Verified — no edits needed.**
- After Unity generates folders, confirm **`ProjectSettings/`, `Packages/manifest.json`,
  `Packages/packages-lock.json` are NOT ignored** (`git status` should show them
  as untracked/trackable, not ignored).
- If the URP template dropped its **own** `.gitignore`, **keep the existing curated
  one** — do not let it overwrite the repo-root file.
- Verify **no legacy "Google Play Billing Plugin for Unity"** (`com.google.play.billing`
  / GPBL3) exists under `Assets/` or `Packages/`.

---

## 6. Task 6 — Smoke-verify & commit

1. Reopen the project clean → **zero compile errors, zero console errors.**
2. Run a **debug `.aab` build** (Build Settings → Build), or at minimum confirm
   the pipeline runs to producing an App Bundle. If a full build isn't feasible,
   note exactly how far it got.
3. `git status` sanity:
   - Untracked (should be ignored): `Library/`, `*.csproj`, `*.sln`.
   - Tracked: `ProjectSettings/`, `Packages/manifest.json`,
     `Packages/packages-lock.json`, `Assets/**/*.meta`.
4. Commit + push (per CLAUDE.md). Amelia can do this step once you resume the
   workflow — she'll also tick the task checkboxes and move the story to `review`.

---

## Quick verification checklist (paste results back to Amelia when done)

- [ ] Unity **2022.3.x** LTS, project opens, **0 console errors** (AC-1)
- [ ] `manifest.json`: arfoundation **5.x**, arcore **= same 5.x**, purchasing
      **≥5.0.0**, newtonsoft-json present (AC-1)
- [ ] `packages-lock.json` generated (AC-1)
- [ ] ARCore Extensions (`#arf5`) imported, no errors (AC-1)
- [ ] Android / **AR Required** / **ARM64** / **IL2CPP** / **Min API 24** / **.aab** (AC-2)
- [ ] No legacy Play Billing plugin present (AC-3)
- [ ] `ProjectSettings/` + `Packages/*` tracked, `Library/`/csproj/sln ignored (AC-3)
- [ ] Debug `.aab` build runs (AC-2 end-to-end)
