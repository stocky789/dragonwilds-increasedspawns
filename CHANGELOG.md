# Changelog

All notable changes to **Dragonwilds Increased Dragon Wolf Spawns** are recorded here in clear, non-technical language suitable for release notes.

This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Release tags use a `v` prefix (for example `v1.0.0`). Release sections use the same four buckets throughout: **Major Feature**, **Minor Feature**, **Bug Fixes**, and **Miscellaneous**.

> **Version history note:** The fixed-spawn changes have passed structural and package checks and loaded on a Linux server, but have not been checked in game. Builds use the newest numbered changelog heading (`1.3.0` → `v1.3.0`).

## [Unreleased]

> **Maintainers:** Leave this section empty. Append new entries under the **latest numbered version** heading below (the first `## [x.y.z]` block after this one) until that version is tagged and published.

## [1.5.0]

### Major Feature

### Minor Feature

- Mounts now sprint 25% faster. This covers every Terrorbird colour and the Flying Carpet. Walking pace on a mount is unchanged.

### Bug Fixes

### Miscellaneous

- The mount sprint speed edit changes the player attribute table and the Flying Carpet's sprint effect (game version 245400, both platforms). It has not yet been checked in game.

## [1.4.0]

### Major Feature

### Minor Feature

- Mining nodes now give double the ore. This covers copper, tin, iron, silver, gold, mithril, adamantite, blurite, runite and luminite ore, plus coal, clay, rune essence, gypsum and limestone. Gem finds from mining keep their normal amount and chance. Stone, granite, sandstone, dragon teeth and soul stone are unchanged.

### Bug Fixes

### Miscellaneous

- The ore nodes in both packages were edited from the matching game files (Linux server and Windows client, game version 245400). The doubled ore has not yet been checked in game.

## [1.3.0]

### Major Feature

### Minor Feature

- Linked chests can now supply building and crafting materials from 150 m away instead of 30 m.

### Bug Fixes

### Miscellaneous

## [1.2.1]

### Major Feature

### Minor Feature

### Bug Fixes

- Ghosts now drop three ectoplasm instead of one. Their ectoplasm drops once per player, which ignored the tripled amount, so each ghost now gives three separate ectoplasm from a single roll. The chance of getting ectoplasm, and of the Soulstone Guardian's mount drops, is unchanged.

### Miscellaneous

- Checked against the 29 September 2026 game update (game version 245400). The update did not change anything the mod edits, so the mod works with the updated game and dedicated server.
- Added the tool that makes the larger Dragon Wolf groups, so the whole mod can be rebuilt after future game updates.

## [1.2.0]

### Major Feature

### Minor Feature

- Enemies that drop undead bones now drop three times as many. This covers skeletons, zombies, zombie ogres, Rotsworn and withered or zombie cows. Drop chances are unchanged.
- Ghosts and spectral creatures that drop ectoplasm now drop three at a time instead of one. Drop chances are unchanged.

### Bug Fixes

### Miscellaneous

## [1.1.0]

### Major Feature

### Minor Feature

- The graveyard near Bleakfields Valley now has 20 corpse cotton plants instead of 5. The 15 new plants are spread over open ground between the graves.

### Bug Fixes

### Miscellaneous

## [1.0.0]

### Major Feature

### Minor Feature

- Dynamic Dragon Wolf groups can contain three times as many wolves where the game uses spawn tables.
- Fixed Dragon Wolf spawn points were tripled across 15 world cells by adding two copies beside each of 37 original points. This has not yet been verified in game.

### Bug Fixes

### Miscellaneous

- Added an installable server pak and automated versioned releases.
- Added separate Linux server and Windows packages.
- The updated Linux package mounted and loaded an existing dedicated-server world. An in-game wolf count is still needed.
