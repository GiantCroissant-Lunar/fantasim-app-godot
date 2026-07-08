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

            // OS.GetExecutablePath(), NOT AppContext.BaseDirectory: in a Godot .NET export
            // BaseDirectory is the per-arch data dir (Contents/Resources/data_*), while the
            // bundle convention is exe-adjacent (Contents/MacOS) — see GodotBundleDirectoryResolver.
            var baseDir = OS.GetExecutablePath().GetBaseDir();
            var pckPath = Path.Combine(baseDir, "bundles", "common.pck");
            var expectedPath = Path.Combine(baseDir, "config", "common-resident-expected.json");
            var hasPck = File.Exists(pckPath);
            var hasExpected = File.Exists(expectedPath);

            // Provisioning matrix (plan D7): neither -> unstripped exe or editor run, skip.
            if (!hasPck && !hasExpected)
            {
                Log("no common.pck and no expectation file - unstripped run; skipping.");
                return;
            }

            // Expected without pck = a stripped exe missing its resident layer. Always fatal.
            if (!hasPck)
                throw Fail($"common.pck missing at {pckPath} but {expectedPath} exists - the exe was stripped; reinstall common.pck");

            if (!new BundleVfs().LoadPck(pckPath))
                throw Fail($"ProjectSettings.LoadResourcePack failed for {pckPath}");

            var manifest = new BundleVfs().ReadManifest(BundleResPath);
            if (manifest?.Managed?.Assemblies is not { Count: > 0 } assemblies)
                throw Fail($"common.pck has no manifest.json with managed.assemblies under {BundleResPath}");

            // Resolving hook BEFORE any load (brief RISKS: dependency order - a preload's
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
                    throw Fail($"common.pck integrity: {name} extracted sha {fileSha} != manifest sha {declaredSha}");
                }
                actual.Add(new CommonAssemblyIdentity(name, fileSha));
            }

            if (hasExpected)
            {
                var expected = CommonResidentCatalog.ParseExpected(File.ReadAllText(expectedPath));
                var errors = CommonResidentCatalog.Validate(expected, actual);
                if (errors.Count > 0)
                    throw Fail("common resident-layer gate: " + string.Join("; ", errors));
            }
            else
            {
                // S1/S2 manual mode. Task 11 (S4) makes this FATAL.
                Log("WARNING: loading common.pck without an expectation file (pre-S4 manual mode).");
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
                    Log($"{simpleName} already loaded in Default ALC (unstripped exe copy) - pck copy skipped.");
                    continue;
                }
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                loadedCount++;
            }

            _loaded = true;
            Log($"loaded {loadedCount}/{extracted.Count} assemblies from common.pck.");
        }
    }

    private static Assembly? OnDefaultResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var map = _extractedByName;
        if (map is null || name.Name is null)
            return null;
        return map.TryGetValue(name.Name, out var path) ? context.LoadFromAssemblyPath(path) : null;
    }

    // GD.Print does not reach a nohup-captured stdout in the exported app; the windowed
    // gate reads the log, so mirror every message to the process Console too.
    private static void Log(string message)
    {
        var line = "[CommonResidentLayer] " + message;
        GD.Print(line);
        Console.WriteLine(line);
    }

    // Returns (rather than throws) so call sites `throw Fail(...)` — definite-assignment
    // analysis does not honor [DoesNotReturn], only nullable analysis does (CS0165 otherwise).
    private static InvalidOperationException Fail(string message)
    {
        // _log does not exist yet (AppComposition has not run) - GD/Console channels only.
        GD.PrintErr("[CommonResidentLayer] FATAL: " + message);
        Console.Error.WriteLine("[CommonResidentLayer] FATAL: " + message);
        OS.Alert(message, "common resident layer");
        return new InvalidOperationException("[CommonResidentLayer] " + message);
    }
}
