# Command core + remote transport ingress — design

> status: concept-lock 2026-06-24 · DESIGN ONLY (build dispatched separately)
> repo: fantasim-app-godot · finishes the `App.Remote` → `App.Command` split correctly (core vs ingress)
> goal: let an agent **operate the exported WINDOWED app as a normal user**, by driving commands over a wire
> ref (read-only): `ref-projects/fantasim-app-godot/project/plugins/App.Remote*`, `project/hosts/agent-cli`, `tools/record-session.py`

## 1. Why

`App.Command` today has only an **in-process** client, so an external process cannot drive a separately-running windowed app. Ref-projects did this with `App.Remote` (an HTTP surface hosted *inside* the app) plus an `agent-cli` host; only the in-process slice was ported here. The tell: `ImmediateMainThreadDispatcher` sits in `App.Command` doing nothing — the main-thread marshaling **seam** landed, but the off-thread **transport** that would need it did not.

This spec defines the missing half **without repeating ref's mistake** of bundling the transport into the command project. A command is a command regardless of its source (user operating the app · system/internal · remote); transport belongs to a separate inbound adapter.

## 2. Current state

- `project/contracts/App.Command/CommandTypes.cs` — `CommandRequest(Command, PayloadJson?, CorrelationId?, ActorKind?, ActorId?)`, `CommandResult(Id, Ok, ResultJson?, Error?)`, `CommandDescriptor`, `CommandHandler`. JSON-serializable, transport-agnostic. **Clean / stable.**
- `project/contracts/App.Command/Services/IService.cs`, `Clients/IClient.cs` — `IService.ExecuteAsync(CommandRequest)` (the inbound port); `IClient.{Health,Status,Command}Async` (the calling port).
- `project/plugins/App.Command/Services/Service.cs` — handler catalog; `ExecuteAsync` wraps each handler in `_mainThread.InvokeAsync(...)` (~line 84).
- `project/plugins/App.Command/Providers/IMainThreadDispatcher.cs` — the seam + `ImmediateMainThreadDispatcher` (inline no-op). **Vestigial today**: the only caller is `InProcessClient`, already on the main thread.
- `project/plugins/App.Command/Clients/InProcessClient.cs` — the only client; routes straight to `IService`.
- `project/plugins/App.Command/HostComposition/CommandComposition.cs` — composition pushed down per-plugin (in-flight refactor by a parallel agent; `HostCompositionContext` = `LoggerFactory` + `Registry`).
- **No transport.** Ref parallels (read-only): `App.Remote/Services/Service.cs` (HTTP listener + catalog, **bundled together**), `App.Remote.Seam/RemoteBridgeNode.cs` (`ConcurrentQueue` drained in `_Process`, 16/frame), `hosts/agent-cli/Program.cs` (`System.CommandLine`: `health`/`status`/`cmd`/`run`), `tools/record-session.py` (drives via `agent-cli` — "the normal user creation flow"), `tools/shot-window.py` (Windows-only screenshot).

## 3. Decisions (the design)

