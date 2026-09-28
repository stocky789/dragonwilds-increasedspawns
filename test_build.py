import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from build import source_assets, version_and_notes


class ChangelogTest(unittest.TestCase):
    def test_latest_version_and_empty_release(self):
        text = "## [Unreleased]\n\n## [1.2.3]\n\n- New wolves\n\n## [1.2.2]\n- Old wolves\n"
        self.assertEqual(version_and_notes(text), ("1.2.3", "- New wolves"))
        with self.assertRaises(ValueError):
            version_and_notes("## [Unreleased]\n\n## [1.2.3]\n")

    def test_source_assets_require_complete_pairs(self):
        with TemporaryDirectory() as directory:
            source = Path(directory)
            asset = source / "Map.uasset"
            asset.touch()
            with self.assertRaises(ValueError):
                source_assets(source)
            asset.with_suffix(".uexp").touch()
            self.assertEqual(source_assets(source), [asset])
            world_cell = source / "WorldCell.umap"
            world_cell.touch()
            with self.assertRaises(ValueError):
                source_assets(source)
            world_cell.with_suffix(".uexp").touch()
            self.assertEqual(source_assets(source), [asset, world_cell])
            (source / "Extra.uexp").touch()
            with self.assertRaises(ValueError):
                source_assets(source)


if __name__ == "__main__":
    unittest.main()
