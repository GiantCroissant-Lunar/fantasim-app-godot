using System.Collections.Generic;
using Godot;

namespace FantaSim.App.Resource.Bundle;

/// <summary>
/// Resident Godot-side registry of scene roots mounted from bundle entry scenes.
/// This is a T4 seam surface: consumers that use it may touch Godot nodes, but T1/T3 contracts must not.
/// </summary>
public interface IBundleSceneRegistry
{
    IReadOnlyList<string> ListActiveSceneIds();

    Node? GetSceneOrNull(string bundleId);

    Node? GetNodeOrNull(string bundleId, NodePath nodePath);
}