1. **Command is transport- and source-agnostic.** `App.Command` stays a pure core: register handler → `ExecuteAsync(CommandRequest) → CommandResult`. It knows nothing about *who* issued a command or *how* it arrived.
2. **Remote is a separate inbound adapter that depends on `App.Command`, never the reverse.** New `App.Remote` plugin. `App.Command.Remote` is **rejected** — it encodes the wrong dependency arrow (Command owning remote).
3. **Transport is pluggable inside the ingress via `ITransport`.** `HttpTransport` is the only impl now; named-pipe / WebSocket are documented fast-follows behind the same interface, with zero change to `App.Command`. Start **unified** inside `App.Remote`; split into per-transport projects only when a second transport ships.
4. **Each transport is an `IHostedComponent`** (house Hosting lib): `StartAsync` binds the listener, `StopAsync` drains/closes. `HostingService` gives priority-ordered start + **reverse-stop** (transport closes before the core). Replaces ref's hand-rolled `_remoteService.Start()` + manual dispose.
5. **Main-thread marshaling stays an explicit `IMainThreadDispatcher` seam.** Real impl = a Godot `_Process`-drained queue in `App.Remote.Seam` (mirrors ref `RemoteBridgeNode`); `Immediate` stays for tests/headless. **`R3.Godot` deferred** — the app references base `R3` only (no `R3.Godot` / `GodotFrameProvider` / autoload), so R3 frame scheduling is a *new dependency*, not free, and not justified for one dispatcher. R3 stays in its lane: observable projections (`App.World.FieldView`, `App.Ui.NodeGraph`) and the reactive event stream a future WebSocket transport would push.
6. **Reuse house primitives, don't reinvent** (see §6): ServiceArchi registry, Hosting `IHostedComponent`, Messaging `IMessageBus`.
7. **Driver = python/curl under `tools/`** (mirrors ref `record-session.py`). **No** `agent-cli` .NET host for now. **Exclude** ref's `App.Agent` LLM `run` loop — that is autonomous-LLM operation, not "drive it as a normal user."
8. **Config via crosscut-foundation `Config` (JSON base + Env override) — not raw env.** Options come from the house `CrosscutFoundation.Config.IService`, composed in `App.Common/Bootstrap` exactly like Logging/Messaging: `RegisterJsonConfig` (priority 50) + `RegisterEnvConfig` (priority 90) + `RegisterConfigService`. A `remote` section in the app JSON config (under `project/hosts/complete-app/config/`, where `collectible-bundles.json` already lives) carries `enabled` + `bind` (default `127.0.0.1:19292`). The **bearer `token` stays in Env (or a gitignored local override) — never committed JSON** (`detect-secrets` / `.secrets.baseline`); the Env layer's higher priority (90 > 50) overrides JSON for secrets/deploy. `RemoteOptions` stays a plain value DTO; `RemoteComposition` reads values from the config service (so the transport never knows the config *source*). The transport stamps `ActorKind="http"` + `ActorId` on each `CommandRequest`. A remote-surface failure must never break boot; zero footprint when `remote.enabled` is false. (Supersedes raw-env gating; `RemoteOptions.FromEnvironment` becomes a fallback, config is the primary source wired at composition. This makes App.Remote the app's first crosscut-`Config` adopter.)

## 4. Why this is the conventional design (validation)

Four established patterns converge on exactly this shape — a transport-agnostic command core with transports as edge adapters that depend inward:

- **Hexagonal / Ports & Adapters** (Cockburn) — a *driving adapter* converts tech-specific input into "technology-agnostic requests the domain understands"; the core holds logic, adapters only translate.
- **CQRS command bus** — the bus "decouples the sender from its handler"; a pipeline may add behavior incl. "sending the command over a network"; the *same* command interface works in- or out-of-process.
- **Mediator / MediatR** — controller, gRPC service, and CLI each depend only on the mediator; each transport maps its wire format → the *same* handlers.
- **MCP** (what Claude Code itself runs on) — explicit split of a *data layer* (capabilities, "written once") from a swappable *transport layer* (stdio / HTTP / SSE); custom transports are allowed = our `ITransport`.

