# Bundle Maximalism Phase 0/1 Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Externalize the shared-assembly policy to one config file consumed by both runtime and
staging (phase 0), then move App.Presentation into the collectible world bundle (phase 1), per
[specs/2026-07-08-bundle-oriented-maximalism.md](../specs/2026-07-08-bundle-oriented-maximalism.md).

**Architecture:** Phase 0 makes `config/shared-assembly-policy.json` the single source of truth for
what is shared-resident: `Bootstrap.BuildPluginHost` reads it at runtime and a new generic stager
(`tools/bundles/stage_bundle.py`) reads it at build time, replacing the hand-written Taskfile
staging mirrors. The actual polarity *flip* (contracts-only sharing) is deliberately NOT in this
plan — after phase 1 proves in the windowed gate, flipping becomes an edit to that one JSON file.
Phase 1 moves the planet presentation binder into world.pck: `IPlanetPresentation` extracts to a
new T1 contract assembly, a `PresentationPlugin` in the world bundle creates/owns the binder, and
the host only resolves the contract, mounts, and severs references on reload.

**Tech Stack:** .NET 8 / Godot 4.7 (Godot.NET.Sdk), xunit, PluginArchi (collectible ALCs +
SharedAssemblyPolicy), ServiceArchi IRegistry, Taskfile, Python 3 stdlib (stager).

## Global Constraints

- Work on branch `feat/bundle-maximalism-phase0-1` (create from `main` before Task 1; never
  implement on `main`).
- Conventional Commits; commit per task; NEVER `--no-verify`, never `--amend` away a failed hook.
- Repo root for all paths below: `yokan-projects/fantasim-app-godot`.
- Behavior parity is the phase-0 acceptance bar: the shared policy lists in the JSON must be
  VERBATIM the arrays currently hardcoded in `project/plugins/App.Common/Bootstrap.cs:110-161`,
  and the staged world-bundle DLL set must be identical before/after the stager swap.
- The `.sln` gotcha: a test project added to `project/FantaSim.sln` without
  `ProjectConfigurationPlatforms` entries is silently skipped — always add via `dotnet sln add`
  and prove discovery with `dotnet test --filter` afterward.
- Python: stdlib only (no pip installs). Run tests via `python3 -m unittest`.
- Do NOT shrink the shared Cartography/App.World.Rendering closure in this plan — that cleanup is
  gated on the post-phase-1 polarity flip (spec "Standing risks"). YAGNI here.
- Full suite green gate per task: `dotnet test project/FantaSim.sln -v q -nologo`.

## File Structure

```
project/hosts/complete-app/config/shared-assembly-policy.json   NEW  runtime+stager share lists
project/plugins/App.Common/SharedAssemblyPolicyConfig.cs        NEW  parser (mirror of CollectibleBundles)
project/plugins/App.Common/Bootstrap.cs                         MOD  BuildPluginHost takes the config
project/hosts/complete-app/Host.cs                              MOD  loads policy json; phase-1 rewiring
project/tests/App.Common.Tests/                                 NEW  xunit project (parser tests)
tools/bundles/stage_bundle.py                                   NEW  generic deps.json-driven stager
tools/bundles/test_stage_bundle.py                              NEW  unittest suite for the stager
project/hosts/complete-app/config/collectible-bundles.json     MOD  add per-bundle "projects" entries
Taskfile.yml                                                    MOD  bundle:*:build call the stager
project/contracts/App.Presentation/App.Presentation.csproj     NEW  T1 contract (FantaSim.App.Presentation.Contracts)
project/contracts/App.Presentation/IPlanetPresentation.cs      NEW  moved from the plugin
project/contracts/App.Presentation/AssemblyInfo.cs             NEW  [PluginSharedContract]
project/plugins/App.Presentation/PresentationComposition.cs    MOD  interface removed (stays factory)
project/plugins/App.Presentation/App.Presentation.csproj       MOD  + contract ref, + PluginArchi refs
project/plugins/App.Presentation/PresentationPlugin.cs         NEW  world-bundle plugin entry
project/tests/App.Presentation.Tests/PresentationPluginTests.cs NEW plugin lifecycle tests
project/hosts/complete-app/complete-app.csproj                 MOD  plugin ref -> contract ref
```

---

### Task 1: Externalize the shared-assembly policy (runtime side)

**Files:**
- Create: `project/hosts/complete-app/config/shared-assembly-policy.json`
- Create: `project/plugins/App.Common/SharedAssemblyPolicyConfig.cs`
- Create: `project/tests/App.Common.Tests/App.Common.Tests.csproj`
- Create: `project/tests/App.Common.Tests/SharedAssemblyPolicyConfigTests.cs`
- Create: `project/tests/App.Common.Tests/CollectibleBundlesTests.cs`
- Modify: `project/plugins/App.Common/Bootstrap.cs:110-162` (BuildPluginHost)
- Modify: `project/hosts/complete-app/Host.cs:52-53` (load + pass the config)
- Check/Modify: `project/hosts/complete-app/export_presets.cfg` (config file must export)

**Interfaces:**
- Consumes: existing `CollectibleBundles` (`AssemblyNames`), `SharedAssemblyPolicy(exactMatches, prefixes, excludedExactMatches)` ctor from PluginArchi.
- Produces: `SharedAssemblyPolicyConfig` with `static SharedAssemblyPolicyConfig ParseJson(string json)`, `IReadOnlyList<string> ExactMatches`, `IReadOnlyList<string> Prefixes`; new signature `Bootstrap.BuildPluginHost(CollectibleBundles collectibleBundles, SharedAssemblyPolicyConfig sharedPolicy)`. Task 2's stager reads the same JSON file.

- [ ] **Step 1: Create the branch**

```bash
cd yokan-projects/fantasim-app-godot && git checkout -b feat/bundle-maximalism-phase0-1
```

- [ ] **Step 2: Create the test project and write the failing tests**

Create `project/tests/App.Common.Tests/App.Common.Tests.csproj`. First run
`cat project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj` and mirror its exact
package set (it uses CPM — no `Version=` attributes); the shape is:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\plugins\App.Common\App.Common.csproj" />
  </ItemGroup>
</Project>
```

Create `project/tests/App.Common.Tests/SharedAssemblyPolicyConfigTests.cs`:

```csharp
using System;
using FantaSim.App.Common;
using Xunit;

namespace FantaSim.App.Common.Tests;

