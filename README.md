# Dragonwilds Increased Dragon Wolf Spawns

This experimental server mod triples Dragon Wolves at the game's existing spawn locations. It increases 20 dynamic wolf groups in 19 spawn tables and adds two nearby points beside each of 37 fixed wolf points across 15 world cells. The game's encounter chance, night conditions, power levels, drops, and respawn timers remain as authored. Native population limits can reduce the number visible at once.

It also adds 15 corpse cotton plants to the graveyard near Bleakfields Valley, bringing it from 5 to 20. They are spread over open graveyard floor between the graves, placed on ground heights read from the game's baked navmesh. Each copy uses the original plant's harvest and respawn behaviour. They are in world cell `58S1H0LFS081MPH4I8XX6PTN2`, and the spots are listed in the [corpse cotton tool](corpse_cotton/Program.cs).

The mod is a UE 5.6 IoStore pak trio, so it does not need UE4SS or RuneSchema. There are separate Linux server and Windows builds because their cooked asset formats differ. The assets were taken from Linux server build **25465077** and Windows client build **25466454** (Dragonwilds 1.0). They were rebuilt from Linux server build **25501739** and Windows client build **25492068** (game version 245400) and came out byte for byte the same, so the mod works on both. Both archives pass pack verification and asset readback. The updated Linux pak mounted and loaded an existing dedicated-server world on 28 September 2026. Its visible wolf count has not yet been checked in game.

Enemies that drop undead bones drop three times as many, and ectoplasm drops come three at a time. Drop chances are unchanged. This edits the enemy loot table (`DT_LootDropTable`) in both builds; the affected rows are listed by the [loot drop tool](loot_drops/Program.cs).

## Install

1. Stop the server and back up its world save.
2. Extract the **LinuxServer** ZIP for a Linux dedicated server, or the **Windows** ZIP for a Windows server. Put all three `DragonWolfSpawns_P` files (`.pak`, `.utoc`, `.ucas`) directly in `RSDragonwilds/Content/Paks/~mods/` on the server. Create `~mods` if necessary.
3. Start the server. Check its logs for the pak being mounted, then visit a known Dragon Wolf area and compare group sizes over several encounters.

The mod replaces 19 data tables and 16 world cells, plus the enemy loot table, so another mod replacing any of those assets will conflict. Remove all three files to uninstall. Rebuild and retest after a game update that changes those assets.

## Build and release

Install [retoc v0.1.5](https://github.com/trumank/retoc/releases/tag/v0.1.5) and run `python3 build.py`. The ZIP is written to `dist/` and is versioned from the first numbered heading in [CHANGELOG.md](CHANGELOG.md). The GitHub workflow builds on pull requests and pushes to `main`. A push to `main` publishes that changelog version as a GitHub release if its tag has not already been released. Bump the newest numbered heading for the next release; a manual workflow run on `main` can retry a failed publication.

`source/linux/` and `source/windows/` each contain 20 edited cooked data tables and 16 edited world cells. The Dragon Wolf groups were enlarged with the [spawn table tool](spawn_tables/Program.cs), the fixed points were duplicated with the small [fixed-spawn tool](fixed_spawns/Program.cs), the graveyard plants were added with the [corpse cotton tool](corpse_cotton/Program.cs), and the loot table was edited with the [loot drop tool](loot_drops/Program.cs). The Windows loot table is unversioned, so that tool adds its newer `Recipes` field to the mappings, then requires the table to reserialize unchanged and match the Linux rows before editing it. The corpse cotton tool clones an upright original plant with all of its components. It reads both platforms' copies of the cell, and checks the byte-patched Windows references against the parsed Linux ones. The tools need a `.usmap` mappings file for the current game version; UE4SS's `DumpUSMAP()` Lua function writes one from a running Windows client. Everything is packed with retoc. The assets are Jagex game content; this is a free community mod and is not affiliated with Jagex.

## Verification still needed

The Linux server loaded the updated pak and existing world without mod-specific asset errors. The earlier dynamic-only build did not increase wolves at a known Dragon's Run spot; that fixed-spawn spot now has two additional points per original in the package. An in-game count is still needed there. The Windows dedicated-server package has not been run. The graveyard corpse cotton has not been seen in game, and the tripled bone and ectoplasm drops have not been checked in game. The plants are level actors, not spawns, so players whose clients lack the pak may not see or harvest them even though the server has them.
