import json
import tempfile
import unittest
from pathlib import Path

import stage_bundle


POLICY = {
    "exactMatches": ["MessagePack", "FantaSim.App.World.Rendering"],
    "prefixes": ["System.", "FantaSim.App."],
}


class ShouldStageTests(unittest.TestCase):
    def test_collectible_override_wins_over_shared_prefix(self):
        self.assertTrue(stage_bundle.should_stage("FantaSim.App.World", {"FantaSim.App.World"}, POLICY))

    def test_shared_exact_is_skipped(self):
        self.assertFalse(stage_bundle.should_stage("MessagePack", set(), POLICY))

    def test_shared_prefix_is_skipped(self):
        self.assertFalse(stage_bundle.should_stage("System.Reactive", set(), POLICY))
        self.assertFalse(stage_bundle.should_stage("FantaSim.App.Ui", set(), POLICY))

    def test_unmatched_is_staged(self):
        self.assertTrue(stage_bundle.should_stage("SurrealDb.Net", set(), POLICY))


class DepsWalkTests(unittest.TestCase):
    def test_runtime_assets_enumerated(self):
        deps = {
            "targets": {
                "net8.0": {
                    "SurrealDb.Net/0.6.0": {"runtime": {"lib/net8.0/SurrealDb.Net.dll": {}}},
                    "NoRuntime/1.0.0": {},
                }
            }
        }
        with tempfile.TemporaryDirectory() as tmp:
            deps_path = Path(tmp) / "X.deps.json"
            deps_path.write_text(json.dumps(deps))
            assets = list(stage_bundle.deps_runtime_assets(deps_path))
        self.assertEqual(assets, [("SurrealDb.Net/0.6.0", "lib/net8.0/SurrealDb.Net.dll")])


class ResolveAssetTests(unittest.TestCase):
    def test_prefers_build_output_then_nuget(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "out"
            nuget = Path(tmp) / "nuget"
            (out).mkdir()
            (out / "Local.dll").write_bytes(b"x")
            pkg_dir = nuget / "surrealdb.net" / "0.6.0" / "lib" / "net8.0"
            pkg_dir.mkdir(parents=True)
            (pkg_dir / "SurrealDb.Net.dll").write_bytes(b"y")

            local = stage_bundle.resolve_asset(out, "Local", "Whatever/1.0.0", "lib/net8.0/Local.dll", nuget)
            self.assertEqual(local, out / "Local.dll")

            remote = stage_bundle.resolve_asset(out, "SurrealDb.Net", "SurrealDb.Net/0.6.0", "lib/net8.0/SurrealDb.Net.dll", nuget)
            self.assertEqual(remote, pkg_dir / "SurrealDb.Net.dll")

    def test_missing_asset_raises(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(stage_bundle.StagingError):
                stage_bundle.resolve_asset(Path(tmp), "Nope", "Nope/1.0.0", "lib/net8.0/Nope.dll", Path(tmp))


if __name__ == "__main__":
    unittest.main()
