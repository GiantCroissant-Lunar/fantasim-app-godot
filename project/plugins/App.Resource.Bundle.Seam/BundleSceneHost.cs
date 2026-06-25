using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleSceneHost : IBundleSceneRegistry
{
    private readonly Node _root;
    private readonly ILogger _logger;
    private readonly Dictionary<string, Node> _activeScenes = new(StringComparer.OrdinalIgnoreCase);

    public BundleSceneHost(Node root, ILogger logger)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Node? InstantiateScene(string scenePath, BundleManifest? manifest = null)
    {
        var packed = ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.ReplaceDeep);
        if (packed is null)
        {
            _logger.LogWarning("Bundle scene not found: {Path}", scenePath);
            return null;
        }

        var instance = packed.Instantiate();
        var rootInstanceId = instance.GetInstanceId();
        ApplyResidentScriptBindings(instance, manifest);

        if (GodotObject.InstanceFromId(rootInstanceId) is not Node reboundInstance)
        {
            _logger.LogWarning("Bundle scene root was invalidated after resident binding: {Path}", scenePath);
            return null;
        }

        _root.CallDeferred(Node.MethodName.AddChild, reboundInstance);
        return reboundInstance;
    }

    public void RegisterScene(string bundleId, Node scene)
    {
        _activeScenes[bundleId] = scene;
        _logger.LogInformation("Bundle scene registered: {BundleId}", bundleId);
    }

    public IReadOnlyList<string> ListActiveSceneIds()
        => _activeScenes.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();

    public Node? GetSceneOrNull(string bundleId)
        => _activeScenes.TryGetValue(bundleId, out var scene)
            && GodotObject.IsInstanceValid(scene)
            ? scene
            : null;

    public Node? GetNodeOrNull(string bundleId, NodePath nodePath)
        => GetSceneOrNull(bundleId)?.GetNodeOrNull(nodePath);

    public void RemoveScene(string bundleId, bool detachFromParent = true)
    {
        if (!_activeScenes.TryGetValue(bundleId, out var scene))
            return;

        if (detachFromParent)
            scene.GetParent()?.RemoveChild(scene);

        scene.QueueFree();
        _activeScenes.Remove(bundleId);
    }

    private void ApplyResidentScriptBindings(Node scene, BundleManifest? manifest)
    {
        if (manifest?.ResidentScripts is not { Count: > 0 } bindings)
            return;

        foreach (var binding in bindings)
        {
            var hasResidentType = !string.IsNullOrWhiteSpace(binding.ResidentType);
            var hasScriptPath = !string.IsNullOrWhiteSpace(binding.ScriptPath);
            if (string.IsNullOrWhiteSpace(binding.NodePath) || (!hasResidentType && !hasScriptPath))
            {
                _logger.LogWarning(
                    "Resident script binding skipped for bundle {BundleId}: nodePath empty or no scriptPath/residentType",
                    manifest.BundleId);
                continue;
            }

            var target = binding.NodePath == "."
                ? scene
                : scene.GetNodeOrNull(binding.NodePath);
            if (target is null)
            {
                _logger.LogWarning(
                    "Resident script binding target not found for bundle {BundleId}: {NodePath}",
                    manifest.BundleId,
                    binding.NodePath);
                continue;
            }

            var source = hasResidentType ? binding.ResidentType : binding.ScriptPath;
            var script = hasResidentType
                ? LoadResidentTypeScript(binding.ResidentType)
                : ResourceLoader.Load<Script>(binding.ScriptPath, null, ResourceLoader.CacheMode.Reuse);

            if (script is null)
            {
                _logger.LogWarning("Resident script not resolved for bundle {BundleId}: {Source}", manifest.BundleId, source);
                continue;
            }

            target.SetScript(script);
            _logger.LogInformation("Resident script bound for bundle {BundleId}: {NodePath} -> {Source}", manifest.BundleId, binding.NodePath, source);
        }
    }

    private static Script? LoadResidentTypeScript(string typeName)
    {
        var type = Type.GetType(typeName)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(resolved => resolved is not null);

        if (type is null)
            return null;

        Node? probe = null;
        try
        {
            probe = Activator.CreateInstance(type) as Node;
            return probe?.GetScript().As<Script>();
        }
        finally
        {
            probe?.Free();
        }
    }
}
