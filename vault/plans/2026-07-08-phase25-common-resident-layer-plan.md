# Phase 2.5 — Common Resident-Layer Bundle Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.
>
> **Executor constraints (codex sandbox, learned phase 2):** codex can build (`dotnet build
> --disable-build-servers -p:UseSharedCompilation=false`) and run **python** unittests, but can
> NEVER run `dotnet test` (VSTest TCP denied) and can NEVER commit (`.git` read-only) or run the
> windowed app. Write the xunit tests anyway; the LEAD session runs them and commits. Tasks marked
> **[LEAD]** are lead-session-only (windowed gates, export pipeline).

**Goal:** the exe's pure-support assemblies (first cut: 30+ DLLs of the measured 119 movable) ship
in a `common.pck` loaded ONCE at boot into the Default ALC, so a C# change in those libraries no
longer requires re-exporting the app.

**Architecture:** a loader (`CommonResidentLayerBootstrap.EnsureLoaded()`, first statement of
`Host._Ready`) mounts `<exeDir>/bundles/common.pck`, validates it against a generated expected
catalog, extracts every manifest DLL via `BundleExtractor`, installs an
`AssemblyLoadContext.Default.Resolving` hook, then preloads in manifest order. A packer
(`stage_bundle.py --stage-common`) stages the policy-listed DLLs + manifest; a strip tool removes
them from the exported app's per-arch data dirs, writes the expected catalog, and installs
`common.pck` — so a stripped app never exists without its layer. Packaging granularity only:
common.pck is **never collectible** (spec: PluginArchi is two-tier).

**Tech Stack:** C# (.NET 8, Godot 4 seam), Python 3 (packer/strip, stdlib only), go-task, Godot
export presets.

**Source docs:** [design brief](2026-07-08-phase25-loader-design-brief.md) (S1–S4 sequence),
[spec](../specs/2026-07-08-common-resident-layer-bundle.md). E1/E2 verdicts constrain everything:
Godot-facing assemblies STAY in the exe (E1); lazy loading works, first demand is late (E2).

## Decisions (resolving the brief's open questions + deviations)

- **D1 (brief open Q1):** the expected catalog lives at `<exeDir>/config/common-resident-expected.json`,
  read with `System.IO` (the loader runs before any composition/res:// plumbing; `res://config`
  parity is not needed).
- **D2 (brief open Q2):** the STRIP tool installs `common.pck` into the app as its final step —
  a stripped app never exists without its resident layer. `bundle:install` ALSO copies it for the
  dev iterate loop (idempotent same-file copy).
- **D3 (deviation from brief §C):** the strip runs as a Taskfile post-step of `build:godot:desktop`
  THIS phase. Upstreaming into `IUnifyGodot.ExportDesktopPlatform` is deferred to the queue
  (cross-repo unify-build version/publish cycle; the Taskfile is the only export entry point used
  today — prove the contract in-repo first).
- **D4:** gate identity = `{assemblyName, sha256}`. `assemblyVersion` is optional-informational
  (stdlib Python cannot read .NET assembly versions; sha256 subsumes version).
- **D5:** the `--check-dual` extension covers bundle∩common and bundle∩bundle. exe∩common is
  enforced by the strip tool itself (post-strip verify: zero manifest DLLs remain in either
  per-arch data dir).
- **D6:** Godot-facing detector = ASCII scan for `GodotSharp` in the DLL bytes (assembly refs are
  ASCII in CLI metadata). Detector-gated entries (`BoomHud.Abstractions`, `BoomHud.Foundation`)
  are skipped with a warning on veto; a veto on any other listed assembly is a `StagingError`.
- **D7:** provisioning matrix in the loader (see Task 3): neither expected-file nor pck → skip
  (editor/unstripped run); expected without pck → FATAL from day one; pck without expected →
  load with warning until Task 11 (S4) flips it to FATAL.

## Global Constraints

- `common.pck` loads into `AssemblyLoadContext.Default` ONLY — never a collectible ALC.
- `EnsureLoaded()` is the FIRST statement of `Host._Ready` — before `GD.Print`,
  `AppComposition.Activate()`, `BuildPluginHost`, every composition call.
- The `Resolving` hook is installed BEFORE any preload (brief RISKS: dependency order).
- Boot failure reporting pre-composition: `GD.PrintErr` + `OS.Alert` + throw
  `InvalidOperationException` (`_log` does not exist yet).
- `shared-assembly-policy.json` stays the single source of truth (Bootstrap sharing lists
  untouched; new `common` section is additive).
- All dotnet builds in scripts: `--disable-build-servers -p:UseSharedCompilation=false`
  (stage_bundle.py already does this — keep it).
- Godot-facing assemblies (`GodotSharp` reference) never enter common.pck (E1).
- Deferred from common (spec): `GodotSharp*`, `Godot.NET.Sdk` seam/script assemblies,
  `PluginArchi.*`, `ServiceArchi.*`, `RegistryArchi.*`, `DependencyArchi.*`,
  `CrosscutFoundation.*`, `App.Common`, `App.Resource`, `App.Resource.Bundle.Seam`,
  `App.SceneFlow`, `App.Command`, `complete-app`, non-contract `FantaSim.App.*` domain modules.
- Commits use Conventional Commits; never `--no-verify`; the LEAD commits (executor cannot).

---

### Task 1: `CommonResidentCatalog` — pure identity/validation model

