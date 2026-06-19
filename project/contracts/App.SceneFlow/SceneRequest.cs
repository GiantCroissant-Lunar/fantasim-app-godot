namespace FantaSim.App.SceneFlow;

public sealed record SceneRequest(string SceneId, string? ParentSceneId = null);
