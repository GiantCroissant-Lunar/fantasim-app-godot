# VPLanet iii Node Graph First Slice Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Add the first executable metadata slice for VPLanet as an iii-backed external tool in the app node graph.

**Architecture:** Generic external-tool manifest DTOs live in `App.NodeGraph` contracts. `App.World.GenerationGraph` projects a pinned VPLanet manifest into `WorldGenerationNodeSchema` entries so the existing world-generation node graph can author VPLanet nodes. `App.Iii` routes `vplanet.*` function ids to `IIiiInvoker`; no truth-stream commit, real VPLanet process runner, or custom preview panel is implemented in this slice.

**Tech Stack:** C# `net8.0`, `App.NodeGraph`, `App.World.GenerationGraph`, `App.Iii`, xUnit, existing `dotnet test` project-level verification.

## Global Constraints

- Do not touch `project/plugins/App.Command/App.Command.csproj` or `project/plugins/App.Command/HostComposition/CommandComposition.cs`; they are already dirty and unrelated.
- Do not implement actual VPLanet CLI execution in this slice.
- Do not add truth-stream commit behavior in this slice.
- Do not create a new test project or modify `project/FantaSim.sln`.
- Keep generated source ASCII-only.
- Use existing node graph kind hints, params, and schema records.

---

## File Structure

- Create `project/contracts/App.NodeGraph/ExternalTools/ExternalToolManifest.cs`
  - Generic DTOs for external tool manifests, functions, ports, parameters, and runtime state metadata.
- Create `project/plugins/App.World/GenerationGraph/ExternalToolNodeSchemaProjector.cs`
  - Converts generic external-tool manifests into `WorldGenerationNodeSchema` values.
- Create `project/plugins/App.World/GenerationGraph/VplanetExternalToolManifest.cs`
  - Pinned VPLanet first-slice manifest with `vplanet.status`, `vplanet.input.build`, `vplanet.run`, and `vplanet.output.parse`.
- Modify `project/plugins/App.World/GenerationGraph/WorldGenerationNodeCatalog.cs`
  - Append projected VPLanet schemas to the existing catalog.
- Modify `project/plugins/App.Iii/IiiFunctionProvider.cs`
  - Route `vplanet.*` to iii.
- Test in `project/tests/App.World.Tests/WorldGenerationGraphPortTests.cs`
  - Verify VPLanet node schemas, port kinds, params, and `NodeFromSchema`.
- Test in `project/tests/App.NodeGraph.Tests/IiiFunctionProviderRoutingTests.cs`
  - Verify `IiiFunctionProvider` supports and forwards `vplanet.*`.
- Modify `project/tests/App.NodeGraph.Tests/App.NodeGraph.Tests.csproj`
  - Add a project reference to `App.Iii` for the routing test only.

## Task 1: External Tool Manifest and VPLanet Node Schemas

**Files:**
- Create: `project/contracts/App.NodeGraph/ExternalTools/ExternalToolManifest.cs`
- Create: `project/plugins/App.World/GenerationGraph/ExternalToolNodeSchemaProjector.cs`
- Create: `project/plugins/App.World/GenerationGraph/VplanetExternalToolManifest.cs`
- Modify: `project/plugins/App.World/GenerationGraph/WorldGenerationNodeCatalog.cs`
- Test: `project/tests/App.World.Tests/WorldGenerationGraphPortTests.cs`

**Interfaces:**
- Produces: `ExternalToolManifest`, `ExternalToolFunctionManifest`, `ExternalToolPortManifest`, `ExternalToolParameterManifest`, `ExternalToolStateManifest`.
- Produces: `ExternalToolNodeSchemaProjector.Project(ExternalToolManifest manifest)`.
- Produces: `VplanetExternalToolManifest.Build()`.
- Consumes: existing `WorldGenerationNodeSchema`, `WorldGenerationGraphPort`, and `WorldGenerationGraphParameter`.

- [ ] **Step 1: Write failing catalog tests**

Add tests that assert:

