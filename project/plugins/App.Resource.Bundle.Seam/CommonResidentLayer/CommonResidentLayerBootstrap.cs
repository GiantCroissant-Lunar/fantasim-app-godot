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
/// Loads bundles/common.pck into the app's COMPONENT AssemblyLoadContext (Godot hosts the
/// game assembly graph in an IsolatedComponentLoadContext, not Default) before composition.
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

            // Hook the COMPONENT load context, not Default: Godot hosts complete-app.dll and
            // its whole dependency graph in an IsolatedComponentLoadContext (asmload trace,
            // S2 gate 2026-07-08). Every stripped-assembly demand originates there, and its
            // fallback chain never consults Default.Resolving. This seam assembly lives in
            // that same context, so resolve it from ourselves.
            var componentAlc = AssemblyLoadContext.GetLoadContext(typeof(CommonResidentLayerBootstrap).Assembly)
                ?? AssemblyLoadContext.Default;
            Log($"hooking load context '{componentAlc.Name ?? componentAlc.GetType().Name}'.");
            componentAlc.Resolving += OnComponentResolving;

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

            // NO eager preload — serve on demand through the Resolving hook. Preloading via
            // LoadFromAssemblyPath registered strong-named assemblies in a way the binder did
            // not accept for MemberRef resolution from TPA callers (S2 gate 2026-07-08:
            // byte-identical MessagePipe/Akka threw MissingMethodException while unsigned Arch
            // worked). An assembly returned FROM the Resolving event is bound to the exact
            // requested identity — the supported extension point. Manifest order is irrelevant:
            // demand drives resolution.
            _loaded = true;
            Log($"extracted {extracted.Count} assemblies from common.pck; serving on demand via the component ALC's Resolving.");
        }
    }

    private static Assembly? OnComponentResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var map = _extractedByName;
        if (map is null || name.Name is null)
            return null;
        var served = map.TryGetValue(name.Name, out var path);
        // Diagnostic channel: every resolution MISS that reaches this hook is
        // either us serving a common assembly or a genuine failure about to surface — log both.
        Console.WriteLine($"[CommonResidentLayer] resolving: {name} -> {(served ? path : "NOT OURS (miss)")}");
        return served ? context.LoadFromAssemblyPath(path!) : null;
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
