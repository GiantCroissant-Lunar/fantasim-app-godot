using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleExtractor
{
    private static readonly string SessionTempRoot = Path.Combine(
        Path.GetTempPath(),
        "fantasim_bundles",
        Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));

    private static int _loadSeq;

    public BundleExtractionResult? Extract(string bundleResPath, string pluginDllName)
    {
        var pluginResPath = bundleResPath + "/" + pluginDllName;
        if (!GodotFileAccess.FileExists(pluginResPath))
            return null;

        var bundleTempDir = NewBundleTempDir(bundleResPath);
        var managedVisitor = new ManagedAssemblyExtractionVisitor(pluginDllName);
        var dataVisitor = new BundleDataExtractionVisitor();
        var visitors = new IBundleEntryVisitor[] { managedVisitor, dataVisitor };
        var context = new BundleExtractionContext(bundleTempDir);

        using (var dir = DirAccess.Open(bundleResPath))
        {
            if (dir is not null)
            {
                foreach (var fileName in dir.GetFiles())
                {
                    var name = fileName.EndsWith(".remap", StringComparison.OrdinalIgnoreCase)
                        ? fileName[..^".remap".Length]
                        : fileName;

                    var entry = new BundleEntry(bundleResPath + "/" + name, name);
                    foreach (var visitor in visitors)
                    {
                        if (visitor.ShouldVisit(entry))
                            visitor.Visit(entry, context);
                    }
                }
            }
        }

        if (managedVisitor.PluginAssemblyPath is null)
        {
            var pluginEntry = new BundleEntry(pluginResPath, pluginDllName);
            managedVisitor.Visit(pluginEntry, context);
        }

        return new BundleExtractionResult(bundleTempDir, managedVisitor.PluginAssemblyPath, dataVisitor.ExtractedFiles);
    }

    private static string NewBundleTempDir(string bundleResPath)
    {
        var loadId = Interlocked.Increment(ref _loadSeq);
        var bundleTempDir = Path.Combine(
            SessionTempRoot,
            BundleTempName(bundleResPath),
            loadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(bundleTempDir);
        return bundleTempDir;
    }

    private static string BundleTempName(string bundleResPath)
    {
        var normalized = bundleResPath.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "bundle" : name;
    }
}

internal sealed class BundleEntry
{
    public BundleEntry(string resPath, string fileName)
    {
        ResPath = resPath ?? throw new ArgumentNullException(nameof(resPath));
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
    }

    public string ResPath { get; }

    public string FileName { get; }
}

internal sealed class BundleExtractionContext
{
    private readonly string _bundleTempDir;

    public BundleExtractionContext(string bundleTempDir)
    {
        _bundleTempDir = bundleTempDir ?? throw new ArgumentNullException(nameof(bundleTempDir));
    }

    public string? TryExtract(BundleEntry entry)
    {
        using var file = GodotFileAccess.Open(entry.ResPath, GodotFileAccess.ModeFlags.Read);
        if (file is null)
            return null;

        var bytes = file.GetBuffer((long)file.GetLength());
        var tempPath = Path.Combine(_bundleTempDir, entry.FileName);
        var tempParent = Path.GetDirectoryName(tempPath);
        if (!string.IsNullOrWhiteSpace(tempParent))
            Directory.CreateDirectory(tempParent);
        File.WriteAllBytes(tempPath, bytes);
        return tempPath;
    }
}

public sealed class BundleExtractionResult
{
    public BundleExtractionResult(string bundleTempDir, string? pluginAssemblyPath, IReadOnlyList<string> dataFiles)
    {
        BundleTempDir = bundleTempDir ?? throw new ArgumentNullException(nameof(bundleTempDir));
        PluginAssemblyPath = pluginAssemblyPath;
        DataFiles = dataFiles ?? throw new ArgumentNullException(nameof(dataFiles));
    }

    public string BundleTempDir { get; }

    public string? PluginAssemblyPath { get; }

    public IReadOnlyList<string> DataFiles { get; }
}

internal interface IBundleEntryVisitor
{
    bool ShouldVisit(BundleEntry entry);

    void Visit(BundleEntry entry, BundleExtractionContext context);
}

internal sealed class ManagedAssemblyExtractionVisitor : IBundleEntryVisitor
{
    private readonly string _pluginDllName;

    public ManagedAssemblyExtractionVisitor(string pluginDllName)
    {
        _pluginDllName = pluginDllName ?? throw new ArgumentNullException(nameof(pluginDllName));
    }

    public string? PluginAssemblyPath { get; private set; }

    public bool ShouldVisit(BundleEntry entry)
        => entry.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    public void Visit(BundleEntry entry, BundleExtractionContext context)
    {
        var extracted = context.TryExtract(entry);
        if (extracted is not null
            && string.Equals(entry.FileName, _pluginDllName, StringComparison.OrdinalIgnoreCase))
        {
            PluginAssemblyPath = extracted;
        }
    }
}

internal sealed class BundleDataExtractionVisitor : IBundleEntryVisitor
{
    private readonly List<string> _extractedFiles = new();

    public IReadOnlyList<string> ExtractedFiles => _extractedFiles;

    public bool ShouldVisit(BundleEntry entry)
        => !entry.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    public void Visit(BundleEntry entry, BundleExtractionContext context)
    {
        var extracted = context.TryExtract(entry);
        if (extracted is not null)
            _extractedFiles.Add(extracted);
    }
}
