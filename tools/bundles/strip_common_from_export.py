#!/usr/bin/env python3
"""Strip the common resident-layer DLLs from an exported .app and provision it.

Godot's C# export packages the whole publish closure into the per-arch data dirs; presets cannot
exclude managed DLLs, so the strip is a mandatory post-export step (spec §strip). This tool:
  1. hash-compares each manifest DLL across BOTH per-arch data dirs (a divergent assembly cannot
     be served from one universal common.pck - fatal),
  2. deletes every manifest DLL from both data dirs,
  3. VERIFIES none remain (this is the exe∩common audit - see stage_bundle --check-dual for the
     bundle∩common and bundle∩bundle sides),
  4. writes <MacOS>/config/common-resident-expected.json (the loader's boot gate),
  5. installs common.pck into <MacOS>/bundles/ - a stripped app never exists without its layer.
"""
import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

ARCHES = ("arm64", "x86_64")


class StripError(RuntimeError):
    pass


def data_dirs(app, assembly):
    return [Path(app) / f"Contents/Resources/data_{assembly}_macos_{arch}" for arch in ARCHES]


def manifest_assemblies(manifest_path):
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    entries = (manifest.get("managed") or {}).get("assemblies") or []
    if not entries:
        raise StripError(f"{manifest_path} has no managed.assemblies")
    return manifest.get("bundleId", "common"), [
        (e["metadata"]["assemblyName"], e["metadata"]["sha256"]) for e in entries
    ]


def run(app, manifest_path, assembly, common_pck):
    app = Path(app)
    dirs = [d for d in data_dirs(app, assembly) if d.is_dir()]
    if not dirs:
        raise StripError(f"no data_{assembly}_macos_* dirs under {app}")

    bundle_id, assemblies = manifest_assemblies(manifest_path)

    for name, _ in assemblies:
        hashes = set()
        for d in dirs:
            dll = d / f"{name}.dll"
            if dll.is_file():
                hashes.add(hashlib.sha256(dll.read_bytes()).hexdigest())
        if len(hashes) > 1:
            raise StripError(
                f"{name}.dll differs between per-arch data dirs - RID-specific managed asset; "
                f"it cannot be served from one universal common.pck (remove it from the policy)")
        for d in dirs:
            dll = d / f"{name}.dll"
            if dll.is_file():
                dll.unlink()

    leftovers = [f"{d.name}/{name}.dll" for d in dirs for name, _ in assemblies
                 if (d / f"{name}.dll").exists()]
    if leftovers:
        raise StripError(f"strip verify failed, still present: {', '.join(leftovers)}")

    macos = app / "Contents/MacOS"
    config_dir = macos / "config"
    config_dir.mkdir(parents=True, exist_ok=True)
    expected = {
        "bundleId": bundle_id,
        "assemblies": [{"assemblyName": n, "sha256": s} for n, s in assemblies],
    }
    (config_dir / "common-resident-expected.json").write_text(
        json.dumps(expected, indent=2) + "\n", encoding="utf-8")

    bundles_dir = macos / "bundles"
    bundles_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(common_pck, bundles_dir / "common.pck")
    print(f"[strip_common] stripped {len(assemblies)} assemblies from {len(dirs)} data dirs; "
          f"expected catalog + common.pck installed under {macos}")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--assembly", default="complete-app")
    parser.add_argument("--common-pck", required=True)
    args = parser.parse_args(argv)
    run(args.app, args.manifest, args.assembly, args.common_pck)
    return 0


if __name__ == "__main__":
    sys.exit(main())
