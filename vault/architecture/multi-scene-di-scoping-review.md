# Multi-scene DI scoping review -- VContainer parent-child container comparison

> **AUDIT (2026-07-06, code-verified):** Issue 4's `SceneActivatorBase` has since been BUILT (`contracts/App.SceneFlow`); Issue 1 (manual forwarding, no parent.CreateScope) remains open as described. _(See the authority index in `vault/README.md`.)_


**Status:** PROPOSED (2026-06-19). Reviews the ref-projects and yokan-projects scene-flow architecture against VContainer's parent-child container model and identifies enhancements.

## Context

The target architecture uses multi-scene development similar to Unity + VContainer, where select scenes act as DI container scopes with parent-child hierarchy. Not every `.tscn` is a DI scope -- only scenes that need their own service registrations or isolation become container scopes. Plain scenes (UI panels, decorative nodes, purely visual content) stay as ordinary Godot nodes with no DI involvement.

## What the previous attempts got right

The ref-projects `App.SceneFlow` established a strong foundation that already mirrors VContainer's parent-child philosophy:

1. **Dynamic parent resolution.** `SceneRequest.ParentSceneId` is resolved at runtime, not via compile-time `[DependencyScope(Parent=...)]`. The same scene can be entered under different parents. This matches VContainer where a child container is built with the parent passed at construction time.

2. **Scene = DI scope.** `ISceneActivator` / `ISceneActivation` makes a scene a DI scope that owns services and disposes cleanly. The `ISceneActivation` carries the scene's `IServiceProvider` and `Dispose()` tears it down.

3. **Hierarchical enter/exit.** `EnterAsync("stage")` -> `EnterAsync("assist", parent:"stage")` -> `ExitAsync("stage")` exits children first. Matches VContainer's nested lifetime scope disposal order.

4. **Cross-ALC scene bundles.** Scene tiers load as PCK bundles into collectible AssemblyLoadContexts. The bundle's plugin registers its `ISceneActivator` into the shared kernel registry across the ALC boundary. The resident SceneFlow service resolves and activates it. This is an architecture VContainer does not have -- VContainer does not deal with ALC unload.

5. **Kernel sharing verified.** The ref-projects verified identical `IRegistry` hash across parent and child scopes (same kernel instance, not a copy). The dynamic parent forwarding pattern works.

## Scene classification

Not all Godot scenes become DI scopes. Three categories:

