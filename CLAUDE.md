# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Core Keeper mod that disables item durability loss by Harmony-patching `PlayerEquipment.ChangeDurabilitySystem.OnUpdate` to skip the original. The skip is gated by a live in-game **Enabled** toggle exposed through the Mod Settings Menu framework (which pulls in CoreLib); both are required runtime dependencies. Built against Pugstorm's official `CoreKeeperModSDK`. Single-target, personal-use, non-commercial (Pugstorm EULA).

## Build and deploy

```bash
source .envrc           # exports UNITY_BIN, SDK_PATH, MOD_INSTALL_PATH, MOD_NAME, …
../utils/build.sh      # Unity batchmode build; on Darwin auto-runs install-macos.sh
```

Unity Editor must be closed (it locks the project). The build takes ~90 s on a warm machine. `utils/install-macos.sh` is the macOS-specific deploy step; opt out with `SKIP_MACOS_INSTALL=1`.

`utils/link.sh` symlinks the repo's `unity/` mirror into `$SDK_PATH/Assets/`: one **directory** symlink for `unity/DisableDurability/`, plus three file symlinks for the Assets-level files beside it (`DisableDurability.asset`, `.asset.meta`, `.meta`). `build.sh` invokes it idempotently on every run, so worktree switches and repo moves self-heal without manual intervention. Run `link.sh` standalone only when iterating on the SDK side outside of `build.sh` (e.g. opening the SDK in Unity Editor and wanting fresh symlinks first).

There are no automated tests. Verification is manual; the gameplay smoke test is "load a world, mine 30 blocks with a pickaxe, durability bar stays put."

## Publishing to mod.io

`../utils/upload.sh` publishes this mod. It runs the shared Editor class
`CoreKeeperModUtils.CLIPublishHelper.Publish` (symlinked in from
`../utils/`, alongside `CLIBuildHelper`) via Unity batchmode. The
publish reads `MOD_REPO_ROOT` (set in `.envrc`) to locate `CHANGELOG.md`.

- `Editor/DisableDurability.Editor.asmdef` references the mod.io plugin DLL
  via `overrideReferences: true` + `precompiledReferences:
  ["modio.UnityPlugin.dll"]`.
- The published version comes from the topmost `## [x.y.z]` entry of
  `CHANGELOG.md`; bump it before publishing.
- The profile logo is `unity/DisableDurability/Editor/logo.png` (readable,
  uncompressed; min 512×288).
- The real mod ID lives in
  `unity/DisableDurability/Editor/DisableDurability_modio.asset`.
- One-time: log in via the SDK window's "Log in" tab before the first
  publish.

## Required external setup

- **Unity Editor `6000.0.59f2`** (exact patch version — pinned in the SDK's `ProjectVersion.txt`, not the SDK's `README.md` which is one patch behind). Add the **Linux Build Support (Mono)** and, on macOS, **Windows Build Support (Mono)** modules via Unity Hub.
- **`Pugstorm/CoreKeeperModSDK`** cloned as a sibling at `$SDK_PATH`. The wizard's "Create New Mod" + "Update Game Files" + manifest settings (`requiredOn=ClientAndServer`) must have been done once.
- **`jq`** and standard macOS userland for `utils/install-macos.sh`.
- **Runtime mod dependencies** — **CoreLib** and **Mod Settings Menu**, both declared `required` in the ModBuilderSettings `.asset` (`dependencies:` block) and needed at runtime for the in-game Enabled toggle. mod.io pulls both in for subscribers; a local dev install must have them present too.

## Architecture

Three runtime classes in the `DisableDurability` namespace, plus the shared editor helpers symlinked in from `../utils/`:

- **`DisableDurabilityMod` (`IMod`)** — bootstrap. `Init()` does three things: `BurstDisabler.DisableBurstForSystem<ChangeDurabilitySystem>()` (Burst-compiled `OnUpdate` methods are not patchable by Harmony; this moves the system out of Burst so the Prefix can intercept), a **manual `BurstDisabler.AddWorld` pass over `World.All`**, and it declares the Mod Settings Menu section — `ModSettings.Section(this).Toggle(out var en, "enabled", true).Build()` — then hands the resulting handle to `ModConfig.Instance.Bind(en)`.

  The `AddWorld` pass is what makes the mod work on a **dedicated server**. Registering the system only arms the Burst bypass for worlds `BurstDisabler.AddWorld` has already seen, and its sole caller is `ECSManager.StartEcs`, which snapshots the systems registered up to that moment. A dedicated server runs `IMod.Init()` *after* `StartEcs`, so that snapshot is taken while the registration is still missing: `OnUpdate` keeps running through the Burst path, the Prefix is never reached, and the server reduces durability authoritatively — its ghost snapshot then overwrites the client's correctly patched prediction, which is why the bar dropped in multiplayer while singleplayer was fine (there `Init()` runs before `StartEcs`). Re-running `AddWorld` over the existing worlds closes that gap; the registry is a set, so it is a no-op in the singleplayer/client ordering. Moving the call to `EarlyInit()` does **not** work — `TypeManager` is not initialised that early and `DisableBurstForSystem` throws `NullReferenceException`.
- **`NoDurabilityLossPatch` (`[HarmonyPatch]`)** — `Prefix` returning `false` skips `ChangeDurabilitySystem.OnUpdate`, which also prevents its two scheduled jobs (`ChangeDurabilityOfHeldEquipmentJob`, `ReduceDurabilityOfAllEquipmentJob`) from running. `[HarmonyPriority(Priority.Last)]` is a defensive default for coexistence with other durability mods.
- **`ModConfig`** — thin adapter over the Mod Settings Menu handle. `enabled` reads the live `SettingHandle<bool>.Value` bound in `Init()`, falling back to the default `true` only in the brief pre-`Bind` window at load. The singleton getter is source-compatible (field → property), so `NoDurabilityLossPatch` still reads `ModConfig.Instance.enabled` unchanged. The framework persists the value via CoreLib, so the mod's own code touches no `System.IO` — which Pugstorm's RoslynCSharp sandbox blocks at runtime. See `docs/research/macos-crossover-wine-workaround.md` for the sandbox details.
- **Shared editor helpers** (`../utils/CLIBuildHelper.cs`, `CLIPublishHelper.cs`, `LocalizationGenerator.cs`, namespace `CoreKeeperModUtils`) — `CLIBuildHelper` wraps `PugMod.ModBuilder.BuildMod(...)` and `CLIPublishHelper` drives the mod.io publish, both for `unity -batchmode -executeMethod`. They are **not** vendored: `utils/link.sh` symlinks them into `unity/DisableDurability/Editor/`, so they compile into the editor-only `DisableDurability.Editor` asmdef (a combined runtime+editor asmdef cannot reference editor-only types like `ModBuilder`/`ModBuilderSettings`). Mod identity comes from `MOD_NAME` in `.envrc`, so one source serves every mod. `LocalizationGenerator` compiles the mod's `localization.yaml` (the Mod Settings Menu section hint plus the `enabled` toggle label, EN/DE) into the generated localization assets under `Localization/Generated/`. The `.cs` symlinks and their Unity-generated `.meta` are gitignored (nothing references them by GUID).

`unity/` is the canonical source — a 1:1 mirror of the SDK's `Assets/` tree holding **every** file the Unity Editor generates for the mod: the `.cs` sources, both `.asmdef` files, the ModBuilderSettings `.asset`, and all `.meta` files (GUID carriers — versioned per Unity convention). The SDK clone's `Assets/DisableDurability` is a **directory symlink** into `unity/DisableDurability/` (created by `utils/link.sh`); because it is a directory symlink, any file the Editor adds later is captured automatically — nothing needs wiring up in `link.sh` by hand. Edit in `unity/`; the SDK picks up the change on the next refresh.

