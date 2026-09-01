# macOS / CrossOver Wine Workaround — running locally built mods

## Symptom

Locally built Pugstorm SDK mods that ship `Scripts/*.cs` source files (the default
Pugstorm pattern) fail to load on Core Keeper running under CrossOver on macOS, with one
of two errors:

1. Game shows a dialog: `Ein Mod konnte nicht geladen werden: DisableDurability
   (Kompilierung fehlgeschlagen)`
2. Player.log contains:
   ```
   IOException: Unknown error : '\\?\C:\users\crossover\AppData\Local\Temp\Pugstorm\Core Keeper\ModLoader\<ModName>\Scripts'
     at System.IO.FileSystem.RemoveDirectoryRecursive
     at PugMod.Loader.Load
   ```

The mod's static constructors never fire; `[ModName] Mod initialized` never appears in the log.

## Root cause

Pugstorm's mod loader follows this pipeline for source-bearing mods:

1. Read `ModManifest.json` from the mod folder (game finds the manifest fine)
2. Extract `Scripts/` and `Bundles/` into `~/Library/Application
   Support/CrossOver/Bottles/Core
   Keeper/drive_c/users/crossover/AppData/Local/Temp/Pugstorm/Core
   Keeper/ModLoader/<ModName>/`
3. Compile the extracted scripts with `RoslynCSharp` (sandboxed)
4. Load the resulting assembly and fire `IMod.Init()` + `[HarmonyPatch]` static constructors

The extraction step in (2) does `RemoveDirectoryRecursive` first to clean up any prior
cache. On the macOS-CrossOver Wine layer this call passes a Windows long-path prefix
(`\\?\C:\...`) and **fails with an unspecified IOException**, even when the target
doesn't exist or is empty. Wine's `RemoveDirectoryRecursive` implementation does not
handle that prefix reliably in this codepath.

Once the cleanup throws, the loader aborts the load for that mod. The dialog the user
sees is misleading — there is no actual C# compile error; the failure happens before the
compile.

## Why mod.io-installed mods are immune

Mods downloaded via the in-game mod browser land in `~/Library/Application
Support/CrossOver/Bottles/Core
Keeper/drive_c/users/Public/mod.io/<game_id>/mods/<mod_id>_<modfile_id>/`. The loader
treats those differently:

- The ZIP is cached at `~/Library/Application Support/CrossOver/Bottles/Core
  Keeper/drive_c/users/crossover/AppData/Local/Temp/Pugstorm/Core
  Keeper/<game_id>/<mod_id>_<modfile_id>.zip`.
- The extracted folder under `mod.io/.../mods/<mod_id>_<modfile_id>/` is read directly.
  The loader does not re-extract into the `ModLoader/` temp directory for those mods, so
  the Wine-failing code path is never executed.

A side-effect of this codepath is the loader expects the cached ZIP to match a real
mod.io API entry. The mod.io client runs an API sync that compares the local
`state.json` and ZIP cache against what mod.io's backend reports for the current
account.

## The workaround — fake mod.io entry

Treat the locally built mod as if it had been downloaded from mod.io, by populating the
same three locations a real mod.io install would have. Use a numeric mod ID that is not
in mod.io's actual catalog (e.g. `9999999`).

### Locations to populate

| Path | Content |
|---|---|
| `mod.io/<game_id>/mods/<mod_id>_<modfile_id>/` | Extracted mod folder: `ModManifest.json`, `Scripts/`, `Bundles/` |
| `<TEMP>/Pugstorm/Core Keeper/<game_id>/<mod_id>_<modfile_id>.zip` | ZIP of the same files (top-level entries, no wrapping folder) |
| `mod.io/<game_id>/state.json` | Add `<mod_id>` to `existingUsers.<userId>.subscribedMods` and add a minimal stub to `mods["<mod_id>"]` |

`<TEMP>` resolves to `~/Library/Application Support/CrossOver/Bottles/Core
Keeper/drive_c/users/crossover/AppData/Local/Temp` on macOS with a bottle named "Core
Keeper".

`<game_id>` is `5289` for Core Keeper.

### Minimal state.json stub for one mod entry

```jsonc
{
  "currentModfile": {
    "id": <modfile_id>,
    "mod_id": <mod_id>,
    "version": "1.0.0",
    "filename": "<modname>.zip"
  },
  "modObject": {
    "id": <mod_id>,
    "game_id": 5289,
    "status": 1,
    "visible": 1,
    "name": "<ModName>",
    "name_id": "<lowercase-slug>",
    "summary": "<short description>",
    "modfile": { "id": <modfile_id>, "mod_id": <mod_id> }
  }
}
```

The loader checks `status: 1` (live) and `visible: 1` (visible). Other fields can be
omitted from the stub; the loader fills in defaults or ignores absent keys.

### ZIP structure

The ZIP must have the mod's top-level entries at the archive root — no wrapping folder.
Verify with `unzip -l <zip>`:

```
Bundles/
Bundles/<ModName>_Windows.assetbundle
Bundles/<ModName>_Linux.assetbundle
ModManifest.json
Scripts/
Scripts/<ModName>Mod.cs
Scripts/...
```

To produce this from a folder built by the SDK:

```bash
cd <BuiltMods>/<ModName>
zip -r <TEMP>/Pugstorm/Core\ Keeper/<game_id>/<mod_id>_<modfile_id>.zip Bundles Scripts ModManifest.json
```

The two integer suffixes `<mod_id>_<modfile_id>` must be identical between the zip
filename and the extracted folder name. They also must match the values in `state.json`.

## Operational rule — do not open the in-game Mod menu

The fake entry is only stable if the mod.io client does not run an API sync. The sync is
triggered when the user opens the in-game Mod menu (the menu that lists installed mods
and lets you browse for more). When the sync runs, it queries mod.io for the subscribed
mods, discovers that `9999999` does not exist server-side, and removes the local files +
ZIP. The next game start has no mod folder, no ZIP, and the loader cannot load the mod.

Game start, world load, and gameplay do not trigger the sync. Settings, controls, and
the other top-level menus are safe. Only the Mod menu is the trigger.

If the menu is opened and the fake entry is wiped, re-run the install script (below) to
restore the three locations and the state.json patch. Starting the game without opening
the Mod menu picks the mod back up.

## Sandbox restriction — no System.IO

Pugstorm's mod loader compiles `Scripts/*.cs` with the `RoslynCSharp` runtime compiler
under a default-deny sandbox. Common BCL surface that triggers compile-failure on first
reference:

- `System.IO.File.{Exists, ReadAllText, WriteAllText, ...}`
- `System.IO.Path.{Combine, GetDirectoryName, ...}`
- `System.IO.Directory.*`
- Likely also `System.Diagnostics.Process`, reflection-emit, and others

The compile error in the log reads:

```
Referenced in method body: '<Type>.<Method>()'
  at instruction: 'IL_XXXX: Call ... System.IO.File::ReadAllText(System.String)'
  ...
mod <ModName> load error: CompileFailed
```

Two ways to live with this:

- **Avoid the restricted APIs.** For configuration that would normally live in
  `config.json`, hardcode defaults in the mod and ship without a runtime config-file.
  The mod can still expose a `bool Enabled` API for future use; consumers (patches) read
  it the same way regardless of where the value comes from.
- **Set `skipSafetyChecks: true` in the `ModManifest.json`.** This disables the sandbox
  and gives the mod full BCL access. The trade-off is whatever Pugstorm's safety checks
  were guarding against; for personal-use mods on your own machine this is acceptable.
  CoreLib does this — see its manifest if you want to confirm.

## Disabling conflicting mods

CoreLib's cache structure (deeply nested `Scripts/<Module>/Scripts/<Submodule>/...`)
hits the same Wine bug when extracted fresh, but its long-stale cache from previous runs
is sometimes accepted by the loader. When iterating on a new mod alongside CoreLib,
**CoreLib's load can crash the loader before it gets to your mod**. Symptoms:

- Both `loaded mod CoreLib` and `loaded mod <YourMod>` appear in the log
- Then an `IOException` on a CoreLib path
- Main menu shows `MODS (0 geladen)`

In this state none of the subscribed mods actually run, because the loader aborts the whole pass.

Workaround: add CoreLib to `disabledMods` in `state.json` while developing. Re-enable when you ship.

## End-to-end install script

The companion script `scripts/install-macos.sh` automates the workaround. It is
idempotent — re-running it overwrites the fake entry with the current build and
re-applies the state.json patch.

Workflow per iteration:

```bash
# 1. Build the mod via Unity (must be CLOSED — Unity locks the project)
./scripts/build.sh

# 2. Install into fake mod.io slot + clean ModLoader cache
./scripts/install-macos.sh

# 3. Launch Core Keeper. DO NOT open the Mod menu.
#    Load a world, test the mod.
```

The script requires the same `.envrc` as `build.sh` (`SDK_PATH`, `MOD_INSTALL_PATH`) and
assumes Core Keeper's bottle is named `Core Keeper` with default Steam install path
inside.

## What we ruled out before this worked

- **Pre-populating the `ModLoader/<ModName>/Scripts/` cache** — the loader still calls
  `RemoveDirectoryRecursive` before extracting. The pre-populated files are not detected
  as "already extracted"; they get clobbered and the Wine bug fires anyway.
- **Stripping macOS extended attributes (`xattr -rc`) from the cache** — does not affect
  the Wine error; CoreLib's working caches still have `@` xattrs from macOS Finder.
- **Marking cache files read-only** — not tested in depth, but the IOException pattern
  suggests it would not change behavior.
- **Installing into `<GameDir>/CoreKeeper_Data/StreamingAssets/Mods/`** — this is the
  SDK's "Create Mod" default destination, but it forces the cache-extract code path and
  hits the Wine bug for every fresh mod.

## Upstream fix candidates

- **Wine / CrossOver**: fix `RemoveDirectoryRecursive` on `\\?\C:\` prefixed paths. Out
  of our hands.
- **Pugstorm**: offer a "ship pre-compiled DLL" mode in `ModBuilderSettings` that skips
  the source-extract step, or a flag to skip the recursive cleanup before extract. Worth
  filing as a GitHub issue against `Pugstorm/CoreKeeperModSDK`.

Until either lands, the fake-mod.io workaround is the practical path for macOS-CrossOver
mod developers.