public class SharedAssemblyPolicyConfigTests
{
    [Fact]
    public void ParsesExactMatchesAndPrefixes()
    {
        var config = SharedAssemblyPolicyConfig.ParseJson(
            """{"comment":"x","exactMatches":["MessagePack","Arch"],"prefixes":["System.","FantaSim.App."]}""");
        Assert.Equal(new[] { "MessagePack", "Arch" }, config.ExactMatches);
        Assert.Equal(new[] { "System.", "FantaSim.App." }, config.Prefixes);
    }

    [Fact]
    public void MissingPrefixesArrayThrows()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SharedAssemblyPolicyConfig.ParseJson("""{"exactMatches":[]}"""));
        Assert.Contains("prefixes", ex.Message);
    }

    [Fact]
    public void EmptyJsonThrows()
        => Assert.Throws<InvalidOperationException>(() => SharedAssemblyPolicyConfig.ParseJson(" "));

    [Fact]
    public void NonStringEntryThrows()
        => Assert.Throws<InvalidOperationException>(
            () => SharedAssemblyPolicyConfig.ParseJson("""{"exactMatches":[1],"prefixes":[]}"""));
}
```

Create `project/tests/App.Common.Tests/CollectibleBundlesTests.cs` (locks in that the Task-2
schema extension is tolerated by the runtime parser):

```csharp
using FantaSim.App.Common;
using Xunit;

namespace FantaSim.App.Common.Tests;

public class CollectibleBundlesTests
{
    [Fact]
    public void ProjectsFieldIsToleratedAndIgnored()
    {
        var bundles = CollectibleBundles.ParseJson(
            """
            {"bundles":[{"bundleId":"stage","pluginAssembly":"FantaSim.App.Stage.dll",
              "projects":[{"csproj":"a.csproj","output":"bin","assembly":"FantaSim.App.Stage"}]}]}
            """);
        Assert.True(bundles.ContainsAssembly("FantaSim.App.Stage"));
    }
}
```

Add to the solution: `dotnet sln project/FantaSim.sln add project/tests/App.Common.Tests/App.Common.Tests.csproj`

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test project/FantaSim.sln --filter "FullyQualifiedName~SharedAssemblyPolicyConfigTests" -v q -nologo`
Expected: build FAILURE — `SharedAssemblyPolicyConfig` does not exist. (The CollectibleBundles
test alone would pass; the compile failure is the RED state.)

- [ ] **Step 4: Write `SharedAssemblyPolicyConfig`**

Create `project/plugins/App.Common/SharedAssemblyPolicyConfig.cs`:

```csharp
using System.Text.Json;

namespace FantaSim.App.Common;

/// <summary>
/// Externalized share-lists for the plugin host's SharedAssemblyPolicy. Single source of truth
/// (config/shared-assembly-policy.json) consumed by BOTH Bootstrap.BuildPluginHost (runtime) and
/// tools/bundles/stage_bundle.py (build-time bundle staging filter), so the two can never drift
/// (the MessagePack two-place-mirror lesson, 2026-07-03).
/// </summary>
public sealed class SharedAssemblyPolicyConfig
{
    private SharedAssemblyPolicyConfig(IReadOnlyList<string> exactMatches, IReadOnlyList<string> prefixes)
    {
        ExactMatches = exactMatches;
        Prefixes = prefixes;
    }

    public IReadOnlyList<string> ExactMatches { get; }

    public IReadOnlyList<string> Prefixes { get; }

    public static SharedAssemblyPolicyConfig ParseJson(string json)
    {
        const string source = "project/hosts/complete-app/config/shared-assembly-policy.json";

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Shared assembly policy config from {source} is empty.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse shared assembly policy config from {source}: {ex.Message}", ex);
        }

        using (doc)
        {
            return new SharedAssemblyPolicyConfig(
                ReadStringArray(doc.RootElement, "exactMatches", source),
                ReadStringArray(doc.RootElement, "prefixes", source));
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property, string source)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Shared assembly policy config from {source} is missing a \"{property}\" array.");

        var values = new List<string>(array.GetArrayLength());
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString()))
                throw new InvalidOperationException($"Shared assembly policy config from {source} has a non-string/empty \"{property}\" entry.");
            values.Add(entry.GetString()!);
        }

        return values;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test project/FantaSim.sln --filter "FullyQualifiedName~App.Common.Tests" -v q -nologo`
Expected: PASS, 5 tests. If 0 tests run, the sln entry is missing `ProjectConfigurationPlatforms`
— fix the sln (re-add via `dotnet sln add`), do not skip.

- [ ] **Step 6: Create the policy JSON — VERBATIM from Bootstrap.cs**

Create `project/hosts/complete-app/config/shared-assembly-policy.json`. The two arrays MUST be
copied exactly from `project/plugins/App.Common/Bootstrap.cs:110-161` (open the file and
transcribe; the lists below are the 2026-07-08 state — re-verify against the file before writing):

```json
{
  "comment": "Share-lists for the plugin host's SharedAssemblyPolicy. SINGLE SOURCE OF TRUTH consumed by App.Common/Bootstrap.cs (runtime) AND tools/bundles/stage_bundle.py (bundle staging). excludedExactMatches come from collectible-bundles.json at runtime. Flipping bundle-maximalism polarity (share contracts only) is an edit to THIS file once phase 1 proves.",
  "exactMatches": [
    "FantaSim.World.Fields.Contracts",
    "FantaSim.World.Fields.Core",
    "FantaSim.World.Shared.Contracts",
    "UnifyMaths",
    "UnifyMaths.Numerics",
    "UnifyStorage.Abstractions",
    "UnifyStorage.Runtime.LiteDb",
    "Arch",
    "MessagePack",
    "MessagePack.Annotations",
    "FantaSim.App.World.Rendering",
    "Cartography.Globe.Core",
    "Cartography.Globe.Contracts",
    "Cartography.Shared.Contracts"
  ],
  "prefixes": [
    "System.",
    "Microsoft.",
    "Godot",
    "GodotSharp",
    "netstandard",
    "PluginArchi.",
    "ServiceArchi.",
    "RegistryArchi.",
    "DependencyArchi.",
    "CrosscutFoundation.",
    "MessagePipe",
    "BoomHud",
    "R3",
    "ReactiveUI",
    "DynamicData",
    "FantaSim.App.",
    "FantaSim.App.World.",
    "FantaSim.App.Command.",
    "Akka",
    "Newtonsoft.Json",
    "UnifyEcs.",
    "TimeDete."
  ]
}
```

