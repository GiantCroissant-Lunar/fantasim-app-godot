import json
import tempfile
import unittest
from pathlib import Path

import stage_bundle


class CommonCandidateTests(unittest.TestCase):
    POLICY = {
        "common": {
            "exactMatches": ["Arch"],
            "prefixes": ["MessagePipe"],
            "suffixRules": [{"prefix": "FantaSim.App.", "suffix": ".Contracts"}],
            "detectorGated": ["BoomHud.Abstractions"],
        }
    }

    def _host_dir(self, names):
        d = Path(tempfile.mkdtemp())
        for n in names:
            (d / f"{n}.dll").write_bytes(b"MZ fake assembly " + n.encode())
        return d

    def test_candidates_match_exact_prefix_and_suffix_rules(self):
        host = self._host_dir([
            "Arch", "MessagePipe", "MessagePipe.Interprocess",
            "FantaSim.App.Timeline.Contracts", "FantaSim.App.Timeline",
            "Newtonsoft.Json",
        ])
        names = {p.stem for p in stage_bundle.common_candidates(self.POLICY, host)}
        self.assertEqual(
            names,
            {"Arch", "MessagePipe", "MessagePipe.Interprocess", "FantaSim.App.Timeline.Contracts"})

    def test_missing_exact_candidate_is_error(self):
        host = self._host_dir(["MessagePipe"])
        with self.assertRaises(stage_bundle.StagingError):
            stage_bundle.common_candidates(self.POLICY, host)

    def test_godot_facing_detector(self):
        d = Path(tempfile.mkdtemp())
        pure = d / "Pure.dll"
        pure.write_bytes(b"MZ...System.Runtime...")
        facing = d / "Facing.dll"
        facing.write_bytes(b"MZ...GodotSharp...")
        self.assertFalse(stage_bundle.is_godot_facing(pure))
        self.assertTrue(stage_bundle.is_godot_facing(facing))

    def test_detector_veto_on_ungated_candidate_is_error(self):
        host = self._host_dir(["MessagePipe"])
        (host / "Arch.dll").write_bytes(b"MZ...GodotSharp...")
        with self.assertRaises(stage_bundle.StagingError):
            stage_bundle.stage_common_from_dir(self.POLICY, host, Path(tempfile.mkdtemp()))

    def test_host_locked_candidates_are_skipped(self):
        host = self._host_dir(["Arch", "MessagePipe"])
        # autoload script assembly referencing MessagePipe -> exe-locked, skipped from common
        (host / "complete-app.dll").write_bytes(b"MZ..\x00MessagePipe\x00..")
        out = Path(tempfile.mkdtemp())
        stage_bundle.stage_common_from_dir(self.POLICY, host, out)
        import json as _json
        manifest = _json.loads((out / "manifest.json").read_text())
        names = {a["metadata"]["assemblyName"] for a in manifest["managed"]["assemblies"]}
        self.assertIn("Arch", names)
        self.assertNotIn("MessagePipe", names)
        self.assertNotIn("complete-app", names)

    def test_stage_common_writes_manifest_with_sha(self):
        host = self._host_dir(["Arch", "MessagePipe", "FantaSim.App.Timeline.Contracts"])
        # gated entry present but Godot-facing -> skipped with warning, not error
        (host / "BoomHud.Abstractions.dll").write_bytes(b"MZ...GodotSharp...")
        out = Path(tempfile.mkdtemp())
        stage_bundle.stage_common_from_dir(self.POLICY, host, out)
        manifest = json.loads((out / "manifest.json").read_text())
        self.assertEqual(manifest["bundleId"], "common")
        self.assertEqual(manifest["metadata"]["bundleType"], "resident-layer")
        entries = {a["metadata"]["assemblyName"]: a for a in manifest["managed"]["assemblies"]}
        self.assertIn("Arch", entries)
        self.assertNotIn("BoomHud.Abstractions", entries)
        self.assertEqual(len(entries["Arch"]["metadata"]["sha256"]), 64)
        self.assertTrue((out / "Arch.dll").is_file())


class CrossBundleAuditTests(unittest.TestCase):
    def _bundles(self, layout):
        root = Path(tempfile.mkdtemp())
        for bundle, names in layout.items():
            d = root / bundle
            d.mkdir()
            for n in names:
                (d / f"{n}.dll").write_bytes(b"x")
        return root

    def test_bundle_common_overlap_detected(self):
        root = self._bundles({"world": ["SurrealDb.Net", "Arch"], "common": ["Arch"]})
        violations = stage_bundle.cross_bundle_violations(root, ["world"], "common")
        self.assertEqual(violations, [("world", "common", "Arch.dll")])

    def test_bundle_bundle_overlap_detected(self):
        root = self._bundles({"world": ["Shared.Thing"], "timeline": ["Shared.Thing"], "common": []})
        violations = stage_bundle.cross_bundle_violations(root, ["world", "timeline"], "common")
        self.assertEqual(violations, [("timeline", "world", "Shared.Thing.dll")])

    def test_clean_layout_passes(self):
        root = self._bundles({"world": ["SurrealDb.Net"], "timeline": ["FantaSim.App.Timeline"], "common": ["Arch"]})
        self.assertEqual(
            stage_bundle.cross_bundle_violations(root, ["world", "timeline"], "common"), [])


if __name__ == "__main__":
    unittest.main()
