# Disable Durability

A small Core Keeper mod that prevents item durability from decreasing when items are used. Built on the official Pugstorm `CoreKeeperModSDK`.

## What it does

While the mod is enabled, the game skips the durability-decrement code path for items in use. The durability bar stays at whatever value the item had when the mod loaded — items at 100/100 stay full; items at 30/100 stay at 30/100.

**This is not a repair mod.** Items keep their current durability — they do not regenerate to maximum.

## Requirements

- Core Keeper (Steam, PC build)
- **Mod Settings Menu** (required) — provides the in-game settings screen
- **CoreLib** (required) — a dependency of Mod Settings Menu
- For multiplayer: install on both client and server.

## Configuration

The mod exposes one setting in-game, under **Options → Mod settings**:

| Setting | Default | Effect |
|---------|---------|--------|
| Enabled | On | Master switch. Turn it off and item durability decreases exactly as in vanilla; turn it back on to freeze durability again. Applies live — no restart. |

## Build (developer)

See `CLAUDE.md` for the build and deploy procedure.

## License

Personal-use, non-commercial — Pugstorm Core Keeper EULA. Built against the
official `CoreKeeperModSDK`. Source on GitHub; contributions welcome.