- [ ] **Step 7: Rewire Bootstrap.BuildPluginHost to consume the config**

In `project/plugins/App.Common/Bootstrap.cs`, change the method signature (line 93):

```csharp
    public void BuildPluginHost(CollectibleBundles collectibleBundles, SharedAssemblyPolicyConfig sharedPolicy)
    {
        ArgumentNullException.ThrowIfNull(collectibleBundles);
        ArgumentNullException.ThrowIfNull(sharedPolicy);
```

and replace the entire hardcoded `.WithSharedPolicy(new SharedAssemblyPolicy(...))` block
(lines 110-162) with:

```csharp
            // Share-lists are externalized to config/shared-assembly-policy.json (single source of
            // truth with the bundle staging filter in tools/bundles/stage_bundle.py). The per-name
            // rationale comments moved into the git history of this file at the extraction commit.
            .WithSharedPolicy(new SharedAssemblyPolicy(
                exactMatches: sharedPolicy.ExactMatches.ToArray(),
                prefixes: sharedPolicy.Prefixes.ToArray(),
                excludedExactMatches: collectibleBundles.AssemblyNames.ToArray()))
```

Then find every other caller: `grep -rn "BuildPluginHost" project --include="*.cs" | grep -v obj`.
Update each to construct/pass a `SharedAssemblyPolicyConfig` (test callers may parse a minimal
inline JSON literal with both arrays).

- [ ] **Step 8: Host loads the policy file**

In `project/hosts/complete-app/Host.cs`, after `_collectibleBundles = LoadCollectibleBundles();`
(line 52), change:

```csharp
        _collectibleBundles = LoadCollectibleBundles();
        _composition.Bootstrap.BuildPluginHost(_collectibleBundles, LoadSharedAssemblyPolicy());
```

and add next to `LoadCollectibleBundles()` (line 737):

```csharp
    private static SharedAssemblyPolicyConfig LoadSharedAssemblyPolicy()
    {
        // Fail hard: without the share-lists the plugin host would share nothing and every bundle
        // would duplicate the kernel closure (type-identity chaos). No silent fallback.
        const string configPath = "res://config/shared-assembly-policy.json";
        if (!Godot.FileAccess.FileExists(configPath))
            throw new InvalidOperationException($"Missing required config: {configPath}");
        return SharedAssemblyPolicyConfig.ParseJson(Godot.FileAccess.GetFileAsString(configPath));
    }
```

- [ ] **Step 9: Verify the config exports with the app**

Run: `grep -n "collectible-bundles\|config" project/hosts/complete-app/export_presets.cfg`
If the export filter names `config/collectible-bundles.json` explicitly (not a `config/*`
wildcard), add `config/shared-assembly-policy.json` to the same `include_filter` list(s).
If it is a wildcard covering `config/`, no change.

- [ ] **Step 10: Build + full suite**

Run: `dotnet build project/FantaSim.sln -v q -nologo && dotnet test project/FantaSim.sln -v q -nologo`
Expected: build OK, all tests PASS.

- [ ] **Step 11: Commit**

```bash
git add -A && git commit -m "feat(bundles): externalize shared-assembly policy to config json

Single source of truth for runtime SharedAssemblyPolicy and (next) the
generic bundle stager. Lists verbatim from Bootstrap.cs; behavior identical."
```

---

### Task 2: Generic bundle stager driven by the two config files

**Files:**
- Create: `tools/bundles/stage_bundle.py`
- Create: `tools/bundles/test_stage_bundle.py`
- Modify: `project/hosts/complete-app/config/collectible-bundles.json` (add `projects` per bundle)
- Modify: `Taskfile.yml:116-314` (bundle build tasks call the stager; delete the inline world script)

**Interfaces:**
- Consumes: `shared-assembly-policy.json` (`exactMatches`, `prefixes`) and
  `collectible-bundles.json` (`bundles[].bundleId`, `.assemblyNames`, new `.projects[]` with
  `csproj`, `output`, `assembly`).
- Produces: CLI `python3 tools/bundles/stage_bundle.py <bundleId>... [--all] [--no-build]` staging
  `project/bundles/<id>/` exactly like today's per-bundle Taskfile logic. Task 5 reuses it for the
  presentation assembly.

- [ ] **Step 1: Write the failing stager tests**

Create `tools/bundles/test_stage_bundle.py`:

```python
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd tools/bundles && python3 -m unittest test_stage_bundle -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'stage_bundle'`.

- [ ] **Step 3: Write the stager**

Create `tools/bundles/stage_bundle.py`:

```python
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


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundles", nargs="*", help="bundle ids to stage")
    parser.add_argument("--all", action="store_true", help="stage every bundle with a 'projects' entry")
    parser.add_argument("--no-build", action="store_true", help="skip dotnet build of root projects")
    args = parser.parse_args(argv)

    registry = load_json(REGISTRY_PATH)
    policy = load_json(POLICY_PATH)

    ids = args.bundles
    if args.all:
        ids = [b["bundleId"] for b in registry["bundles"] if b.get("projects")]
    if not ids:
        parser.error("no bundle ids given (or use --all)")

    for bundle_id in ids:
        stage(bundle_id, registry, policy, build=not args.no_build)


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd tools/bundles && python3 -m unittest test_stage_bundle -v`
Expected: PASS, 7 tests.

- [ ] **Step 5: Add `projects` entries to collectible-bundles.json**

