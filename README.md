# Dragonwilds Increased Dragon Wolf Spawns

This experimental server mod triples Dragon Wolves at the game's existing spawn locations. It increases 20 dynamic wolf groups in 19 spawn tables and adds two nearby points beside each of 37 fixed wolf points across 15 world cells. The game's encounter chance, night conditions, power levels, drops, and respawn timers remain as authored. Native population limits can reduce the number visible at once.

The mod is a UE 5.6 IoStore pak trio, so it does not need UE4SS or RuneSchema. There are separate Linux server and Windows builds because their cooked asset formats differ. The assets were taken from Linux server build **25465077** and Windows client build **25466454** (Dragonwilds 1.0). Both archives pass pack verification and asset readback. The updated Linux pak mounted and loaded an existing dedicated-server world on 28 September 2026. Its visible wolf count has not yet been checked in game.

## Install

1. Stop the server and back up its world save.
2. Extract the **LinuxServer** ZIP for a Linux dedicated server, or the **Windows** ZIP for a Windows server. Put all three `DragonWolfSpawns_P` files (`.pak`, `.utoc`, `.ucas`) directly in `RSDragonwilds/Content/Paks/~mods/` on the server. Create `~mods` if necessary.
3. Start the server. Check its logs for the pak being mounted, then visit a known Dragon Wolf area and compare group sizes over several encounters.

The mod replaces 19 data tables and 15 world cells, so another mod replacing any of those assets will conflict. Remove all three files to uninstall. Rebuild and retest after a game update that changes those assets.

## Build and release

Install [retoc v0.1.5](https://github.com/trumank/retoc/releases/tag/v0.1.5) and run `python3 build.py`. The ZIP is written to `dist/` and is versioned from the first numbered heading in [CHANGELOG.md](CHANGELOG.md). The GitHub workflow builds on pull requests and pushes to `main`. A push to `main` publishes that changelog version as a GitHub release if its tag has not already been released. Bump the newest numbered heading for the next release; a manual workflow run on `main` can retry a failed publication.

`source/linux/` and `source/windows/` each contain 19 edited cooked data tables and 15 edited world cells. The fixed points were duplicated with the small [fixed-spawn tool](fixed_spawns/Program.cs) and packed with retoc. The assets are Jagex game content; this is a free community mod and is not affiliated with Jagex.

## Verification still needed

The Linux server loaded the updated pak and existing world without mod-specific asset errors. The earlier dynamic-only build did not increase wolves at a known Dragon's Run spot; that fixed-spawn spot now has two additional points per original in the package. An in-game count is still needed there. The Windows dedicated-server package has not been run.
