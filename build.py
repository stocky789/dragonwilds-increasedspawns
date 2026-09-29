#!/usr/bin/env python3
"""Pack the edited Dragon Wolf assets into versioned release ZIPs."""

import re
import shutil
import subprocess
import sys
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "source"
DIST = ROOT / "dist"
NAME = "DragonWolfSpawns_P"
PLATFORMS = {"linux": "LinuxServer", "windows": "Windows"}


def version_and_notes(changelog):
    match = re.search(r"^## \[(\d+\.\d+\.\d+)\]\s*$", changelog, re.MULTILINE)
    if not match:
        raise ValueError("CHANGELOG.md needs a numbered version heading")
    end = re.search(r"^## \[", changelog[match.end():], re.MULTILINE)
    notes = changelog[match.end():match.end() + end.start() if end else None].strip()
    if not re.search(r"^- ", notes, re.MULTILINE):
        raise ValueError("The latest changelog version has no release entries")
    return match.group(1), notes


def source_assets(source):
    assets = sorted((*source.rglob("*.uasset"), *source.rglob("*.umap")))
    exports = {asset.with_suffix(".uexp") for asset in assets}
    if not assets or len(exports) != len(assets) or exports != set(source.rglob("*.uexp")):
        raise ValueError(f"Expected matching .uasset/.umap and .uexp files in {source}")
    return assets


def main():
    version, notes = version_and_notes((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"))
    if sys.argv[1:] == ["--version"]:
        print(version)
        return
    if sys.argv[1:]:
        raise SystemExit("Usage: python3 build.py [--version]")
    if not shutil.which("retoc"):
        raise SystemExit("Install retoc v0.1.5 and put it on PATH")
    DIST.mkdir(exist_ok=True)
    (DIST / "release-notes.md").write_text(notes + "\n", encoding="utf-8")
    for platform, label in PLATFORMS.items():
        source = SOURCE / platform
        assets = source_assets(source)
        output = DIST / platform
        output.mkdir(exist_ok=True)
        utoc = output / f"{NAME}.utoc"
        subprocess.run(["retoc", "to-zen", "--version", "UE5_6", str(source), str(utoc)], check=True)
        subprocess.run(["retoc", "verify", str(utoc)], check=True)
        listing = subprocess.check_output(["retoc", "list", "--path", str(utoc)], text=True)
        packed_paths = {line.split()[-1].removeprefix("../../../") for line in listing.splitlines() if line.split()}
        missing = [str(asset.relative_to(source)) for asset in assets if asset.relative_to(source).as_posix() not in packed_paths]
        if missing:
            raise SystemExit(f"Packed {platform} archive is missing: {', '.join(missing)}")
        archive = DIST / f"IncreasedSpawns-{label}-v{version}.zip"
        with ZipFile(archive, "w", ZIP_DEFLATED) as zip_file:
            for suffix in ("pak", "utoc", "ucas"):
                path = output / f"{NAME}.{suffix}"
                zip_file.write(path, path.name)
        print(archive)


if __name__ == "__main__":
    main()