In `project/hosts/complete-app/config/collectible-bundles.json`, extend each entry (the runtime
parser ignores `projects` — locked by Task 1's test). Final file:

```json
{
  "comment": "Registry of collectible bundles. Single source of truth for (a) the SharedAssemblyPolicy excludedExactMatches (assembly name = pluginAssembly minus .dll) so the bundle loads into its own collectible ALC instead of the shared parent, (b) the load-time lint in BundleHost, and (c) the generic stager tools/bundles/stage_bundle.py ('projects' entries: root csprojs + their build output dirs). Every bundle under project/bundles/<id>/ with a pluginAssembly MUST have an entry here.",
  "bundles": [
    {
      "bundleId": "stage",
      "pluginAssembly": "FantaSim.App.Stage.dll",
      "projects": [
        { "csproj": "project/plugins/App.Stage/App.Stage.csproj", "output": "project/plugins/App.Stage/bin/Debug/net8.0", "assembly": "FantaSim.App.Stage" }
      ]
    },
    {
      "bundleId": "assist",
      "pluginAssembly": "FantaSim.App.Assist.dll",
      "projects": [
        { "csproj": "project/plugins/App.Assist/App.Assist.csproj", "output": "project/plugins/App.Assist/bin/Debug/net8.0", "assembly": "FantaSim.App.Assist" }
      ]
    },
    {
      "bundleId": "timeline",
      "pluginAssembly": "FantaSim.App.Timeline.dll",
      "projects": [
        { "csproj": "project/plugins/App.Timeline/App.Timeline.csproj", "output": "project/plugins/App.Timeline/.godot/mono/temp/bin/Debug", "assembly": "FantaSim.App.Timeline" }
      ]
    },
    {
      "bundleId": "activity",
      "pluginAssembly": "FantaSim.App.Ui.Activity.dll",
      "projects": [
        { "csproj": "project/plugins/App.Ui.Activity/App.Ui.Activity.csproj", "output": "project/plugins/App.Ui.Activity/bin/Debug/net8.0", "assembly": "FantaSim.App.Ui.Activity" }
      ]
    },
    {
      "bundleId": "world",
      "pluginAssembly": "FantaSim.App.World.dll",
      "projects": [
        { "csproj": "project/plugins/App.World/App.World.csproj", "output": "project/plugins/App.World/bin/Debug/net8.0", "assembly": "FantaSim.App.World" }
      ],
      "assemblyNames": [
        "FantaSim.App.World",
        "FantaSim.App.World.FieldView",
        "FantaSim.App.World.Composition",
        "ConcurrentCollections",
        "Dahomey.Cbor",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.ObjectPool",
        "Microsoft.IO.RecyclableMemoryStream",
        "Microsoft.Spatial",
        "Semver",
        "SurrealDb.Embedded.InMemory",
        "SurrealDb.Net",
        "System.Collections.Immutable",
        "System.IO.Pipelines",
        "System.Linq.AsyncEnumerable",
        "System.Reactive",
        "SystemTextJsonPatch",
        "UnifyStorage.Runtime.SurrealDb",
        "Websocket.Client"
      ]
    }
  ]
}
```

- [ ] **Step 6: Parity check BEFORE touching the Taskfile**

```bash
ls project/bundles/world/*.dll | xargs -n1 basename | sort > /tmp/world-before.txt
python3 tools/bundles/stage_bundle.py world
ls project/bundles/world/*.dll | xargs -n1 basename | sort > /tmp/world-after.txt
diff /tmp/world-before.txt /tmp/world-after.txt
```

Expected: empty diff. Any difference means the filter port is wrong — STOP and fix the stager
(compare against the `is_shared_exact`/`is_shared_prefix` shell functions in Taskfile.yml git
history) before proceeding. Repeat the same before/after check for `stage`, `assist`, `timeline`,
`activity` (each currently stages exactly its one root DLL; the stager may ADD a `*.deps.json`
sidecar — that is expected and acceptable, DLL sets must match).

> **Gate outcome (2026-07-08):** stage/assist/activity/world parity EMPTY. `timeline` gained
> `UnifyMaths.Abstractions.dll` — verified as a CORRECT addition, not a filter bug: the assembly
> is in timeline's real deps closure, is NOT policy-shared (only `UnifyMaths` +
> `UnifyMaths.Numerics` are exact-shared; no shared contract references `.Abstractions`), and the
> world bundle already ships it as collectible cargo, windowed-verified. The old hand-copy task
> was silently under-staging — precisely the drift class this tool eliminates. Delta accepted;
> timeline hot-reload re-proven in Task 6.

- [ ] **Step 7: Rewire the Taskfile**

Replace each `bundle:<id>:build` task body. `bundle:stage:build` becomes:

```yaml
  bundle:stage:build:
    desc: Build + stage the App.Stage tier assembly into the bundle source dir (generic stager)
    cmds:
      - python3 tools/bundles/stage_bundle.py stage
```

Same one-liner shape for `bundle:assist:build`, `bundle:timeline:build`, `bundle:activity:build`
(keep their existing `desc` intent), and `bundle:world:build` — DELETE its entire inline shell
pipeline (Taskfile.yml lines 176-294 body) in favor of:

```yaml
  bundle:world:build:
    desc: Build + stage the App.World data-bundle assemblies into the bundle source dir (generic stager)
    cmds:
      - python3 tools/bundles/stage_bundle.py world
```

Add a stager test task next to them:

```yaml
  bundle:stagetool:test:
    desc: Run the generic bundle stager's unit tests
    cmds:
      - python3 -m unittest discover -s tools/bundles -p "test_*.py"
```

Do not touch `bundle:<id>` export tasks, `bundle:iii:*` (data-only, no DLL), `bundles`, or
`bundle:install`.

- [ ] **Step 8: End-to-end task check**

Run: `task bundle:world:build && task bundle:stage:build && task bundle:stagetool:test`
Expected: all succeed; `git status` shows only the intended Taskfile/json/tool changes (bundle
dirs are gitignored or unchanged in DLL content).

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat(bundles): generic deps.json-driven bundle stager

