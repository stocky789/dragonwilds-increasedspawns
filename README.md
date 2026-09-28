# Dragonwilds Increased Spawns

An experimental mod project to add more **Dragon Wolves** across their existing regions, without changing loot or combat.

## Current implementation

[generate.py](generate.py) turns surveyed vanilla spawn locations into [RuneSchema](https://github.com/gh0sted5456-us/RuneSchema) AI spawn-point definitions. For each vanilla anchor you provide, it adds **two** persistent Dragon Wolf spawn points, 25 and 50 metres along a chosen bearing. The original point remains: this targets three spawn points per anchor across a wider area.

This is **not a tested or ready-to-install mod**. We still need actual Dragon Wolf spawn coordinates, their regional power levels, and an in-game test. The published 1.0 class catalog lists the Dragon Wolf path used in the generator but labels its behavior unverified. The game may also limit live enemy population independently of spawn-point count, so three points may not yield three times as many wolves.

## Supply surveyed anchors

Create an `anchors.json` containing a JSON array. For each **real, existing** Dragon Wolf spawn you inspect in the game, supply:

- `id`: unique short label using letters, digits, underscores or hyphens.
- `x`, `y`: Unreal world coordinates in centimetres.
- `bearing_degrees`: direction from the original spawn into valid terrain **within the same Dragon Wolf region** (0 = +X, 90 = +Y). Check both points at 25 and 50 metres for navigable ground, safe-area boundaries and nearby bases.
- `power_level` (optional): regional power level from the vanilla wolf or spawn point; omit if you have not verified it.

Example **format only**; these are invented coordinates and must not be installed as a mod:

```json
[
  {
    "id": "surveyed_wolf_01",
    "x": 100000,
    "y": 200000,
    "bearing_degrees": 90,
    "power_level": 5
  }
]
```

Run `python generate.py anchors.json`. It writes `RuneSchema/mods/DragonWolfSpawns/spawns/dragon_wolves.json`. Review every output position before installation. RuneSchema traces `"$"` to the ground and uses the game's native AI spawn-point rules.

## Multiplayer and server compatibility

[RuneSchema's authoring guide](https://github.com/gh0sted5456-us/RuneSchema/blob/main/source/AUTHORING-GUIDE.md) instructs mod authors to install the same content mod on the **server and every client** for multiplayer. Its released runtime is a UE4SS DLL for the Windows game layout. This project has **not** been validated on a dedicated server, and a Linux dedicated server cannot simply load that Windows DLL. Server-only deployment and Linux support need separate investigation.

For a Windows test server, install a compatible UE4SS and RuneSchema build, then copy the generated `DragonWolfSpawns` directory under its `RuneSchema/mods/` folder and enable `DragonWolfSpawns : 1` in `RuneSchema/mods/runeschema.txt`. Install matching content on clients as RuneSchema requires. Back up the world before the first test. Inspect `UE4SS.log` for loader errors, verify spawn positions and power levels in game, then count live wolves over repeated visits.

## References

- [RuneSchema spawn loader format](https://github.com/gh0sted5456-us/RuneSchema/blob/main/source/loaders/spawns.md)
- [RuneSchema spawn schema](https://github.com/gh0sted5456-us/RuneSchema/blob/main/docs/schemas/spawns.schema.json)
- [Dragon Wolf class catalog](https://github.com/RSDWArchive/RSDWDevKit/blob/main/RSDWTools/json/SpawnCatalog.json)

RuneScape: Dragonwilds is a Jagex game. This is an independent community project.
