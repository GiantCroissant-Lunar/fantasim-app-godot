#!/usr/bin/env python3
"""Generic collectible-bundle stager.

Stages project/bundles/<id>/ from each bundle root project's real deps.json, filtered through
the SAME shared-assembly policy the runtime plugin host uses. Sources of truth:
  project/hosts/complete-app/config/shared-assembly-policy.json  (what stays resident-shared)
  project/hosts/complete-app/config/collectible-bundles.json     (bundle roots + collectible overrides)

Replaces the per-bundle hand-written Taskfile staging (the 2026-07-03 MessagePack two-place-mirror
lesson: hand mirrors of the share list WILL drift).
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REGISTRY_PATH = REPO_ROOT / "project/hosts/complete-app/config/collectible-bundles.json"
POLICY_PATH = REPO_ROOT / "project/hosts/complete-app/config/shared-assembly-policy.json"
BUNDLES_DIR = REPO_ROOT / "project/bundles"


class StagingError(RuntimeError):
    pass


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def should_stage(assembly_name, collectible_overrides, policy):
    """Collectible override wins; otherwise shared exact/prefix names stay OUT of the bundle."""
    if assembly_name in collectible_overrides:
        return True
    if assembly_name in set(policy["exactMatches"]):
        return False
    if any(assembly_name.startswith(p) for p in policy["prefixes"]):
        return False
    return True


def deps_runtime_assets(deps_path):
    """Yield (package, asset) for every runtime .dll asset across all targets, in file order."""
    deps = load_json(deps_path)
    for target in deps.get("targets", {}).values():
        for package, entry in target.items():
            for asset in (entry or {}).get("runtime", {}) or {}:
                if asset.endswith(".dll"):
                    yield package, asset


def resolve_asset(output_dir, assembly_name, package, asset, nuget_root):
    local = output_dir / f"{assembly_name}.dll"
    if local.is_file():
        return local
    package_id, _, package_version = package.partition("/")
    remote = nuget_root / package_id.lower() / package_version / asset
    if remote.is_file():
        return remote
    raise StagingError(f"missing runtime asset for bundle staging: {local} and {remote}")


def bundle_entry(registry, bundle_id):
    for entry in registry["bundles"]:
        if entry["bundleId"] == bundle_id:
            return entry
    raise StagingError(f"bundle '{bundle_id}' not found in {REGISTRY_PATH}")


def clean_bundle_dir(dest):
    for f in dest.iterdir():
        if f.is_file() and (f.suffix == ".dll" or f.name.endswith(".deps.json")):
            f.unlink()


def stage(bundle_id, registry, policy, build=True):
    entry = bundle_entry(registry, bundle_id)
    projects = entry.get("projects")
    if not projects:
        raise StagingError(f"bundle '{bundle_id}' has no 'projects' in {REGISTRY_PATH}")
    overrides = set(entry.get("assemblyNames", []))
    dest = BUNDLES_DIR / bundle_id
    dest.mkdir(parents=True, exist_ok=True)
    clean_bundle_dir(dest)

    nuget_root = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages"))
    staged = set()

    for project in projects:
        csproj = REPO_ROOT / project["csproj"]
        output = REPO_ROOT / project["output"]
        assembly = project["assembly"]

        if build:
            subprocess.run(
                ["dotnet", "build", str(csproj), "-c", "Debug", "-v", "q", "-nologo"],
                check=True, cwd=REPO_ROOT)

        plugin_dll = output / f"{assembly}.dll"
        if not plugin_dll.is_file():
            raise StagingError(f"bundle root assembly not found after build: {plugin_dll}")
        shutil.copy2(plugin_dll, dest)
        staged.add(assembly)

        deps_path = output / f"{assembly}.deps.json"
        if not deps_path.is_file():
            print(f"[stage_bundle] {bundle_id}: no deps.json for {assembly}; staged the root dll only")
            continue
        shutil.copy2(deps_path, dest)

        for package, asset in deps_runtime_assets(deps_path):
            name = Path(asset).name[: -len(".dll")]
            if name in staged or not should_stage(name, overrides, policy):
                continue
            staged.add(name)
            shutil.copy2(resolve_asset(output, name, package, asset, nuget_root), dest)

    print(f"[stage_bundle] {bundle_id}: staged {len(staged)} assemblies into {dest}")


HOST_OUTPUT_DIR = REPO_ROOT / "project/hosts/complete-app/.godot/mono/temp/bin/Debug"

# Known dual copies with an owning plan — each entry MUST cite the work that removes it.
# Anything NOT in this list failing --check-dual is new drift and must be fixed, not added here.
KNOWN_DUAL_COPIES = {
    # Bundle-maximalism phase 2 (Timeline T3 -> timeline bundle) removes the host's
    # ProjectReference to plugins/App.Timeline; until then the resident T3 copy and the
    # bundle's plugin copy coexist, talking only through shared contracts.
    # See vault/specs/2026-07-08-bundle-oriented-maximalism.md phase table.
    ("timeline", "FantaSim.App.Timeline.dll"),
}


def find_dual_copies(bundle_dir, host_assembly_names):
    """DLLs staged in a bundle that ALSO exist in the resident host output.

    A dual copy is the MessagePack-class type-identity trap: the resident side binds the
    resident copy while the bundle's ALC loads its private one, and any type crossing the
    boundary splits (2026-07-08 audit: 7 of 51 world-bundle assemblies were dual copies)."""
    if not bundle_dir.is_dir():
        return []
    return sorted(f.name for f in bundle_dir.glob("*.dll") if f.name in host_assembly_names)


def check_dual(registry):
    if not HOST_OUTPUT_DIR.is_dir():
        print(f"[stage_bundle] --check-dual skipped: host output not built ({HOST_OUTPUT_DIR})")
        return False
    host_names = {f.name for f in HOST_OUTPUT_DIR.glob("*.dll")}
    violations = False
    for entry in registry["bundles"]:
        bundle_id = entry["bundleId"]
        dual = [d for d in find_dual_copies(BUNDLES_DIR / bundle_id, host_names)
                if (bundle_id, d) not in KNOWN_DUAL_COPIES]
        if dual:
            violations = True
            print(f"[stage_bundle] DUAL COPIES in bundle '{entry['bundleId']}' "
                  f"(also in resident host output — promote to shared-assembly-policy.json "
                  f"or drop the collectible override): {', '.join(dual)}")
    if not violations:
        print("[stage_bundle] --check-dual: no dual copies; bundle/resident split is clean")
    return violations


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundles", nargs="*", help="bundle ids to stage")
    parser.add_argument("--all", action="store_true", help="stage every bundle with a 'projects' entry")
    parser.add_argument("--no-build", action="store_true", help="skip dotnet build of root projects")
    parser.add_argument("--check-dual", action="store_true",
                        help="audit staged bundles for assemblies duplicated in the resident host output; exit 1 on findings")
    args = parser.parse_args(argv)

    registry = load_json(REGISTRY_PATH)
    policy = load_json(POLICY_PATH)

    if args.check_dual and not args.bundles and not args.all:
        return 1 if check_dual(registry) else 0

    ids = args.bundles
    if args.all:
        ids = [b["bundleId"] for b in registry["bundles"] if b.get("projects")]
    if not ids:
        parser.error("no bundle ids given (or use --all)")

    for bundle_id in ids:
        stage(bundle_id, registry, policy, build=not args.no_build)

    if args.check_dual:
        return 1 if check_dual(registry) else 0


if __name__ == "__main__":
    sys.exit(main())