tools/bundles/stage_bundle.py stages every bundle from collectible-bundles.json
'projects' + shared-assembly-policy.json — one filter, no hand mirrors.
World staging parity verified DLL-for-DLL against the old inline script."
```

---

### Task 3: Extract IPlanetPresentation to a T1 contract assembly

**Files:**
- Create: `project/contracts/App.Presentation/App.Presentation.csproj`
- Create: `project/contracts/App.Presentation/AssemblyInfo.cs`
- Create: `project/contracts/App.Presentation/IPlanetPresentation.cs`
- Modify: `project/plugins/App.Presentation/PresentationComposition.cs` (interface removed)
- Modify: `project/plugins/App.Presentation/App.Presentation.csproj` (+ contract ProjectReference)

**Interfaces:**
- Produces: assembly `FantaSim.App.Presentation.Contracts`, namespace `FantaSim.App.Presentation`,
  containing `IPlanetPresentation` EXACTLY as it exists today in
  `project/plugins/App.Presentation/PresentationComposition.cs:14-33` (`Rebind()`,
  `UpdateCutaway(double azimuthDeg, double widthDeg)`, `UpdateExploded(double factor)`,
  `UpdateMantle(bool enabled)`, `: IDisposable`). Namespace is unchanged, so every existing
  consumer keeps compiling.

- [ ] **Step 1: Create the contract project**

`project/contracts/App.Presentation/App.Presentation.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App.Presentation</RootNamespace>
    <AssemblyName>FantaSim.App.Presentation.Contracts</AssemblyName>
    <!-- ServiceArchi Tier 1: host-facing planet-presentation mount surface. The binder (T4 impl)
         lives in plugins/App.Presentation and ships INSIDE world.pck (bundle-maximalism phase 1);
         the resident host talks to it only through this shared contract. No T2 proxy: the world
         bundle's PresentationPlugin registers the instance and the host resolves it per use. -->
    <ServiceArchiTier>T1</ServiceArchiTier>
  </PropertyGroup>

  <ItemGroup>
    <CompilerVisibleProperty Include="ServiceArchiTier" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GiantCroissant.PluginArchi.Extensibility.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: AssemblyInfo with the shared-contract marker**

Run `cat project/contracts/App.Camera/AssemblyInfo.cs` and create
`project/contracts/App.Presentation/AssemblyInfo.cs` with the identical attribute usage
(the `[assembly: PluginSharedContract]` marker from PluginArchi.Extensibility.Abstractions),
adjusting only any comment text to say "planet presentation".

- [ ] **Step 3: Move the interface**

Create `project/contracts/App.Presentation/IPlanetPresentation.cs` by CUTTING lines 9-33 (the
`IPlanetPresentation` interface with its full XML docs) out of
`project/plugins/App.Presentation/PresentationComposition.cs` verbatim, with this header:

```csharp
using System;

namespace FantaSim.App.Presentation;
```

`PresentationComposition.cs` keeps only its `using`s, the namespace line, and the
`PresentationComposition` static factory class. Update the factory's doc comment:

```csharp
/// <summary>
/// Composition entry for the presentation plugin. Bundle-maximalism phase 1: called by the world
/// bundle's PresentationPlugin (same collectible ALC); the resident host consumes only the
/// IPlanetPresentation contract (contracts/App.Presentation).
/// </summary>
```

- [ ] **Step 4: Reference the contract from the plugin, add to sln**

In `project/plugins/App.Presentation/App.Presentation.csproj` add to the existing contracts
ItemGroup:

```xml
    <ProjectReference Include="..\..\contracts\App.Presentation\App.Presentation.csproj" />
```

Then: `dotnet sln project/FantaSim.sln add project/contracts/App.Presentation/App.Presentation.csproj`
(the sln already disambiguates same-named contract/plugin projects — see App.World precedent).

- [ ] **Step 5: Build + full suite (this is a pure move — everything must still compile)**

Run: `dotnet build project/FantaSim.sln -v q -nologo && dotnet test project/FantaSim.sln -v q -nologo`
Expected: PASS. The host still references the plugin at this point — that flips in Task 5.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor(presentation): extract IPlanetPresentation to T1 contract assembly