```csharp
WorldGenerationNodeCatalog.Find("vplanet.status") is not null;
WorldGenerationNodeCatalog.Find("vplanet.input.build") is not null;
WorldGenerationNodeCatalog.Find("vplanet.run") is not null;
WorldGenerationNodeCatalog.Find("vplanet.output.parse") is not null;
```

Add concrete checks:

```csharp
var run = WorldGenerationNodeCatalog.Find("vplanet.run")!;
Assert.Equal("Run VPLanet", run.Label);
Assert.Equal("external/science", run.Category);
Assert.True(run.IsSideEffect);
Assert.True(run.IsExpensive);
Assert.Contains(run.Inputs, p => p.PortId == "inputBundle" && p.KindHint == "vplanet/input-bundle" && p.Required);
Assert.Contains(run.Outputs, p => p.PortId == "runResult" && p.KindHint == "vplanet/run-result");
Assert.Contains(run.Parameters!, p => p.Key == "timeoutSeconds" && p.KindHint == "int" && p.Value == "300");
```

Add `NodeFromSchema` check:

```csharp
var node = WorldGenerationGraphDefaults.NodeFromSchema("vplanet_run", "vplanet.run");
Assert.Equal("vplanet.run", node.TypeId);
Assert.Equal("Run VPLanet", node.Label);
Assert.Single(node.Inputs);
Assert.Single(node.Outputs);
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~Vplanet
```

Expected: fail because `vplanet.*` schemas are not registered.

- [ ] **Step 3: Implement manifest DTOs**

Create records under namespace `FantaSim.App.NodeGraph`:

```csharp
public sealed record ExternalToolManifest(
    string ToolId,
    string ToolVersion,
    string Provider,
    string? License,
    string? SourceUrl,
    IReadOnlyList<ExternalToolFunctionManifest> Functions);

public sealed record ExternalToolFunctionManifest(
    string FunctionId,
    string Label,
    string Category,
    string Summary,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<ExternalToolPortManifest> Inputs,
    IReadOnlyList<ExternalToolPortManifest> Outputs,
    IReadOnlyList<ExternalToolParameterManifest>? Parameters = null,
    ExternalToolStateManifest? State = null);

public sealed record ExternalToolPortManifest(
    string PortId,
    string Label,
    string Kind,
    bool Required);

public sealed record ExternalToolParameterManifest(
    string Key,
    string Label,
    string Kind,
    string DefaultValue,
    string? Unit = null,
    string? Description = null);

public sealed record ExternalToolStateManifest(
    bool Progress,
    bool Logs,
    bool Artifacts,
    bool Warnings);
```

- [ ] **Step 4: Implement projector and VPLanet manifest**

`ExternalToolNodeSchemaProjector.Project` maps each manifest function into `WorldGenerationNodeSchema`:

- `TypeId = function.FunctionId`
- `Inputs = function.Inputs.Select(p => new WorldGenerationGraphPort(p.PortId, p.Label, p.Kind, p.Required))`
- `Outputs = function.Outputs.Select(p => new WorldGenerationGraphPort(p.PortId, p.Label, p.Kind, p.Required))`
- `Parameters = function.Parameters?.Select(p => new WorldGenerationGraphParameter(p.Key, p.Label, p.DefaultValue, p.Kind))`

`VplanetExternalToolManifest.Build()` returns a pinned manifest with these functions:

- `vplanet.status`
  - output `status`, kind `vplanet/status`
  - no inputs
  - not side-effecting, not expensive
- `vplanet.input.build`
  - output `inputBundle`, kind `vplanet/input-bundle`
  - params `systemName=solarsystem`, `starBodyName=sun`, `planetBodyName=earth`, `stopTimeYears=4.6e9`, `outputTimeYears=1.0e6`
  - not side-effecting, not expensive
- `vplanet.run`
  - input `inputBundle`, kind `vplanet/input-bundle`, required
  - output `runResult`, kind `vplanet/run-result`
  - param `timeoutSeconds=300`
  - side-effecting and expensive
- `vplanet.output.parse`
  - input `runResult`, kind `vplanet/run-result`, required
  - output `outputTable`, kind `vplanet/output-table`
  - param `bodyName=sun`
  - not side-effecting, not expensive