**Files:**
- Create: `project/plugins/App.Resource/CommonResidentCatalog.cs`
- Test: `project/tests/App.Resource.Tests/CommonResidentCatalogTests.cs`

**Interfaces:**
- Consumes: nothing (pure; System.Text.Json only).
- Produces: `CommonAssemblyIdentity(string AssemblyName, string Sha256)`;
  `CommonResidentCatalog.ParseExpected(string json) -> IReadOnlyList<CommonAssemblyIdentity>`;
  `CommonResidentCatalog.Validate(IReadOnlyList<CommonAssemblyIdentity> expected,
  IReadOnlyList<CommonAssemblyIdentity> actual) -> IReadOnlyList<string>` (empty = valid).
  Task 3's bootstrap and Task 6's strip tool both target this shape.

- [ ] **Step 1: Write the failing tests**

```csharp
// project/tests/App.Resource.Tests/CommonResidentCatalogTests.cs
using System.Linq;
using FantaSim.App.Resource;
using Xunit;

namespace App.Resource.Tests;

public sealed class CommonResidentCatalogTests
{
    private const string ExpectedJson = """
    {
      "bundleId": "common",
      "assemblies": [
        { "assemblyName": "Arch", "sha256": "aa11" },
        { "assemblyName": "MessagePack", "sha256": "bb22" }
      ]
    }
    """;

    [Fact]
    public void ParseExpectedReadsIdentities()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        Assert.Equal(2, expected.Count);
        Assert.Equal("Arch", expected[0].AssemblyName);
        Assert.Equal("aa11", expected[0].Sha256);
    }

    [Fact]
    public void ValidateAcceptsExactMatchAnyOrder()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        var actual = expected.Reverse().ToList();
        Assert.Empty(CommonResidentCatalog.Validate(expected, actual));
    }

    [Fact]
    public void ValidateReportsMissingExtraAndHashMismatch()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        var actual = new[]
        {
            new CommonAssemblyIdentity("Arch", "DIFFERENT"),
            new CommonAssemblyIdentity("Newtonsoft.Json", "cc33"),
        };
        var errors = CommonResidentCatalog.Validate(expected, actual);
        Assert.Contains(errors, e => e.Contains("hash mismatch") && e.Contains("Arch"));
        Assert.Contains(errors, e => e.Contains("missing") && e.Contains("MessagePack"));
        Assert.Contains(errors, e => e.Contains("unexpected") && e.Contains("Newtonsoft.Json"));
    }

    [Fact]
    public void ParseExpectedRejectsGarbage()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => CommonResidentCatalog.ParseExpected("not json"));
    }
}
```

- [ ] **Step 2: Verify it fails to build** (type not defined)

Run: `dotnet build project/tests/App.Resource.Tests/App.Resource.Tests.csproj -v q -nologo --disable-build-servers -p:UseSharedCompilation=false`
Expected: FAIL — `CommonResidentCatalog` not found.

- [ ] **Step 3: Implement**

```csharp
// project/plugins/App.Resource/CommonResidentCatalog.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantaSim.App.Resource;

public sealed record CommonAssemblyIdentity(string AssemblyName, string Sha256);

/// <summary>
/// Pure identity model for the common resident-layer gate: the stripped exe's generated
/// expectation (config/common-resident-expected.json) vs what common.pck's manifest declares.
/// Identity is {assemblyName, sha256}; mismatch of any kind is boot-fatal in the caller.
/// </summary>
public static class CommonResidentCatalog
{
    private sealed class ExpectedFile
    {
        [JsonPropertyName("bundleId")]
        public string BundleId { get; set; } = string.Empty;

        [JsonPropertyName("assemblies")]
        public List<ExpectedEntry> Assemblies { get; set; } = new();
    }

    private sealed class ExpectedEntry
    {
        [JsonPropertyName("assemblyName")]
        public string AssemblyName { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    public static IReadOnlyList<CommonAssemblyIdentity> ParseExpected(string json)
    {
        var file = JsonSerializer.Deserialize<ExpectedFile>(json)
            ?? throw new JsonException("expected-catalog json deserialized to null");
        var result = new List<CommonAssemblyIdentity>(file.Assemblies.Count);
        foreach (var entry in file.Assemblies)
        {
            if (string.IsNullOrWhiteSpace(entry.AssemblyName) || string.IsNullOrWhiteSpace(entry.Sha256))
                throw new JsonException("expected-catalog entry missing assemblyName or sha256");
            result.Add(new CommonAssemblyIdentity(entry.AssemblyName, entry.Sha256));
        }
        return result;
    }

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<CommonAssemblyIdentity> expected,
        IReadOnlyList<CommonAssemblyIdentity> actual)
    {
        var errors = new List<string>();
        var actualByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in actual)
            actualByName[a.AssemblyName] = a.Sha256;

        var expectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in expected)
        {
            expectedNames.Add(e.AssemblyName);
            if (!actualByName.TryGetValue(e.AssemblyName, out var actualSha))
                errors.Add($"missing from common.pck: {e.AssemblyName}");
            else if (!string.Equals(e.Sha256, actualSha, StringComparison.OrdinalIgnoreCase))
                errors.Add($"hash mismatch for {e.AssemblyName}: expected {e.Sha256}, pck has {actualSha}");
        }

        foreach (var a in actual)
        {
            if (!expectedNames.Contains(a.AssemblyName))
                errors.Add($"unexpected assembly in common.pck: {a.AssemblyName}");
        }

        return errors;
    }
}
```