Sources: [Ports & Adapters](https://codesoapbox.dev/ports-adapters-aka-hexagonal-architecture-explained/) · [AWS hexagonal](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/hexagonal-architecture.html) · [CQRS command bus](https://gnugat.github.io/2016/05/11/towards-cqrs-command-bus.html) · [Azure CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs) · [MCP transports](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports) · [.NET mediator app-layer](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-application-layer-implementation-web-api).

## 5. Target structure & dependency direction

```
   App.Ui     (user operating the app) ─┐
   host/system (internal commands)      ├─► build CommandRequest → IService.ExecuteAsync
   App.Remote (remote, over a wire)    ─┘
                                            │  every source depends on ↓
                                       App.Command  — pure core: catalog + execute,
                                                      ZERO transport / source / thread knowledge
```

New projects (all **new files** — no edits to existing plugins):

- **`project/plugins/App.Remote`** (T3, plain .NET) — `ITransport`; `HttpTransport : IHostedComponent` (HttpListener → deserialize → `IClient.CommandAsync` → serialize → HTTP response); bearer-token auth; a plain static `RemoteComposition` entry that resolves `IService` / `IMainThreadDispatcher` from the registry, then registers + starts enabled transports. References `contracts/App.Command`, ServiceArchi, Hosting (Messaging optional).
- **`project/plugins/App.Remote.Seam`** (T4, `Godot.NET.Sdk`) — `RemoteBridgeNode : Node, IMainThreadDispatcher`: the `_Process`-drained main-thread queue. T4 is the only tier permitted to touch Godot (`vault/architecture/service-tier-architecture.md`).
- **`project/tests/App.Remote.Tests`** — headless xUnit: start `HttpTransport`, POST a command, assert it routes to a fake `IService` and returns the `CommandResult`; auth-reject; unknown-command error.
- **`tools/fantasim-cmd.py`** (+ curl snippets) — `health` / `status` / `cmd <id> <payload-json>` over HTTP; the "operate the windowed app as a normal user" driver.

## 6. House-library reuse

| Concern | House primitive | Repo (verify exact namespace at build) | Reuse instead of hand-rolling |
|---|---|---|---|
| Registration / discovery | ServiceArchi / RegistryArchi *(already used)* | `plate-projects/service-archi/dotnet/` | `IRegistry` + tags (`transport`/`ui`/`system`/`remote`) + `RegisterOwned<T>()→IDisposable` |
| Transport **lifecycle** | `Hosting.IHostedComponent` + `HostingService` | `plate-projects/crosscut-foundation/dotnet/src/Hosting.*` | ordered `StartAsync`/`StopAsync` + reverse-stop |
| Event / result **push** (WS later) | `Messaging.IMessageBus` (Cysharp MessagePipe) | `plate-projects/crosscut-foundation/dotnet/src/Messaging.*` | `Publish`/`Subscribe`; no hand-rolled fan-out |

> Reuse the house libraries — do not reinvent these primitives (workspace rule). Confirm exact type/namespace names from source when wiring; the survey located them but the build must verify.

## 7. The `IMainThreadDispatcher` resolution

Main-thread marshaling is an **ingress concern** (only an off-thread source needs it), so the real impl belongs in `App.Remote.Seam`, not the core. End state: `App.Command` keeps only the injected seam interface (or it moves out entirely — a small follow-up once the pushdown settles, to avoid collision). The "vestigial" look disappears the moment a transport uses it.

## 8. Coordination (multi-agent — build isolation)

A parallel agent is mid "HostComposition pushdown" (uncommitted `Host.cs`, `App.Command.csproj`, new `*/HostComposition/`). To avoid collision:

1. Build in a **git worktree off `c817985`** (the clean, green baseline). The dispatched agent creates **only new files** (the two plugins + tests + driver) and **must not modify** existing files **except** adding the new projects to `FantaSim.sln`.
2. The composition entry is a **plain static method** (`registry`, `loggerFactory` args) so it slots into either `Host.cs` or the new `HostComposition` pattern at integration time.
3. **Lead-session integrates after the pushdown commits**: add one host call to `RemoteComposition.Compose(...)` and confirm `.sln`. That single host line + the `.sln` entry are the *only* shared-file touches.

## 9. Out of scope / fast-follows

- Named-pipe + WebSocket transports (behind `ITransport`); WebSocket pairs with `IMessageBus` for per-tick streaming (ref `record-session` frame capture).
- `R3.Godot` frame scheduling (only if adopted app-wide).
- A `.NET` `agent-cli` host (if we later want ref parity or the LLM `run` loop).
- A macOS screenshot tool (replacing Windows-only `shot-window.py`) for visual verification.