Patch target was identified by inspecting PermaBreak's source under `mod.io/.../5289/mods/3407304_4384035/Scripts/` (a production mod that does the inverse). PermaBreak's published source references a method (`PlayerController.ReduceDurabilityOfEquipment`) that no longer exists in the current game build; the working target is `PlayerEquipment.ChangeDurabilitySystem.OnUpdate` from `Pug.Other.dll`. The full reasoning is in `docs/research/permabreak-patch-target.md`.

## macOS / CrossOver — critical operational rules

Pugstorm's loader extracts `Scripts/` from locally built mods into `~/Library/Application Support/CrossOver/Bottles/Core Keeper/drive_c/users/crossover/AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/<ModName>/`, and Wine's `RemoveDirectoryRecursive` on the `\\?\C:\` long-path prefix fails with an unspecified IOException. The loader then reports "compilation failed" even though no compile has run.

The workaround — implemented by `utils/install-macos.sh` — routes the mod through mod.io's load path instead (which doesn't go through `ModLoader/`). It populates three locations:

1. `mod.io/5289/mods/9999999_1/` — extracted files (mod ID `9999999` is a fake; mod.io's real catalog never has it)
2. `<bottle>/users/crossover/AppData/Local/Temp/Pugstorm/Core Keeper/5289/9999999_1.zip` — ZIP cache the loader expects
3. `mod.io/5289/state.json` — `subscribedMods += "9999999"` plus a minimal `mods["9999999"]` stub

**Do not open the in-game Mods menu while a fake-ID mod is installed.** Opening it triggers a mod.io API sync that resolves `9999999` against the real mod.io catalog, finds it doesn't exist, and **deletes the local files and the cached ZIP**. The fix is to re-run `../utils/build.sh` (which re-populates the three locations idempotently). Game start, world load, and gameplay do not trigger the sync; only the mod browser does.

CoreLib triggers the same Wine bug when its cache is fresh — its cache structure is deep enough to hit the failure mode reliably. As of 1.1.0 CoreLib and Mod Settings Menu are hard runtime dependencies (they back the in-game Enabled toggle), so they can no longer be parked in `disabledMods` while iterating — beware that any cache-cleaning event can drop CoreLib back into the compilation-failed state.

The full background, including what was ruled out and upstream fix candidates, is in `docs/research/macos-crossover-wine-workaround.md`. The other research notes (`macos-sdk-steamworks-fix.md`, `permabreak-patch-target.md`) cover one-time fixes already applied; they are mostly relevant if a fresh SDK clone is set up.

## Conventions

- Commit messages: short subject, imperative, no emoji. Wrap body at ~75 chars.
- Documentation files under `docs/` are English (per the user's global instructions); chat answers are German.
- The user prefers `git commit --amend` and `git reset --soft` over fix-up commits while a change is in progress on a personal branch. For shared/pushed branches, ask first.
- The user prefers `git rebase` over `git merge` for integrating branches when there are real divergent commits. The worktree branch produced by `superpowers:using-git-worktrees` is normally fast-forwardable into `main`.
- macOS-specific: GNU `sed`/`find`/`xargs` are on `PATH` (Homebrew). `pgrep -P` and `pgrep -x` are unreliable under proctools — prefer `pgrep -f`. `eza` may shadow `ls`; use `/bin/ls` if column flags misbehave.

## When picking up work later

1. `source .envrc` — confirm UNITY_BIN, SDK_PATH, MOD_INSTALL_PATH still resolve.
2. Check `git log --oneline | head -5` and `git status` — the project is on `main` with the worktree removed; new work should create a new worktree via `superpowers:using-git-worktrees`.
3. If anything in `mod.io/5289/mods/9999999_1/` or the cached ZIP is missing (e.g. after the user opened the Mods menu), `../utils/build.sh` restores all three locations.
