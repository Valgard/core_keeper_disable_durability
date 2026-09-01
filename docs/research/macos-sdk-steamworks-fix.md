# macOS SDK fix — Steamworks namespace compile errors

## Symptom

Opening the freshly cloned `Pugstorm/CoreKeeperModSDK` (commit pinned to Unity
`6000.0.59f2`) on a macOS Editor produces a "compilation errors / Enter Safe Mode"
dialog. Console shows:

```
Packages/dev.pugstorm.mod/SDK/Editor/ModSDKWindow/ModSDKWindow.cs(5,7): error CS0246: The type or namespace name 'Steamworks' could not be found
Packages/dev.pugstorm.mod/SDK/Editor/ModSDKWindow/SteamWorkshopTab.cs(5,7): error CS0246: The type or namespace name 'Steamworks' could not be found
```

## Cause

The SDK ships two Facepunch.Steamworks DLLs at `Assets/Plugins/CoreKeeperModSDK/`:

- `Facepunch.Steamworks.Win64.dll` — meta sets `Editor.OS: Windows`,
  `OSXUniversal.enabled: 0`. Only loads in the Windows Editor.
- `Facepunch.Steamworks.Posix.dll` — meta sets `Editor.OS: Linux`,
  `OSXUniversal.enabled: 0`. Only loads in the Linux Editor.

On a macOS Editor host, neither DLL loads, so the `Steamworks` namespace is unresolved
at compile time. The two `.cs` files in
`Packages/dev.pugstorm.mod/SDK/Editor/ModSDKWindow/` that contain `using Steamworks;`
therefore fail to compile, blocking the rest of the SDK from initialising.

The Steamworks references are confined to the Editor's Steam Workshop upload feature.
They are not part of the mod runtime. Pugstorm appears to have tested only Windows and
Linux developer setups; macOS is a hole.

## Fix applied

Edit `Assets/Plugins/CoreKeeperModSDK/Facepunch.Steamworks.Posix.dll.meta` to enable the
DLL on macOS host (Editor and Standalone). Posix is the right DLL to enable because
macOS is a Posix system.

Four single-value YAML changes:

```diff
-        Exclude OSXUniversal: 1
+        Exclude OSXUniversal: 0
…
-        OS: Linux
+        OS: AnyOS
…
-      enabled: 0
+      enabled: 1
…
-        CPU: None
+        CPU: AnyCPU
```

After the edit:

- `Any.Exclude OSXUniversal: 0` — the catch-all permission no longer excludes macOS.
- `Editor.OS: AnyOS` — the DLL loads in any Editor host, including macOS.
- `OSXUniversal.enabled: 1` with `CPU: AnyCPU` — explicit per-platform permission for
  macOS Standalone targets (not strictly needed for compile but matches Unity's expected
  platform-data shape).

A copy of the original meta is preserved alongside as `*.macos-backup`.

## Effect

The Posix DLL is a managed assembly; the `Steamworks` namespace and types resolve at
compile time on macOS. Runtime invocation of Facepunch.Steamworks calls would still fail
(no `libsteam_api.dylib` available in the bottle), but this never matters in our
workflow because:

1. We never trigger the Steam Workshop upload tab in the Editor — we use mod.io for distribution.
2. The runtime mod (our `.dll` that ships in the mod folder) contains no Steamworks references.

## Reproducibility

To re-apply on a fresh SDK clone (e.g., after `rm -rf CoreKeeperModSDK && git clone …`):

```bash
META="$SDK_PATH/Assets/Plugins/CoreKeeperModSDK/Facepunch.Steamworks.Posix.dll.meta"
cp -n "$META" "$META.macos-backup"
sed -i.bak \
  -e 's/Exclude OSXUniversal: 1/Exclude OSXUniversal: 0/' \
  -e 's/OS: Linux$/OS: AnyOS/' \
  -e 's/^      enabled: 0$/      enabled: 1/' \
  -e 's/^        CPU: None$/        CPU: AnyCPU/' \
  "$META"
```

After editing the meta, close Unity, remove `$SDK_PATH/Library/` to force a clean
re-import, then re-open the project. The compile errors should be gone.

## Upstream

Worth filing as a Pugstorm GitHub issue or pull request — enabling the Posix DLL for
`OS: AnyOS` is a one-line upstream fix that would unblock macOS modders out of the box.
