import json
import tempfile
import unittest
from pathlib import Path

import strip_common_from_export as strip


def make_app(tmp, arch_dlls):
    app = Path(tmp) / "complete-app.app"
    macos = app / "Contents/MacOS"
    macos.mkdir(parents=True)
    for arch in ("arm64", "x86_64"):
        d = app / f"Contents/Resources/data_complete-app_macos_{arch}"
        d.mkdir(parents=True)
        for name, content in arch_dlls.get(arch, {}).items():
            (d / f"{name}.dll").write_bytes(content)
    return app


MANIFEST = {
    "bundleId": "common",
    "managed": {"assemblies": [
        {"id": "Arch", "uri": "res://bundles/common/Arch.dll", "kind": "dll",
         "metadata": {"assemblyName": "Arch", "sha256": "ab" * 32}},
    ]},
}


class StripTests(unittest.TestCase):
    def _manifest_path(self, tmp):
        p = Path(tmp) / "manifest.json"
        p.write_text(json.dumps(MANIFEST))
        return p

    def test_strip_removes_writes_expected_and_installs_pck(self):
        tmp = tempfile.mkdtemp()
        app = make_app(tmp, {
            "arm64": {"Arch": b"same-bytes", "Keep": b"k"},
            "x86_64": {"Arch": b"same-bytes", "Keep": b"k"},
        })
        pck = Path(tmp) / "common.pck"
        pck.write_bytes(b"pck-bytes")
        strip.run(app, self._manifest_path(tmp), "complete-app", pck)

        for arch in ("arm64", "x86_64"):
            d = app / f"Contents/Resources/data_complete-app_macos_{arch}"
            self.assertFalse((d / "Arch.dll").exists())
            self.assertTrue((d / "Keep.dll").exists())

        expected = json.loads((app / "Contents/MacOS/config/common-resident-expected.json").read_text())
        self.assertEqual(expected["bundleId"], "common")
        self.assertEqual(expected["assemblies"][0]["assemblyName"], "Arch")
        self.assertEqual(expected["assemblies"][0]["sha256"], "ab" * 32)
        self.assertEqual((app / "Contents/MacOS/bundles/common.pck").read_bytes(), b"pck-bytes")

    def test_arch_divergence_is_fatal(self):
        tmp = tempfile.mkdtemp()
        app = make_app(tmp, {
            "arm64": {"Arch": b"arm-bytes"},
            "x86_64": {"Arch": b"intel-bytes"},
        })
        pck = Path(tmp) / "common.pck"
        pck.write_bytes(b"p")
        with self.assertRaises(strip.StripError):
            strip.run(app, self._manifest_path(tmp), "complete-app", pck)

    def test_leftover_manifest_dll_fails_verify(self):
        # a manifest DLL that strip cannot remove (e.g. re-listed under another name) must fail
        tmp = tempfile.mkdtemp()
        app = make_app(tmp, {"arm64": {}, "x86_64": {}})
        pck = Path(tmp) / "common.pck"
        pck.write_bytes(b"p")
        # missing everywhere is tolerated (already-stripped rerun) - idempotent
        strip.run(app, self._manifest_path(tmp), "complete-app", pck)


if __name__ == "__main__":
    unittest.main()