- [ ] **Step 4: Build green**

Run: `dotnet build project/tests/App.Resource.Tests/App.Resource.Tests.csproj -v q -nologo --disable-build-servers -p:UseSharedCompilation=false`
Expected: PASS. *(xunit execution is a [LEAD] gate: `dotnet test project/tests/App.Resource.Tests/App.Resource.Tests.csproj` → 4 new tests pass.)*

---

### Task 2: `BundleExtractor.ExtractAllManaged`

**Files:**
- Modify: `project/plugins/App.Resource.Bundle.Seam/BundleExtractor.cs`

**Interfaces:**
- Consumes: existing `NewBundleTempDir`, `BundleEntry`, `BundleExtractionContext`.
- Produces: `ExtractAllManaged(string bundleResPath, IReadOnlyList<string> dllFileNames)
  -> IReadOnlyList<(string FileName, string ExtractedPath)>` — throws
  `InvalidOperationException` naming every missing/unextractable DLL. Task 3 consumes this.

No headless test is possible (Godot `FileAccess`/`DirAccess`); the S1 windowed gate (Task 7)
exercises it. Keep the method a thin composition of the existing pieces.

- [ ] **Step 1: Implement** — add to `BundleExtractor` (after `Extract`, before `NewBundleTempDir`):

```csharp
    /// <summary>
    /// Extracts exactly the named DLLs from the bundle into one temp session dir. Unlike
    /// Extract() this returns EVERY extracted path (the common resident layer preloads all of
    /// them into the Default ALC); any missing or unextractable DLL is an error, not a skip.
    /// </summary>
    public IReadOnlyList<(string FileName, string ExtractedPath)> ExtractAllManaged(
        string bundleResPath, IReadOnlyList<string> dllFileNames)
    {
        var bundleTempDir = NewBundleTempDir(bundleResPath);
        var context = new BundleExtractionContext(bundleTempDir);
        var extracted = new List<(string, string)>(dllFileNames.Count);
        var missing = new List<string>();

        foreach (var fileName in dllFileNames)
        {
            var entry = new BundleEntry(bundleResPath + "/" + fileName, fileName);
            var path = GodotFileAccess.FileExists(entry.ResPath) ? context.TryExtract(entry) : null;
            if (path is null)
                missing.Add(fileName);
            else
                extracted.Add((fileName, path));
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"common bundle extraction failed for: {string.Join(", ", missing)} (bundle {bundleResPath})");

        return extracted;
    }
```

- [ ] **Step 2: Build green**

Run: `dotnet build project/plugins/App.Resource.Bundle.Seam/App.Resource.Bundle.Seam.csproj -v q -nologo --disable-build-servers -p:UseSharedCompilation=false`
Expected: PASS.

---

### Task 3: `CommonResidentLayerBootstrap` + Host wiring

**Files:**
- Create: `project/plugins/App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs`
- Modify: `project/hosts/complete-app/Host.cs:44-46` (first statement of `_Ready`)