| Category | Has DI scope? | Example | Lifecycle |
|----------|--------------|---------|-----------|
| **DI scene** | Yes | `stage`, `assist`, a game level with its own services | SceneFlow `EnterAsync`/`ExitAsync`, owns `IServiceProvider`, disposes on exit |
| **View bundle scene** | No (uses host's scope) | `reference-overview`, `node-graph` (BoomHud surface) | Loaded as PCK, rendered by resident `ViewHost`, no own DI scope |
| **Plain scene** | No | A `.tscn` for a settings panel, a decorative effect, a simple HUD element | Instantiated by Godot `ResourceLoader.Load<PackedScene>`, no DI involvement |

Only **DI scenes** go through `ISceneActivator` and get their own container. The other two categories are ordinary Godot content. This matches VContainer where only scenes with a `LifetimeScope` component get a container; everything else is just Unity content.

## Issues identified -- five enhancements

### Issue 1: Singleton sharing is manual forwarding, not true parent-child container hierarchy

**Severity:** High

The `StageActivator` manually forwards kernel singletons:

```csharp
services.AddSingleton(parent.GetRequiredService<IRegistry>());
services.AddSingleton(parent.GetRequiredService<ILoggerFactory>());
```

This is resolve-and-forward -- it pulls resolved instances from the parent and re-registers them in the child. It works (verified: identical `IRegistry` hash across scopes), but it is fragile:

- Every new shared singleton must be manually forwarded in every scene activator. If someone adds `IMessageBus` to the kernel but forgets to forward it in `AssistActivator`, assist silently gets a different (or missing) bus.
- No compile-time check that the forwarded types are complete. A missing forwarding silently resolves to `null` or throws at runtime.
- The child builds its own `ServiceProvider` from a fresh `ServiceCollection` -- it is not a real child scope of the parent provider, so parent-registered scoped/transient services are not visible.
- Disposal behavior is accidentally correct: MEDI does not dispose `AddSingleton(instance)` singletons when the child provider disposes, so the shared kernel survives child disposal. But this is a MEDI implementation detail, not a guaranteed contract.

**VContainer equivalent:** `parent.CreateScope()` creates a real child container where parent singletons are shared by construction, parent services are visible, and only child-specific registrations need to be added. No manual forwarding.

**Enhancement:** Fix the DependencyArchi `MicrosoftExtensionsScopeActivationAdapter` to use MEDI's native `parentProvider.CreateScope()` instead of copying descriptors into a fresh collection. The RFC at `ref-projects/vault/rfc/rfc-dependency-archi-child-scope-singletons.md` already documents this as option 2 ("Nested provider"):

```csharp
// Instead of: copy parent descriptors + build new provider
// Do: create a child scope of the parent provider
var childScope = parentProvider.CreateScope();
// Register child-specific modules on the child scope's ServiceCollection
```

This makes the manual kernel forwarding in `StageActivator` unnecessary -- the child scope automatically sees parent singletons. The activator would only register scene-specific services.

**Impact on the activator pattern:** `ISceneActivator.ActivateAsync(IServiceProvider parent)` stays the same -- the parent is still dynamic. But internally the activator (or a base class) does `parent.CreateScope()` instead of `new ServiceCollection()` + manual forwarding.

### Issue 2: SceneFlow uses SemaphoreSlim -- candidate for actor conversion

**Severity:** Low

`SceneFlow.Service` guards `EnterAsync`/`ExitAsync` with `SemaphoreSlim(1,1)`. This is the same pattern as `EcsWorldContext`'s `lock(_gate)` -- manual synchronization that an actor mailbox handles naturally. If SceneFlow's T3 became an actor, the scene tree (`List<SceneSession>`) becomes actor state (no lock), enter/exit are messages (serialized by mailbox), and supervision handles activation failures.

However, SceneFlow has low concurrency pressure (scenes are entered/exited infrequently, not at 60fps). The `SemaphoreSlim` is sufficient.

**Enhancement:** Leave SceneFlow as plain class T3 for now. Document as a candidate for actor conversion if concurrency pressure increases (e.g., hot-reload of a scene while another is entering).

### Issue 3: No Godot scene tree integration -- DI scenes are pure C# scopes, not connected to Godot Node lifecycle

**Severity:** High

The `ISceneActivator` contract builds a DI scope but does not interact with Godot's `SceneTree`. A "scene" in this architecture is a DI scope with services, not a Godot `Node` tree. This is intentional (keeps Godot quarantined in T4) but creates a gap:

- Godot's scene lifecycle (`_EnterTree`, `_ExitTree`, `_Ready`, `_Process`) is not connected to the DI scope lifecycle. A Godot scene node instantiated from a PCK does not automatically get its DI scope created or disposed.
- The `SceneFlowProvider` loads the PCK (which may contain a `.tscn`) but the `ISceneActivator` is pure C# -- it does not instantiate the `.tscn` or connect the Godot node lifecycle to the DI scope.
- For DI scenes that also instantiate Godot content (a stage with a 3D world, an assist panel with UI), there is no automatic bridge between the DI scope and the Godot node tree that renders it.

**VContainer equivalent:** VContainer's `LifetimeScope` is a MonoBehaviour -- the Godot equivalent would be a `Node` subclass that IS the DI scope, so `_EnterTree` creates the scope and `_ExitTree` disposes it. Child nodes resolve services from the scope.

**Enhancement:** Add a Godot-facing scene node type (T4) that bridges Godot's scene lifecycle to the DI scope lifecycle for DI scenes that also need Godot content:

```csharp
// T4 seam: a Godot Node that creates/disposes a DI scope on enter/exit
// Only used on DI scenes that have Godot-rendered content. Pure-C# DI scenes
// (no Godot nodes) do not need this -- their ISceneActivator is enough.
public sealed class SceneScopeNode : Node
{
    private ISceneActivation? _activation;
    private string _sceneId = "";
    private IServiceProvider? _parent;

    public void Configure(string sceneId, IServiceProvider parent)
    {
        _sceneId = sceneId;
        _parent = parent;
    }

    public IServiceProvider? Services => _activation?.Services;

    public override void _EnterTree()
    {
        if (_parent is null) return;
        Callable.From(async () =>
        {
            var sceneFlow = _parent.GetRequiredService<SceneFlow.IService>();
            _activation = await sceneFlow.EnterAsync(
                new SceneRequest(_sceneId), CancellationToken.None)
                as ISceneActivation;
        }).CallDeferred();
    }

    public override void _ExitTree()
    {
        _activation?.Dispose();
        _activation = null;
    }
}
```

This lets DI scenes that have Godot-rendered content (a stage with a 3D world) automatically create DI scopes when they enter the tree and dispose them when they leave. Children of that node can resolve services from the scope.

**Important:** This node type is opt-in. A `.tscn` without a `SceneScopeNode` is a plain scene with no DI involvement. Only scenes that explicitly include a `SceneScopeNode` (or are entered via `SceneFlow.EnterAsync`) get a DI scope.

### Issue 4: Manual module application boilerplate in every activator

**Severity:** Medium

The `StageActivator` manually fetches the DependencyArchi scope plan and applies modules:

```csharp
var stagePlan = GeneratedDependencyArchi.GetScopePlans<IServiceCollection>()
    .First(plan => plan.Descriptor.Id == new DependencyScopeId("stage"));
foreach (var registration in stagePlan.GetOrderedModules())
    registration.Module.Register(services, new DependencyModuleContext(registration.Descriptor));
```

Every scene activator repeats this pattern. It works but requires every scene to know the DependencyArchi API. If a scene bundle (loaded from a PCK into a collectible ALC) wants to register its own services, it must pre-declare modules at compile time (via `[DependencyScope]` attributes).

**VContainer equivalent:** VContainer's `LifetimeScope.Configure(IContainerBuilder builder)` receives the builder and adds registrations. No source generator needed, no scope plan lookup.

**Enhancement:** Provide a simpler registration API for scene bundles that does not require the full DependencyArchi module/scope plan machinery:

```csharp
// Simpler base for scenes that just want to register services
public abstract class SceneActivatorBase : ISceneActivator
{
    public abstract string SceneId { get; }

    public Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken ct = default)
    {
        var services = new ServiceCollection();
        Configure(services, parent);
        var provider = services.BuildServiceProvider();
        return Task.FromResult<ISceneActivation>(new Activation(SceneId, provider));
    }

    // Scenes override this instead of dealing with DependencyArchi scope plans.
    // If issue 1 is fixed, this becomes parent.CreateScope() + child-specific registrations.
    protected abstract void Configure(IServiceCollection services, IServiceProvider parent);
}
```

Scene bundles implement `Configure` instead of dealing with DependencyArchi scope plans. This is closer to the VContainer `Configure(IContainerBuilder)` pattern and removes the boilerplate.

Scenes that want the full DependencyArchi module/scope plan system can still use it -- `SceneActivatorBase` is an optional base, not a requirement. A scene can implement `ISceneActivator` directly and use DependencyArchi modules if it needs the dependency-ordering and module-catalog features.

### Issue 5: No automatic child scene discovery from Godot scene tree

**Severity:** Low

In Unity + VContainer, a `LifetimeScope` on a parent GameObject automatically discovers child `LifetimeScope` components in the same scene hierarchy and parents them. The ref-projects SceneFlow requires explicit `EnterAsync(SceneRequest("assist", parent: "stage"))` -- the parent-child relationship is always manually specified.

**Enhancement:** If we add `SceneScopeNode` (issue 3), Godot's tree traversal can discover parent-child scene relationships automatically. A `SceneScopeNode` on a child `.tscn` that is instantiated under a parent `SceneScopeNode` can automatically use the parent's scope as its parent:

```
Stage (SceneScopeNode, parent=app-root)
  +-- Assist (SceneScopeNode, parent=Stage)  <- discovered from tree, not explicit
```

This is optional -- the explicit `ParentSceneId` in `SceneRequest` should still work for programmatic scene entry. The tree-based discovery is a convenience for scene-authored hierarchies (`.tscn` files that include child `.tscn` files).

```csharp
public override void _EnterTree()
{
    // Auto-discover parent scope from the Godot tree if not explicitly configured
    if (_parent is null)
    {
        var parentScopeNode = GetParent()?.FindChild("*", true, false) as SceneScopeNode;
        _parent = parentScopeNode?.Services;
    }
    // ... proceed with scope creation
}
```

---

## Summary

| Issue | Severity | Enhancement | Effort | Priority |
|-------|----------|------------|--------|----------|
| 1. Manual singleton forwarding | High | Fix DependencyArchi adapter to use `parent.CreateScope()` | Medium -- one adapter file, tests | First |
| 2. SceneFlow SemaphoreSlim | Low | Leave as-is; document as actor candidate | None | Later |
| 3. No Godot scene lifecycle bridge | High | Add `SceneScopeNode` T4 for DI scenes with Godot content | Medium -- new T4 seam type | First (with 1) |
| 4. Manual module boilerplate | Medium | Add `SceneActivatorBase.Configure(IServiceCollection)` | Low -- base class | After 1+3 |
| 5. No tree-based parent discovery | Low | Optional: `SceneScopeNode` auto-discovers parent from Godot tree | Low-Medium | After 4 |

Issues 1 and 3 are the highest-value fixes. Issue 1 eliminates the fragile manual kernel forwarding and makes parent-child DI work like VContainer's real container hierarchy. Issue 3 bridges the gap between Godot's scene tree lifecycle and the DI scope lifecycle for DI scenes that also render Godot content. Issues 4 and 5 are quality-of-life improvements. Issue 2 is a note for future evolution.

## References

- ref-projects SceneFlow: `lunar-horse-002/ref-projects/fantasim-app-godot/project/contracts/App.SceneFlow/`, `project/plugins/App.SceneFlow/`
- ref-projects Stage activator: `lunar-horse-002/ref-projects/fantasim-app-godot/project/plugins/App.Stage/StageActivator.cs`
- ref-projects AppComposition: `lunar-horse-002/ref-projects/fantasim-app-godot/project/plugins/App.Common/AppComposition.cs`
- ref-projects CompositionModules: `lunar-horse-002/ref-projects/fantasim-app-godot/project/plugins/App.Common/CompositionModules.cs`
- DependencyArchi adapter: `plate-projects/dependency-archi/dotnet/src/DependencyArchi.MicrosoftExtensions/MicrosoftExtensionsScopeActivationAdapter.cs`
- DependencyArchi child-scope RFC: `lunar-horse-002/ref-projects/fantasim-app-godot/vault/rfc/rfc-dependency-archi-child-scope-singletons.md`
- SceneFlow handover: `lunar-horse-002/ref-projects/fantasim-app-godot/vault/handover/2026-06-09-scene-flow-dynamic-hierarchical-di.md`
- Collectible scene bundles handover: `lunar-horse-002/ref-projects/fantasim-app-godot/vault/handover/2026-06-09-collectible-scene-bundles.md`
- Service tier architecture: `vault/architecture/service-tier-architecture.md`
- Cross-ALC rules: `vault/architecture/cross-alc-rules.md`
