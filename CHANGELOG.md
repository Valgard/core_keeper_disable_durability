# Changelog

All notable changes to this mod are documented in this file. The format is
loosely based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
without strict adherence — entries describe what shipped per release, not
every commit. The topmost `## [x.y.z]` entry is the current published version.

## [1.1.1]

### Fixed

- **Durability still dropped when playing on a dedicated server.** The mod
  worked in single-player and when hosting, but had no effect after joining a
  dedicated server: its patch never became active on the server side, and the
  server's authoritative durability value overwrote what your own game had
  already corrected. Both sides now apply it. Update the mod on the server as
  well — the fix has to run there.

## [1.1.0]

### Added

- **In-game Enabled toggle.** Switch the mod on or off from **Options → Mod
  Settings** without uninstalling it. When off, item durability drops again
  exactly like vanilla. Applies live — no restart.

### Changed

- New required dependencies: **Mod Settings Menu** and **CoreLib**, which host
  the in-game settings screen. mod.io pulls both in when you subscribe.

## [1.0.0]

- Initial release: items never lose durability when used.