**Interfaces:**
- Consumes: `BundleVfs.LoadPck/ReadManifest`, `BundleExtractor.ExtractAllManaged` (Task 2),
  `CommonResidentCatalog` (Task 1), `BundleManifest.Managed?.Assemblies`
  (`ManagedAssembly.Id/Uri/Metadata`, metadata keys `assemblyName`/`sha256` per Task 4's packer).
- Produces: `CommonResidentLayerBootstrap.EnsureLoaded()` — idempotent, thread-safe, boot-fatal
  on integrity failure.

- [ ] **Step 1: Implement the bootstrap**

```csharp
// project/plugins/App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using FantaSim.App.Resource;
using Godot;

namespace FantaSim.App.Resource.Bundle.CommonResidentLayer;

/// <summary>
/// Loads bundles/common.pck into the DEFAULT AssemblyLoadContext before any composition runs.
/// Called as the FIRST statement of Host._Ready. Never touches collectible ALC machinery —
/// the common layer is packaging granularity, not hot-reload (spec: PluginArchi is two-tier).
/// </summary>
public static class CommonResidentLayerBootstrap
{
    private const string BundleResPath = "res://bundles/common";

    private static readonly object Gate = new();
    private static bool _loaded;
    private static Dictionary<string, string>? _extractedByName;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded)
                return;

            var baseDir = AppContext.BaseDirectory;
            var pckPath = Path.Combine(baseDir, "bundles", "common.pck");
            var expectedPath = Path.Combine(baseDir, "config", "common-resident-expected.json");
            var hasPck = File.Exists(pckPath);
            var hasExpected = File.Exists(expectedPath);

            // Provisioning matrix (plan D7): neither → unstripped exe or editor run, skip.
            if (!hasPck && !hasExpected)
            {
                GD.Print("[CommonResidentLayer] no common.pck and no expectation file — unstripped run; skipping.");
                return;
            }

            // Expected without pck = a stripped exe missing its resident layer. Always fatal.
            if (!hasPck)
                Fail($"common.pck missing at {pckPath} but {expectedPath} exists — the exe was stripped; reinstall common.pck");

            if (!new BundleVfs().LoadPck(pckPath))
                Fail($"ProjectSettings.LoadResourcePack failed for {pckPath}");

            var manifest = new BundleVfs().ReadManifest(BundleResPath);
            if (manifest?.Managed?.Assemblies is not { Count: > 0 } assemblies)
                Fail($"common.pck has no manifest.json with managed.assemblies under {BundleResPath}");

            // Resolving hook BEFORE any load (brief RISKS: dependency order — a preload's
            // dependency may resolve before its own preload turn).
            AssemblyLoadContext.Default.Resolving += OnDefaultResolving;

            var dllNames = assemblies!.Select(a => Path.GetFileName(a.Uri)).ToList();
            var extracted = new BundleExtractor().ExtractAllManaged(BundleResPath, dllNames);
            _extractedByName = extracted.ToDictionary(
                e => Path.GetFileNameWithoutExtension(e.FileName),
                e => e.ExtractedPath,
                StringComparer.Ordinal);

            // Integrity gate: extracted bytes vs manifest sha256, then manifest vs expectation.
            var actual = new List<CommonAssemblyIdentity>(assemblies.Count);
            foreach (var entry in assemblies)
            {
                var name = entry.Metadata.TryGetValue("assemblyName", out var n)
                    ? n
                    : Path.GetFileNameWithoutExtension(entry.Uri);
                var declaredSha = entry.Metadata.TryGetValue("sha256", out var s) ? s : string.Empty;
                var path = _extractedByName[Path.GetFileNameWithoutExtension(Path.GetFileName(entry.Uri))];
                var fileSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!string.IsNullOrEmpty(declaredSha)
                    && !string.Equals(declaredSha, fileSha, StringComparison.OrdinalIgnoreCase))
                {
                    Fail($"common.pck integrity: {name} extracted sha {fileSha} != manifest sha {declaredSha}");
                }
                actual.Add(new CommonAssemblyIdentity(name, fileSha));
            }

            if (hasExpected)
            {
                var expected = CommonResidentCatalog.ParseExpected(File.ReadAllText(expectedPath));
                var errors = CommonResidentCatalog.Validate(expected, actual);
                if (errors.Count > 0)
                    Fail("common resident-layer gate: " + string.Join("; ", errors));
            }
            else
            {
                // S1/S2 manual mode. Task 11 (S4) makes this FATAL.
                GD.Print("[CommonResidentLayer] WARNING: loading common.pck without an expectation file (pre-S4 manual mode).");
            }

            // Preload in manifest order. Skip names the Default ALC already has: an unstripped
            // exe copy wins, and a duplicate LoadFromAssemblyPath of the same simple name would
            // create a type-identity split (the MessagePack lesson, 2026-07-02).
            var alreadyLoaded = new HashSet<string>(
                AssemblyLoadContext.Default.Assemblies.Select(a => a.GetName().Name ?? string.Empty),
                StringComparer.Ordinal);
            var loadedCount = 0;
            foreach (var (fileName, path) in extracted)
            {
                var simpleName = Path.GetFileNameWithoutExtension(fileName);
                if (alreadyLoaded.Contains(simpleName))
                {
                    GD.Print($"[CommonResidentLayer] {simpleName} already loaded in Default ALC (unstripped exe copy) — pck copy skipped.");
                    continue;
                }
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                loadedCount++;
            }

            _loaded = true;
            GD.Print($"[CommonResidentLayer] loaded {loadedCount}/{extracted.Count} assemblies from common.pck.");
        }
    }

    private static Assembly? OnDefaultResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var map = _extractedByName;
        if (map is null || name.Name is null)
            return null;
        return map.TryGetValue(name.Name, out var path) ? context.LoadFromAssemblyPath(path) : null;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn] // definite-assignment analysis relies on this
    private static void Fail(string message)
    {
        // _log does not exist yet (AppComposition has not run) — GD channels only.
        GD.PrintErr("[CommonResidentLayer] FATAL: " + message);
        OS.Alert(message, "common resident layer");
        throw new InvalidOperationException("[CommonResidentLayer] " + message);
    }
}
```

- [ ] **Step 2: Wire Host._Ready** — in `project/hosts/complete-app/Host.cs`, make it the FIRST
  statement (before `GD.Print("[Host] composition root starting...")`):

```csharp
    public override void _Ready()
    {
        FantaSim.App.Resource.Bundle.CommonResidentLayer.CommonResidentLayerBootstrap.EnsureLoaded();

        GD.Print("[Host] composition root starting...");
```

- [ ] **Step 3: Build gates**

Run (each, expect PASS):
```
dotnet build project/plugins/App.Resource.Bundle.Seam/App.Resource.Bundle.Seam.csproj -v q -nologo --disable-build-servers -p:UseSharedCompilation=false
dotnet build project/hosts/complete-app/complete-app.csproj -v q -nologo --disable-build-servers -p:UseSharedCompilation=false
```

---

### Task 4: Packer — `stage_bundle.py --stage-common`

**Files:**
- Modify: `project/hosts/complete-app/config/shared-assembly-policy.json` (add `common` section)
- Modify: `tools/bundles/stage_bundle.py`
- Test: `tools/bundles/test_stage_common.py`

**Interfaces:**
- Consumes: policy `common` section; host output dir DLLs (`HOST_OUTPUT_DIR`).
- Produces: `project/bundles/common/<name>.dll` + `project/bundles/common/manifest.json`
  (`bundleId:"common"`, `metadata.bundleType:"resident-layer"`, `managed.assemblies[]` with
  `metadata.assemblyName` + `metadata.sha256`); functions `common_candidates(policy, host_dir)`,
  `is_godot_facing(dll_path)`, `stage_common(policy)`. Tasks 5/6 consume the staged dir + manifest.

- [ ] **Step 1: Add the `common` section to the policy** (S1 scope — Arch only; Task 9 expands):

```json
  "common": {
    "comment": "Assemblies packed into common.pck (resident layer, Default ALC, packaging granularity only). Stripped from the exported exe by tools/bundles/strip_common_from_export.py. S1 scope = Arch; S2 expands to the first-cut list.",
    "exactMatches": [
      "Arch"
    ],
    "prefixes": [],
    "suffixRules": [],
    "detectorGated": []
  }
```

(Insert after the `"prefixes"` array, before the closing brace, keeping existing content intact.)

- [ ] **Step 2: Write the failing python tests**

```python
# tools/bundles/test_stage_common.py
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

    def test_stage_common_writes_manifest_with_sha(self):
        host = self._host_dir(["Arch", "MessagePipe", "FantaSim.App.Timeline.Contracts"])
        # gated entry present but Godot-facing → skipped with warning, not error
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


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 3: Run to verify failure**

Run: `cd tools/bundles && python3 -m unittest test_stage_common -v`
Expected: FAIL — `stage_bundle` has no attribute `common_candidates`.

- [ ] **Step 4: Implement in `stage_bundle.py`** (add after `check_dual`, before `main`):

```python
import hashlib  # add to the imports block at the top

COMMON_BUNDLE_ID = "common"


def _common_policy(policy):
    section = policy.get("common")
    if not section:
        raise StagingError("shared-assembly-policy.json has no 'common' section")
    return section


def _matches_common(name, section):
    if name in set(section.get("exactMatches", [])):
        return True
    if any(name.startswith(p) for p in section.get("prefixes", [])):
        return True
    for rule in section.get("suffixRules", []):
        if name.startswith(rule["prefix"]) and name.endswith(rule["suffix"]):
            return True
    return False


def common_candidates(policy, host_dir):
    """Every host-output DLL selected by the policy's common section.

    Each exactMatch MUST be present (a listed assembly the host no longer ships is a config
    bug, not a skip); prefix/suffix rules match whatever exists."""
    section = _common_policy(policy)
    host_dir = Path(host_dir)
    found = {p.stem: p for p in host_dir.glob("*.dll")}
    missing = [n for n in section.get("exactMatches", []) if n not in found]
    if missing:
        raise StagingError(
            f"common exactMatches not present in host output {host_dir}: {', '.join(missing)}")
    return sorted(
        (p for name, p in found.items() if _matches_common(name, section)),
        key=lambda p: p.stem)


def is_godot_facing(dll_path):
    """E1 rule: Godot-facing assemblies never enter common.pck. Assembly references are
    ASCII in CLI metadata, so a byte scan for GodotSharp is a reliable reject signal."""
    return b"GodotSharp" in Path(dll_path).read_bytes()


def stage_common_from_dir(policy, host_dir, dest):
    section = _common_policy(policy)
    gated = set(section.get("detectorGated", []))
    dest = Path(dest)
    dest.mkdir(parents=True, exist_ok=True)
    clean_bundle_dir(dest)

    entries = []
    for dll in common_candidates(policy, host_dir):
        if is_godot_facing(dll):
            if dll.stem in gated:
                print(f"[stage_bundle] common: {dll.stem} is Godot-facing — detector-gated, SKIPPED")
                continue
            raise StagingError(
                f"common candidate {dll.stem} references GodotSharp — Godot-facing assemblies "
                f"never enter common.pck (E1); remove it from the policy's common section")
        shutil.copy2(dll, dest)
        sha = hashlib.sha256(dll.read_bytes()).hexdigest()
        entries.append({
            "id": dll.stem,
            "uri": f"res://bundles/common/{dll.name}",
            "kind": "dll",
            "metadata": {"assemblyName": dll.stem, "sha256": sha},
        })

    manifest = {
        "bundleId": COMMON_BUNDLE_ID,
        "displayName": "Common resident layer",
        "version": "0.1.0",
        "metadata": {"bundleType": "resident-layer"},
        "managed": {"assemblies": entries},
    }
    (dest / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"[stage_bundle] common: staged {len(entries)} assemblies into {dest}")
    return manifest


def stage_common(policy):
    if not HOST_OUTPUT_DIR.is_dir():
        raise StagingError(
            f"host output not built ({HOST_OUTPUT_DIR}) — build complete-app.csproj first")
    return stage_common_from_dir(policy, HOST_OUTPUT_DIR, BUNDLES_DIR / COMMON_BUNDLE_ID)
```

And in `main()`, add the flag and dispatch (after the `--check-dual` argument definition):

```python
    parser.add_argument("--stage-common", action="store_true",
                        help="stage the common resident-layer bundle (policy 'common' section) + manifest")
```

and immediately after `policy = load_json(POLICY_PATH)`:

```python
    if args.stage_common:
        stage_common(policy)
        if not args.bundles and not args.all and not args.check_dual:
            return 0
```

- [ ] **Step 5: Tests green**

Run: `cd tools/bundles && python3 -m unittest test_stage_common -v`
Expected: PASS (5 tests). Also run the existing suite:
`python3 -m unittest discover -s tools/bundles -p "test_*.py"` — all green.

---

### Task 5: Export preset + Taskfile targets

**Files:**
- Modify: `project/hosts/content-app/export_presets.cfg` (append a `common PCK` preset)
- Modify: `Taskfile.yml` (add `bundle:common:build`, `bundle:common`; extend `bundle:install`, `bundles`)

**Interfaces:**
- Consumes: Task 4's staged `project/bundles/common/`.
- Produces: `build/_artifacts/<v>/godot/bundles/common.pck`; `task bundle:common`;
  `bundle:install` copies `common.pck`. Task 6/7 consume the pck.

- [ ] **Step 1: Append the preset** to `project/hosts/content-app/export_presets.cfg`. Copy the
  LAST existing `[preset.N]` block pair verbatim (e.g. the `iii PCK` one), increment `N` by 1 in
  both `[preset.N]` and `[preset.N.options]` headers, and set exactly these keys in the copied
  block (leave every other copied key unchanged):

```
name="common PCK"
export_filter="customized"
include_filter="bundles/common/*.json,bundles/common/*.dll"
exclude_filter=""
export_path="../../../build/bundles/common.pck"
```

- [ ] **Step 2: Taskfile targets** — insert after `bundle:iii:` (keeping style identical to the
  siblings):

```yaml
  bundle:common:build:
    desc: Stage the common resident-layer assemblies + manifest (policy 'common' section)
    cmds:
      - python3 tools/bundles/stage_bundle.py --stage-common

  bundle:common:
    desc: Stage + export the common resident-layer PCK (manifest + dlls; Default-ALC, never collectible)
    deps: [bundle:link, bundle:common:build, artifacts:latest]
    cmds:
      - mkdir -p {{.BUILD_DIR}}/_artifacts/{{.ARTIFACTS_VERSION}}/godot/bundles
      - '{{.GODOT}} --headless --path {{.CONTENT_PROJECT}} --export-pack "common PCK" {{.ROOT_DIR}}/{{.BUILD_DIR}}/_artifacts/{{.ARTIFACTS_VERSION}}/godot/bundles/common.pck'
```

Extend `bundles:` deps: `[bundle:stage, bundle:assist, bundle:timeline, bundle:activity, bundle:world, bundle:iii, bundle:common]`.

Extend `bundle:install` cmds (after the `iii.pck` line):

```yaml
      - cp "{{.PCKS}}/common.pck" "{{.MACOS}}/bundles/common.pck"
```

- [ ] **Step 3: Verify** — `task bundle:common` produces
  `build/_artifacts/<v>/godot/bundles/common.pck` (S1 content: `manifest.json` + `Arch.dll`).
  *(Executor without Godot access: verify `python3 tools/bundles/stage_bundle.py --stage-common`
  stages `project/bundles/common/{Arch.dll,manifest.json}` and leave the export to the [LEAD].)*

---

### Task 6: Strip tool

**Files:**
- Create: `tools/bundles/strip_common_from_export.py`
- Test: `tools/bundles/test_strip_common.py`

**Interfaces:**
- Consumes: Task 4's `manifest.json`; the exported `.app`.
- Produces: per-arch data dirs stripped of every manifest DLL; verified empty intersection;
  `<MacOS>/config/common-resident-expected.json` (Task 1 schema); `common.pck` installed into
  `<MacOS>/bundles/` (plan D2). CLI:
  `python3 tools/bundles/strip_common_from_export.py --app <path.app> --manifest <manifest.json> --assembly complete-app --common-pck <common.pck>`.

- [ ] **Step 1: Write the failing tests**

```python
# tools/bundles/test_strip_common.py
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
        # missing everywhere is tolerated (already-stripped rerun) — idempotent
        strip.run(app, self._manifest_path(tmp), "complete-app", pck)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run to verify failure**

Run: `cd tools/bundles && python3 -m unittest test_strip_common -v`
Expected: FAIL — no module `strip_common_from_export`.

- [ ] **Step 3: Implement**

```python
#!/usr/bin/env python3
"""Strip the common resident-layer DLLs from an exported .app and provision it.

Godot's C# export packages the whole publish closure into the per-arch data dirs; presets cannot
exclude managed DLLs, so the strip is a mandatory post-export step (spec §strip). This tool:
  1. hash-compares each manifest DLL across BOTH per-arch data dirs (a divergent assembly cannot
     be served from one universal common.pck — fatal),
  2. deletes every manifest DLL from both data dirs,
  3. VERIFIES none remain (this is the exe∩common audit — see stage_bundle --check-dual for the
     bundle∩common and bundle∩bundle sides),
  4. writes <MacOS>/config/common-resident-expected.json (the loader's boot gate),
  5. installs common.pck into <MacOS>/bundles/ — a stripped app never exists without its layer.
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
                f"{name}.dll differs between per-arch data dirs — RID-specific managed asset; "
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
```

- [ ] **Step 4: Tests green**

Run: `cd tools/bundles && python3 -m unittest test_strip_common -v` — PASS (3 tests).
Then the whole tool suite: `python3 -m unittest discover -s tools/bundles -p "test_*.py"` — green.

---

### Task 7: **[LEAD]** S1 windowed gate — Arch via common.pck

No file changes. Sequence (main repo, after Tasks 1–6 reviewed + committed):

- [ ] `dotnet build project/FantaSim.sln -v q -nologo` + `dotnet test project/FantaSim.sln` +
  `dotnet build project/hosts/complete-app/complete-app.csproj -v q -nologo` — all green.
- [ ] `task build:godot:desktop && task bundles && task bundle:install`
- [ ] Strip manually (S1 is manual by design):
  `python3 tools/bundles/strip_common_from_export.py --app build/_artifacts/0.1.2/godot/osx/complete-app.app --manifest project/bundles/common/manifest.json --assembly complete-app --common-pck build/_artifacts/0.1.2/godot/bundles/common.pck`
- [ ] Launch windowed with remote ingress; gate on ALL of:
  - `[CommonResidentLayer] loaded 1/1 assemblies from common.pck.` in the log,
  - NO `FileNotFoundException`, `composition activated.`, world scene enters,
  - `timeline.seek` ok via `tools/fantasim-cmd.py`,
  - one world reload → `old ALC collected for bundle world` (no regression in collection).
- [ ] Commit Tasks 1–6 + gate note.

---

### Task 8: `--check-dual` extension — bundle∩common, bundle∩bundle

**Files:**
- Modify: `tools/bundles/stage_bundle.py` (`check_dual`)
- Test: `tools/bundles/test_stage_common.py` (extend)

**Interfaces:**
- Consumes: staged `project/bundles/*/` dirs incl. `common`.
- Produces: `check_dual` exit 1 on any of: bundle∩host (existing), bundle∩common, bundle∩bundle.
  (exe∩common is the strip tool's verify — plan D5.)

- [ ] **Step 1: Failing tests** (append to `tools/bundles/test_stage_common.py`):

```python
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
```

Run: `cd tools/bundles && python3 -m unittest test_stage_common -v` → new tests FAIL
(`cross_bundle_violations` missing).

- [ ] **Step 2: Implement** (in `stage_bundle.py`, next to `check_dual`):

```python
def cross_bundle_violations(bundles_root, collectible_ids, common_id):
    """(bundle, other, dll) triples for bundle∩common and bundle∩bundle overlaps.

    Two ALCs (or an ALC and the Default context) each loading a private copy of the same
    assembly is the type-identity split class — always a staging bug, never allowlisted."""
    bundles_root = Path(bundles_root)

    def names(bundle_id):
        d = bundles_root / bundle_id
        return {f.name for f in d.glob("*.dll")} if d.is_dir() else set()

    violations = []
    common_names = names(common_id)
    for bundle_id in sorted(collectible_ids):
        for dll in sorted(names(bundle_id) & common_names):
            violations.append((bundle_id, common_id, dll))
    ids = sorted(collectible_ids)
    for i, a in enumerate(ids):
        for b in ids[i + 1:]:
            for dll in sorted(names(a) & names(b)):
                violations.append((a, b, dll))
    return violations
```

And extend `check_dual(registry)` — after the existing host-overlap loop, before the final print:

```python
    collectible_ids = [e["bundleId"] for e in registry["bundles"]]
    for bundle_id, other, dll in cross_bundle_violations(BUNDLES_DIR, collectible_ids, COMMON_BUNDLE_ID):
        violations = True
        print(f"[stage_bundle] CROSS-BUNDLE DUAL COPY: '{bundle_id}' and '{other}' both stage {dll}")
```

- [ ] **Step 3: Green** — `python3 -m unittest discover -s tools/bundles -p "test_*.py"` all pass;
  `python3 tools/bundles/stage_bundle.py --check-dual` still clean on the real tree.

---

### Task 9: S2 — full first-cut list

**Files:**
- Modify: `project/hosts/complete-app/config/shared-assembly-policy.json` (`common` section only)

- [ ] **Step 1: Expand the `common` section** to exactly:

```json
  "common": {
    "comment": "Assemblies packed into common.pck (resident layer, Default ALC, packaging granularity only). Stripped from the exported exe by tools/bundles/strip_common_from_export.py. First-cut list per the 2026-07-08 design brief §E; Godot-facing candidates are detector-vetoed (E1).",
    "exactMatches": [
      "Arch",
      "Arch.LowLevel",
      "Collections.Pooled",
      "CommunityToolkit.HighPerformance",
      "Schedulers",
      "MessagePack",
      "MessagePack.Annotations",
      "UnifySerialization.MessagePack.Runtime",
      "UnifyMaths",
      "UnifyMaths.Numerics",
      "UnifyMaths.Abstractions",
      "UnifyStorage.Abstractions",
      "UnifyStorage.Runtime.LiteDb",
      "LiteDB",
      "Cartography.Globe.Core",
      "Cartography.Globe.Contracts",
      "Cartography.Shared.Contracts",
      "FantaSim.Cross.Abstractions",
      "FantaSim.World.Fields.Contracts",
      "FantaSim.World.Fields.Core",
      "FantaSim.World.Shared.Contracts",
      "FantaSim.App.World.Rendering",
      "Akka",
      "Newtonsoft.Json",
      "R3",
      "ReactiveUI",
      "DynamicData"
    ],
    "prefixes": [
      "MessagePipe",
      "UnifyEcs.",
      "TimeDete."
    ],
    "suffixRules": [
      { "prefix": "FantaSim.App.", "suffix": ".Contracts" }
    ],
    "detectorGated": [
      "BoomHud.Abstractions",
      "BoomHud.Foundation"
    ]
  }
```

Note: `BoomHud.Abstractions`/`BoomHud.Foundation` also go into `exactMatches` — the
`detectorGated` list only downgrades their Godot-facing veto from error to skip (Task 4 D6
semantics). Add both names to the END of `exactMatches` above.

- [ ] **Step 2: Restage + audit** —
  `python3 tools/bundles/stage_bundle.py --stage-common && python3 tools/bundles/stage_bundle.py --check-dual`
  Expected: staged N≈30+ assemblies; `--check-dual` clean. If an exactMatch is missing from host
  output → the name is wrong for this repo: REPORT it in the summary (do not silently drop) and
  remove it from the list with a comment. If `--check-dual` reports bundle∩common overlaps
  (e.g. a world-bundle DLL now also in common), fix by REMOVING it from the bundle side only if
  collectible-bundles.json carries an explicit override for it — otherwise report and stop.
- [ ] **Step 3 [LEAD]: S2 windowed gate** — same sequence as Task 7 (rebuild host first so
  deps.json is current; re-export; strip with the full manifest; boot; `composition activated.`;
  timeline commands; world reload ×2 with `old ALC collected`; timeline hot-reload ×1). Log line
  must read `loaded N/N assemblies` with N = staged count.

---

### Task 10: S3 — every export is stripped + provisioned

**Files:**
- Modify: `Taskfile.yml` (`build:godot:desktop` gains post-steps; `bundle:stagetool:test` already
  picks up the new python tests via discover)

- [ ] **Step 1:** the current task is (Taskfile.yml:94-100):

```yaml
  build:godot:desktop:
    desc: Export the Godot app for desktop platforms only via UnifyBuild
    deps: [restore, build, artifacts:latest]
    cmds:
      - '{{.UNIFY}} BuildGodotDesktop'
    env:
      GITVERSION_MAJORMINORPATCH: '{{.GITVERSION_MAJORMINORPATCH}}'
```

Replace its `cmds:` list with:

```yaml
    cmds:
      - '{{.UNIFY}} BuildGodotDesktop'
      # Phase 2.5 (S3): a produced app is ALWAYS stripped of common assemblies and provisioned
      # with common.pck + the expected catalog. Deviation D3: lives here (not IUnifyGodot) until
      # the strip contract is proven; upstreaming is queued.
      - task: bundle:common
      - python3 tools/bundles/strip_common_from_export.py --app {{.BUILD_DIR}}/_artifacts/{{.ARTIFACTS_VERSION}}/godot/osx/{{.ASSEMBLY}}.app --manifest project/bundles/common/manifest.json --assembly {{.ASSEMBLY}} --common-pck {{.BUILD_DIR}}/_artifacts/{{.ARTIFACTS_VERSION}}/godot/bundles/common.pck
```

(`- task: bundle:common` inside `cmds:` is valid go-task syntax; `desc`, `deps`, `env` unchanged.)

- [ ] **Step 2 [LEAD]: verify** — `task build:godot:desktop` then check
  `<app>/Contents/Resources/data_complete-app_macos_arm64/` contains NO manifest DLL,
  `<app>/Contents/MacOS/config/common-resident-expected.json` exists, and
  `<app>/Contents/MacOS/bundles/common.pck` exists — straight off the build, no manual step.

---

### Task 11: S4 — expectation becomes mandatory

**Files:**
- Modify: `project/plugins/App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs`

- [ ] **Step 1:** replace the `else` branch of the `hasExpected` check (the "pre-S4 manual mode"
  warning) with:

```csharp
            else
            {
                // S4: a common.pck with no expectation file is a half-provisioned install —
                // the strip tool always writes both. Editor/unstripped runs return earlier
                // (neither file exists), so this is unambiguous corruption.
                Fail($"common.pck present but {expectedPath} missing — half-provisioned install; re-run the export (build:godot:desktop strips + provisions both)");
            }
```

- [ ] **Step 2:** build gates (same two builds as Task 3 Step 3) — green.
- [ ] **Step 3 [LEAD]: final windowed gate** — full `task build:godot:desktop` → launch → boot
  green; then NEGATIVE test: delete `<MacOS>/config/common-resident-expected.json`, relaunch,
  expect the boot-fatal alert + `[CommonResidentLayer] FATAL` in the log; restore by re-running
  the strip tool. Then the standard reload gate (world ×1, timeline ×1, `old ALC collected`).
  Handover + memory update.

---

## Self-review notes (spec coverage)

- Loader placement/first-statement/E1-avoidance: Task 3 (brief §A). Idempotency: static lock +
  `_loaded` + single hook + already-loaded skip.
- Packer/policy/manifest/detector: Tasks 4, 9 (brief §B, §E incl. deferred list untouched).
- Strip/per-arch/exe∩common verify/audit extension: Tasks 6, 8, 10 (brief §C; D3 deviation
  recorded; D5 split recorded).
- Version gate: Tasks 1, 3, 11 (brief §D; D4 identity = name+sha256).
- Spike order S1→S4 = Tasks 7 → 9 → 10 → 11 (brief §F).
- RISKS: dependency order → hook-before-preload (Task 3); RID-specific assets → arch hash
  compare (Task 6); Godot script registration from Default-loaded PCK assemblies stays OUT of
  scope (no task touches it).
