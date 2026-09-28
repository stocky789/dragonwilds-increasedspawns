#!/usr/bin/env python3
"""Generate two additional Dragon Wolf spawn points per surveyed vanilla point."""

import argparse
import json
import math
from pathlib import Path

AI_CLASS = (
    "/FutureMajorVersion/Gameplay/AI/Wolf/DragonWolf/"
    "BP_AI_DragonWolf_Character.BP_AI_DragonWolf_Character_C"
)
OUTPUT = Path("RuneSchema/mods/DragonWolfSpawns/spawns/dragon_wolves.json")


def build_spawns(anchors):
    if not isinstance(anchors, list) or not anchors:
        raise ValueError("Supply at least one surveyed vanilla Dragon Wolf spawn point")
    seen = set()
    spawns = []
    for anchor in anchors:
        name = anchor["id"]
        if not isinstance(name, str) or not name or not all(
            c.isascii() and (c.isalnum() or c in "_-") for c in name
        ) or name in seen:
            raise ValueError(f"Invalid or duplicate anchor id: {name!r}")
        seen.add(name)
        x, y = anchor["x"], anchor["y"]
        bearing = anchor["bearing_degrees"]
        for value in (x, y, bearing):
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ValueError(f"{name}: coordinates and bearing must be finite numbers")
        power = anchor.get("power_level")
        if power is not None and (type(power) is not int or not 1 <= power <= 100):
            raise ValueError(f"{name}: power_level must be an integer from 1 to 100")
        # Bearings point into traversable terrain, chosen while surveying.
        # Unreal coordinates are centimetres; the new points are 25 m and 50 m away.
        for index, distance in enumerate((25, 50), start=1):
            angle = math.radians(bearing)
            point = {
                "Id": f"{name}_extra_{index}",
                "Type": "AISpawnPoint",
                "AIClass": AI_CLASS,
                "Location": [round(x + distance * 100 * math.cos(angle), 2),
                             round(y + distance * 100 * math.sin(angle), 2), "$"],
                "SpawnRadiusMeters": 100,
            }
            if power is not None:
                point["PowerLevel"] = power
            spawns.append(point)
    return spawns


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("anchors", type=Path, help="JSON list of surveyed spawn anchors")
    args = parser.parse_args()
    anchors = json.loads(args.anchors.read_text(encoding="utf-8"))
    spawns = build_spawns(anchors)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(spawns, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(spawns)} additional spawn points to {OUTPUT}")


if __name__ == "__main__":
    main()