- [ ] **Step 5: Register projected schemas**

Modify `WorldGenerationNodeCatalog` so `Schemas` includes the existing hand-authored world nodes plus:

```csharp
ExternalToolNodeSchemaProjector.Project(VplanetExternalToolManifest.Build())
```

Keep `Find` behavior unchanged.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~Vplanet
```

Expected: pass.

## Task 2: iii Provider Routing for VPLanet Functions

**Files:**
- Modify: `project/plugins/App.Iii/IiiFunctionProvider.cs`
- Modify: `project/tests/App.NodeGraph.Tests/App.NodeGraph.Tests.csproj`
- Create: `project/tests/App.NodeGraph.Tests/IiiFunctionProviderRoutingTests.cs`

**Interfaces:**
- Consumes: `IIiiInvoker.RequestAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)`.
- Produces: `IiiFunctionProvider.Supports("vplanet.status") == true`.
- Produces: forwarded request for any `vplanet.*` function.

- [ ] **Step 1: Write failing routing tests**

Create a fake `IIiiInvoker` that records the requested function id and payload, then returns:

```json
{ "ok": true, "functionId": "<function id>" }
```

Assert:

```csharp
Assert.True(provider.Supports("vplanet.status"));
Assert.True(provider.Supports("vplanet.run"));
Assert.False(provider.Supports("world.options"));
```

Then call:

```csharp
var result = await provider.InvokeAsync("vplanet.status", new JsonObject { ["probe"] = "1" });
Assert.Equal("vplanet.status", fake.FunctionId);
Assert.Equal("1", fake.Payload!["probe"]!.GetValue<string>());
Assert.True(result["ok"]!.GetValue<bool>());
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.NodeGraph.Tests/App.NodeGraph.Tests.csproj --filter FullyQualifiedName~IiiFunctionProviderRoutingTests
```

Expected: fail because `App.NodeGraph.Tests` does not yet reference `App.Iii`, or because `vplanet.*` is not supported.

- [ ] **Step 3: Add test project reference**

Add to `App.NodeGraph.Tests.csproj`:

```xml
<ProjectReference Include="..\..\plugins\App.Iii\App.Iii.csproj" />
```

- [ ] **Step 4: Implement routing**

Modify `IiiFunctionProvider.Supports` to include:

```csharp
|| functionId.StartsWith("vplanet.", StringComparison.Ordinal)
```

Update the XML summary to mention `vplanet.*`.

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.NodeGraph.Tests/App.NodeGraph.Tests.csproj --filter FullyQualifiedName~IiiFunctionProviderRoutingTests
```

Expected: pass.

## Task 3: Focused Build Verification

**Files:**
- No new source files.

**Interfaces:**
- Verifies Task 1 and Task 2 together.

- [ ] **Step 1: Run focused tests**

Run:

```bash
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~Vplanet
dotnet test /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.NodeGraph.Tests/App.NodeGraph.Tests.csproj --filter FullyQualifiedName~IiiFunctionProviderRoutingTests
```

Expected: both pass.

- [ ] **Step 2: Run scoped compile**

Run:

```bash
dotnet build /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World/App.World.csproj
dotnet build /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Iii/App.Iii.csproj
```

Expected: both pass. Existing unrelated warnings are acceptable; new errors are not.

- [ ] **Step 3: Review changed files**

Run:

```bash
git -C /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot status --short
git -C /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot diff -- project/contracts/App.NodeGraph project/plugins/App.World/GenerationGraph project/plugins/App.Iii project/tests/App.World.Tests project/tests/App.NodeGraph.Tests
```

Expected: only the planned files changed, plus pre-existing unrelated dirty files remain untouched.

## Self-Review

- Spec coverage: first-slice VPLanet nodes, iii routing, generic manifest DTOs, hybrid-friendly schema projection, and no truth-stream commit are covered.
- Placeholder scan: no placeholder implementation steps remain.
- Type consistency: manifest DTOs project to existing `WorldGenerationNodeSchema`, `WorldGenerationGraphPort`, and `WorldGenerationGraphParameter`; provider routing uses existing `IIiiInvoker`.