contracts/App.Presentation (FantaSim.App.Presentation.Contracts), namespace
unchanged so all consumers keep compiling. Prep for the world-bundle move."
```

---

### Task 4: PresentationPlugin — the world bundle owns the binder

**Files:**
- Create: `project/plugins/App.Presentation/PresentationPlugin.cs`
- Modify: `project/plugins/App.Presentation/App.Presentation.csproj` (PluginArchi + DI packages)
- Test: `project/tests/App.Presentation.Tests/PresentationPluginTests.cs`

**Interfaces:**
- Consumes: `IPlanetPresentation` (Task 3), `PresentationComposition.CreatePlanetPresentation(
  IRegistry, FantaSim.App.Resource.IService, IBundleSceneRegistry, ILoggerFactory, string?, bool)`,
  `IRegistry.RegisterOwned<T>(T, ServiceRegistration)` (StagePlugin pattern), PluginArchi
  `[Plugin]`/`ILifecyclePlugin`/`IPluginContext`.
- Produces: `[Plugin("app.presentation")] PresentationPlugin` that registers
  `IPlanetPresentation` on init and disposes/unregisters on shutdown (main-thread-marshalled).
  Internal ctor `PresentationPlugin(Func<IPluginContext, IPlanetPresentation> factory,
  Func<bool>? isOnMainThread)` is the test seam.

- [ ] **Step 1: Write the failing tests**

First inspect the plugin context surface: `grep -n "interface IPluginContext" -r -A 10 ~/.nuget/packages/giantcroissant.pluginarchi.extensibility.abstractions/*/lib/net8.0/ 2>/dev/null || codegraph node IPluginContext` — or read it in
`plate-projects/plugin-archi/dotnet/src/PluginArchi.Extensibility.Abstractions/`. `WorldPlugin`
uses only `context.Services`; implement any other members on the fake by throwing
`NotSupportedException`.

Create `project/tests/App.Presentation.Tests/PresentationPluginTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FantaSim.App.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public class PresentationPluginTests
{
    private sealed class FakePresentation : IPlanetPresentation
    {
        public bool Disposed;
        public void Rebind() { }
        public void UpdateCutaway(double azimuthDeg, double widthDeg) { }
        public void UpdateExploded(double factor) { }
        public void UpdateMantle(bool enabled) { }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeContext : IPluginContext
    {
        public FakeContext(IServiceProvider services) => Services = services;
        public IServiceProvider Services { get; }
        // Implement any further IPluginContext members with `throw new NotSupportedException();`
    }

    private static (PresentationPlugin plugin, IRegistry registry, FakePresentation presentation) Arrange()
    {
        var registry = new ServiceRegistry();
        var presentation = new FakePresentation();
        var services = new ServiceCollection()
            .AddSingleton<IRegistry>(registry)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var plugin = new PresentationPlugin(_ => presentation, isOnMainThread: () => true);
        return (plugin, registry, presentation);
    }

    [Fact]
    public async Task InitializeRegistersThePresentationContract()
    {
        var (plugin, registry, presentation) = Arrange();
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)), default);
        Assert.Same(presentation, registry.TryGet<IPlanetPresentation>());
    }

    [Fact]
    public async Task ShutdownUnregistersAndDisposes()
    {
        var (plugin, registry, presentation) = Arrange();
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)), default);
        await plugin.ShutdownAsync();
        Assert.Null(registry.TryGet<IPlanetPresentation>());
        Assert.True(presentation.Disposed);
    }

    private static IServiceProvider BuildProvider(IRegistry registry)
        => new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
}
```

(If `Arrange()`'s inline provider duplicates `BuildProvider`, simplify to use `BuildProvider`
only — keep the two assertions exactly as written.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test project/FantaSim.sln --filter "FullyQualifiedName~PresentationPluginTests" -v q -nologo`
Expected: build FAILURE — `PresentationPlugin` does not exist.

- [ ] **Step 3: Add the plugin packages**

In `project/plugins/App.Presentation/App.Presentation.csproj`, mirror the PluginArchi/DI
PackageReference lines from `project/plugins/App.World/App.World.csproj` (open it and copy the
exact ids/versions — at minimum `GiantCroissant.PluginArchi.Extensibility.Abstractions` and the
`Microsoft.Extensions.DependencyInjection.Abstractions` it uses for `GetRequiredService`).

- [ ] **Step 4: Write PresentationPlugin**

Create `project/plugins/App.Presentation/PresentationPlugin.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource.Bundle;               // IBundleSceneRegistry
using Godot;                                      // Callable + OS (main-thread marshal on shutdown)
using Microsoft.Extensions.DependencyInjection;   // GetRequiredService
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;     // [Plugin], ILifecyclePlugin, IPluginContext
using ServiceArchi.Contracts;                     // IRegistry, ServiceRegistration
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation;

/// <summary>
/// World-bundle plugin entry for the planet presentation (bundle-maximalism phase 1). Ships INSIDE
/// world.pck — same collectible ALC as the world data service — creates the
/// PlanetPresentationBinder and registers it behind the shared IPlanetPresentation contract. The
/// resident host resolves the contract after the bundle loads, calls Rebind() on the main thread,
/// and wires the render/camera ingress targets; the host must sever those references on world
/// RuntimeChanging (Host.OnResourceRuntimeChanging) or the old ALC never collects.
/// </summary>
[Plugin("app.presentation", Name = "Planet Presentation", Description = "Registers the planet presentation binder behind IPlanetPresentation.", Tags = "domain-bundle")]
public sealed partial class PresentationPlugin : ILifecyclePlugin
{
    private readonly Func<IPluginContext, IPlanetPresentation> _factory;
    private readonly Func<bool> _isOnMainThread;
    private IDisposable? _registration;
    private IPlanetPresentation? _presentation;
    private ILogger? _log;

    public PresentationPlugin()
        : this(CreateDefault, isOnMainThread: null)
    {
    }

    // Test seam: App.Presentation.Tests injects a fake factory + main-thread answer so the
    // lifecycle is verifiable headless (no Godot engine in the test host).
    internal PresentationPlugin(Func<IPluginContext, IPlanetPresentation> factory, Func<bool>? isOnMainThread)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _isOnMainThread = isOnMainThread ?? (static () => OS.GetThreadCallerId() == OS.GetMainThreadId());
    }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _log = loggerFactory.CreateLogger("PresentationPlugin");

        _presentation = _factory(context);
        _registration = registry.RegisterOwned<IPlanetPresentation>(
            _presentation,
            new ServiceRegistration { Tags = new[] { "presentation", "world-bundle" }, Description = "planet presentation binder (world bundle)" });
        _log.LogInformation("PresentationPlugin: IPlanetPresentation registered.");
        return ValueTask.CompletedTask;
    }

    public async ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _registration?.Dispose();
        _registration = null;

        var presentation = _presentation;
        _presentation = null;
        if (presentation is not null)
        {
            // Binder disposal frees Godot nodes — main-thread only. The reload path may run
            // ShutdownAsync off the main thread (RemoveGroupWithDiagnosticsAsync); marshal and WAIT
            // so the unmount completes BEFORE the ALC unloads.
            if (_isOnMainThread())
            {
                presentation.Dispose();
            }
            else
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Callable.From(() =>
                {
                    try
                    {
                        presentation.Dispose();
                        done.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                }).CallDeferred();
                await done.Task.ConfigureAwait(false);
            }
        }

        _log?.LogInformation("PresentationPlugin: shutdown completed.");
        _log = null;
    }

    private static IPlanetPresentation CreateDefault(IPluginContext context)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        var resource = registry.Get<ResourceService>();
        var sceneRegistry = registry.Get<IBundleSceneRegistry>();
        var config = registry.TryGet<CrosscutFoundation.Config.IService>();
        return PresentationComposition.CreatePlanetPresentation(
            registry,
            resource,
            sceneRegistry,
            loggerFactory,
            // M0 (spec D1): globe:plateView=identity (env globe__plateView) keeps the PlateIdentity
            // diagnostic on the geosphere.plate track; default is the Continents membership view.
            config?.Get("globe:plateView"),
            // P4: the world-generation node-graph panel stays env-gated behind world:showGraph.
            config?.GetValue("world:showGraph", false) ?? false);
    }
}
```

(If `registry.Get<T>` returns `object` in this ServiceArchi version, cast as `(ResourceService)`
/ `(IBundleSceneRegistry)` — match whichever style `Host.cs` compiles with today.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test project/FantaSim.sln --filter "FullyQualifiedName~PresentationPluginTests" -v q -nologo`
Expected: PASS, 2 tests.

- [ ] **Step 6: Full suite + commit**

```bash
dotnet test project/FantaSim.sln -v q -nologo
git add -A && git commit -m "feat(presentation): PresentationPlugin — world bundle owns the binder

[Plugin] lifecycle registers IPlanetPresentation via RegisterOwned; shutdown
disposes on the main thread (marshalled) BEFORE the ALC unloads."
```

---

### Task 5: Rewire the host; ship App.Presentation inside world.pck

**Files:**
- Modify: `project/hosts/complete-app/complete-app.csproj:27` (plugin ref → contract ref)
- Modify: `project/hosts/complete-app/Host.cs` (resolve contract; world reload severing/rebind)
- Modify: `project/hosts/complete-app/config/collectible-bundles.json` (world gains presentation)

**Interfaces:**
- Consumes: `IPlanetPresentation` (Task 3), `PresentationPlugin` registration (Task 4), stager
  (Task 2), existing `HandleTimelineBundleReloaded()` / `IBundleReloadHook` machinery in Host.cs.
- Produces: a host with NO compile-time reference to the presentation implementation; world.pck
  carrying `FantaSim.App.Presentation.dll`.

- [ ] **Step 1: Swap the csproj reference**

In `project/hosts/complete-app/complete-app.csproj` replace line 27:

```xml
    <ProjectReference Include="..\..\plugins\App.Presentation\App.Presentation.csproj" />
```

with:

```xml
    <!-- Bundle-maximalism phase 1 (2026-07-08): the presentation binder ships INSIDE world.pck.
         The host compiles against the T1 contract only; the world bundle's PresentationPlugin
         registers the instance behind IPlanetPresentation. -->
    <ProjectReference Include="..\..\contracts\App.Presentation\App.Presentation.csproj" />
```

- [ ] **Step 2: Rewire Host.cs**

All edits in `project/hosts/complete-app/Host.cs`:

**(a)** Add a field next to `_timelineReloadPending` (line 41):

```csharp
    private bool _worldReloadPending;
```

**(b)** Replace `OnResourceRuntimeChanging` (lines 121-128):

```csharp
    private void OnResourceRuntimeChanging(object? sender, FantaSim.App.Resource.ResourceRuntimeChangingEventArgs e)
    {
        if (e.Operation != FantaSim.App.Resource.ResourceRuntimeOperation.Reload)
            return;

        if (string.Equals(e.BundleId, "timeline", StringComparison.OrdinalIgnoreCase))
            _timelineReloadPending = true;

        if (string.Equals(e.BundleId, "world", StringComparison.OrdinalIgnoreCase))
        {
            // Sever every resident->bundle reference BEFORE the old ALC unloads: the render-ingress
            // delegates, the camera orbit target, and the host's contract handle all point at
            // objects typed in the outgoing world ALC.
            _worldReloadPending = true;
            _renderComposition?.SetCutawayTarget(null);
            _renderComposition?.SetExplodedTarget(null);
            _renderComposition?.SetMantleTarget(null);
            _cameraComposition?.SetOrbitTarget(null);
            _planetPresentation = null;
        }
    }
```

**(c)** Replace `OnResourceRuntimeChanged` (lines 130-145):

```csharp
    private void OnResourceRuntimeChanged(object? sender, EventArgs e)
    {
        if (_composition is null)
            return;

        var registry = _composition.Bootstrap.Registry;
        var resource = registry.TryGet<FantaSim.App.Resource.IService>();

        if (_worldReloadPending)
        {
            _worldReloadPending = false;
            if (resource?.IsLoaded("world") == true)
                HandleWorldBundleReloaded();
        }

        if (_timelineReloadPending)
        {
            _timelineReloadPending = false;
            if (resource?.IsLoaded("timeline") == true)
                HandleTimelineBundleReloaded();
        }
    }
```

**(d)** Add next to `HandleTimelineBundleReloaded` (line 154):

```csharp
    private void HandleWorldBundleReloaded()
    {
        if (_composition is null)
            return;

        var registry = _composition.Bootstrap.Registry;
        Callable.From(() =>
        {
            BindPlanetPresentation(registry);
            // The new bundle's binder re-registered ITimelineController; recompose the resident
            // timeline service + face against the new controller instance.
            HandleTimelineBundleReloaded();
        }).CallDeferred();
        _log.LogInformation("world bundle reloaded; presentation rebind scheduled.");
        RecordActivity(ActivityEntryKind.Log, "world.presentation.rebound", "system", "world", outcome: "rebind scheduled");
    }
```

**(e)** Extend the reload hook (the `TimelineReloadHook` nested class, lines 189-205) so the
command-driven path covers world too — rename it `BundleReloadHook` and replace its body:

```csharp
    private sealed class BundleReloadHook : FantaSim.App.Command.IBundleReloadHook
    {
        private readonly Host _host;

        public BundleReloadHook(Host host)
        {
            _host = host;
        }

        public Task AfterReloadAsync(string bundleId, CancellationToken cancellationToken = default)
        {
            if (string.Equals(bundleId, "timeline", StringComparison.OrdinalIgnoreCase))
                _host.HandleTimelineBundleReloaded();
            if (string.Equals(bundleId, "world", StringComparison.OrdinalIgnoreCase))
                _host.HandleWorldBundleReloaded();

            return Task.CompletedTask;
        }
    }
```

and update `RegisterTimelineReloadHook` (line 147) to register `new BundleReloadHook(this)` with
description `"Resident rebind after bundle reload (timeline, world)"` (method rename to
`RegisterBundleReloadHook` + fix the call site at line 69).

**(f)** Replace the tail of `LoadWorldBundleAndMountPlanetAsync` (lines 684-714) — everything from
`if (!resource.IsLoaded("world")) return;` onward becomes:

```csharp
        if (!resource.IsLoaded("world"))
            return;

        BindPlanetPresentation(registry);
    }

    // Bundle-maximalism phase 1: the binder lives INSIDE the world bundle. Its PresentationPlugin
    // creates and owns it; the host only resolves the shared contract, mounts, and wires the
    // ingress targets. The host holds NO owning reference — _planetPresentation is severed on
    // world RuntimeChanging so the old ALC can collect, and disposal belongs to the plugin.
    private void BindPlanetPresentation(IRegistry registry)
    {
        _planetPresentation = registry.TryGet<IPlanetPresentation>();
        if (_planetPresentation is null)
        {
            _log.LogWarning("world bundle loaded but IPlanetPresentation is not registered; planet stays unmounted.");
            return;
        }

        _planetPresentation.Rebind();
        _renderComposition?.SetCutawayTarget(_planetPresentation.UpdateCutaway);
        _renderComposition?.SetExplodedTarget(_planetPresentation.UpdateExploded);
        _renderComposition?.SetMantleTarget(_planetPresentation.UpdateMantle);

        // Mount the default globe camera now that the world bundle has mounted the globe at the
        // origin. Deferred so the pcam is built on the main thread after the scene tree settles.
        if (_cameraComposition is not null)
        {
            Callable.From(() => CameraComposition.MountDefaultGlobeCamera(
                new HostCompositionContext(_composition!),
                this,
                _cameraComposition)).CallDeferred();
        }
    }
```

Note the removed pieces: `_planetPresentation?.Dispose()` (the plugin owns disposal now), the
`PresentationComposition.CreatePlanetPresentation(...)` call, and the two config-knob arguments
(the plugin reads `globe:plateView` / `world:showGraph` itself — Task 4).

**(g)** In `_Notification` (lines 746-777) replace `_planetPresentation?.Dispose();` with
`_planetPresentation = null;` (disposal runs via the plugin host teardown in
`_composition?.Dispose()`).

- [ ] **Step 3: Register the presentation assembly as world-bundle cargo**

In `project/hosts/complete-app/config/collectible-bundles.json`, world entry:
- append `"FantaSim.App.Presentation"` to `assemblyNames` (it now loads into the world ALC
  instead of matching the shared `FantaSim.App.` prefix);
- determine the plugin's build output dir: `dotnet build project/plugins/App.Presentation/App.Presentation.csproj -c Debug -v q -nologo`
  then `find project/plugins/App.Presentation -name "FantaSim.App.Presentation.dll" -newer project/plugins/App.Presentation/App.Presentation.csproj`
  — Godot.NET.Sdk projects emit either `bin/Debug/net8.0` or `.godot/mono/temp/bin/Debug`; use
  whichever path the find returns;
- append to `projects`:

```json
        { "csproj": "project/plugins/App.Presentation/App.Presentation.csproj", "output": "<the dir found above>", "assembly": "FantaSim.App.Presentation" }
```

- [ ] **Step 4: Build, stage, and audit the closure**

```bash
dotnet build project/FantaSim.sln -v q -nologo
dotnet test project/FantaSim.sln -v q -nologo
python3 tools/bundles/stage_bundle.py world
ls project/bundles/world/ | sort
```

Expected: suite green; the world bundle now additionally contains
`FantaSim.App.Presentation.dll` (+ its deps.json) and NOTHING ELSE new — its other references
(App.NodeGraph, App.Ui.NodeGraph, contracts, Cartography, RegistryArchi) are all shared-resident
and must be filtered out. If any extra DLL appears, list it, decide shared-vs-collectible against
`vault/architecture/cross-alc-rules.md`, and either add it to world `assemblyNames` (collectible)
or stop and flag for review.

- [ ] **Step 5: The transitive-drop check (2026-07-03 lesson: cutting a ProjectReference silently
drops transitive assemblies from the host export)**

```bash
ls project/hosts/complete-app/.godot/mono/temp/bin/Debug/ | sort > /tmp/host-after.txt
git stash && dotnet build project/hosts/complete-app/complete-app.csproj -c Debug -v q -nologo \
  && ls project/hosts/complete-app/.godot/mono/temp/bin/Debug/ | sort > /tmp/host-before.txt; git stash pop
dotnet build project/hosts/complete-app/complete-app.csproj -c Debug -v q -nologo
diff /tmp/host-before.txt /tmp/host-after.txt
```

Expected: the ONLY removals are `FantaSim.App.Presentation.dll`/`.pdb` (now bundle cargo).
Any other removed assembly was reaching the host transitively through the cut reference — pin it
with an explicit `PackageReference`/`ProjectReference` in `complete-app.csproj` (the
`GiantCroissant.CrosscutFoundation.Persistence.Abstractions` precedent at csproj lines 70-78).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(bundles): presentation ships inside world.pck (phase 1)

Host resolves IPlanetPresentation from the registry, severs render/camera
delegates + contract handle on world RuntimeChanging, and rebinds (incl.
timeline recompose) after reload. complete-app references the T1 contract only."
```

---

> **Task 4/5 execution notes (2026-07-08):**
> - Task 4's agent swapped the host csproj to the contract reference early (NU1107 forced it),
>   leaving commit `3ff5152` with a broken host build that the sln config masks — Task 5's Host.cs
>   rewiring restored it. Lesson: `dotnet test FantaSim.sln` green does NOT prove the host
>   compiles; always build `complete-app.csproj` explicitly.
> - `SeamConfigBanTests` bans `CrosscutFoundation.Config` inside `plugins/App.Presentation`, so
>   the plan's config reads in `PresentationPlugin.CreateDefault` were replaced by a shared
>   `PlanetPresentationOptions` record in `contracts/App.Presentation`: the HOST reads
>   `globe:plateView` / `world:showGraph` and registers the options before the world bundle loads
>   (`Host.RegisterPresentationOptions`); the plugin resolves it with a `Default` fallback.
> - The binder gained `{Message}` args on five `LogError` calls (agent build-fix; behavior-equivalent).
> - Closure audit result: world bundle 50 → 51 assemblies, sole addition
>   `FantaSim.App.Presentation.dll`. Host output drop check: only the presentation impl left the
>   host; `CrosscutFoundation.Persistence.Contracts.dll` (package id `...Persistence.Abstractions`)
>   and both MessagePack pins still present.

### Task 6: Windowed verification gate (in-session, NOT delegated)

Per `.agent/rules/bundle-runtime-verification.md` + the `verify-windowed` skill. This gate is
eye-judged and requires the live exported app; it stays with the lead session.

- [ ] **Step 1:** `task build && task bundles && task bundle:install && task run:exported`
      (use the exact task names from `verify-windowed` — read the skill first).
- [ ] **Step 2:** Boot sanity in the log: `composition activated`, `entered scene 'stage'`,
      `IPlanetPresentation registered`, planet visible, mantle layer + cutaway drive via
      `tools/fantasim-cmd.py` (seek → select_layer → screenshot).
- [ ] **Step 3:** Hot-reload the world bundle in the LIVE app: touch a presentation constant
      (e.g. a color in `PlateSurfaceMaterialTuning.cs`), `task bundle:world && task bundle:install`,
      watch-reload, then confirm ALL of: `old ALC collected` in the log; the visual change is on
      screen; timeline scrub still works (controller rebound); no Godot main-thread errors during
      unmount.
- [ ] **Step 4:** Repeat step 3 once more (second reload proves the previous ALC fully collected).
- [ ] **Step 5:** Record the evidence (log excerpts + screenshots) in a dated handover note under
      `vault/handover/`, update the spec's phase table, and merge the branch.

## Deferred (explicitly NOT in this plan)

- The polarity flip of `shared-assembly-policy.json` (contracts-only sharing) — after Task 6 proves.
- Shrinking the Cartography/App.World.Rendering shared closure — only meaningful post-flip.
- Phase 2 (Timeline T3 → timeline bundle) — next plan; deletes the Host rebind machinery this plan
  extends.
