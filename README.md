# Dragonwilds Increased Spawns

A work-in-progress mod for **RuneScape: Dragonwilds** to make naturally occurring enemies less sparse, particularly when farming creatures such as dragon wolves.

## Goals

- Increase natural enemy availability without spawning free items or bypassing combat.
- Keep spawn changes configurable and avoid flooding bases or safe areas.
- Preserve the game's normal enemy levels, loot, and progression.
- Make multiplayer behavior server authoritative where the game's modding hooks allow it.

## Status

Repository initialized; the mod is **not implemented or installable yet**. The next step is to inspect the current game build's spawn system and confirm a supported hook or data override before choosing the runtime and writing code. Dedicated server behavior and Linux compatibility need to be verified on the target server build.

## Development notes

Community Dragonwilds mods commonly use [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS), but that alone does not establish which spawn controls this game exposes. Do not copy game binaries or extracted assets into this repository. Track source code and original configuration only.

RuneScape: Dragonwilds is a Jagex game. This project is an independent community mod.
