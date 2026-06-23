# FantaSim Godot App

A Godot 4+ application for the FantaSim world simulation platform. This project
implements the Godot host layer of the FantaSim architecture — the visual,
interactive front-end that renders simulated worlds and surfaces their data
through a node-graph UI.

## Architecture

The app is structured as a **service-tier architecture** with four tiers (T1–T4)
that separate concerns across **collectible bundles** — hot-reloadable PCK
packages loaded at runtime by the Godot host:

| Tier | Bundle | Purpose |
|------|--------|---------|
| T1 | `stage` | Scene-level orchestration, camera, lighting |
| T2 | `assist` | AI-assisted interaction, command dispatch |
| T3 | `timeline` | Time-advancement HUD, epoch/event display |
| T4 | `activity` | Activity ledger UI, event log |

Each bundle is a self-contained Godot PCK with a manifest, optional scene, and
a .NET assembly DLL. Bundles are built independently and hot-reloaded into the
running exported app — no full rebuild required for bundle-scoped changes.

### Hosts

- **`complete-app`** — The main Godot project. Exports as a standalone desktop
  app that loads bundles at runtime. Contains the Rust gdext bridge, GPU compute
  shaders, and the full plugin surface.
- **`content-app`** — A minimal Godot project used as the export source for
  collectible bundle PCKs. Contains only the bundle scenes and export presets.

### Plugins

29+ C# plugin projects under `project/plugins/`, organized by domain:

- `App.World.*` — World rendering, field-of-view, composition
- `App.Ui.*` — UI components (activity, node-graph)
- `App.Timeline` — Time-advancement HUD
- `App.Ecs` — ECS integration (Akka.NET-based)
- `App.GpuCompute` / `App.GpuShader` — GPU compute and shader management
- `App.NodeGraph` — Node-graph paradigm runtime
- `App.Iii` — iii orchestration engine integration
- `App.Resource` — Resource management and bundle loading
- `App.Camera` / `App.Audio` / `App.Command` / `App.SceneFlow` — Core subsystems

### Native bridge

A Rust gdext bridge (`iii-bridge`) provides native integration with the
iii orchestration engine. Built with `cargo` and staged into the Godot host
as a `libiii_bridge.dylib` cdylib.

### Workers

Python iii workers under `project/workers/`:

- **comfy-worker** — ComfyUI-based image generation
- **blender-worker** — Blender text-to-3D pipeline
- **pipeline-worker** — Orchestration pipeline coordinator

## Prerequisites

- [Godot 4.4+ Mono](https://godotengine.org/download/macos/) (installed at
  `tools/Godot_mono.app` in the workspace root)
- .NET 8 SDK
- Rust toolchain (for the gdext bridge)
- Python 3.11+ (for iii workers)
- [Task](https://taskfile.dev/) — the project task runner

## Quick start

```sh
# Restore .NET tools and dependencies
task restore

# Build the full solution
task build

# Run tests
task test

# Run the Godot editor
task run

# Run headless
task run:headless
```

## Bundle development

The hot-reload workflow for bundle-scoped changes:

```sh
# Build and export a single bundle
task bundle:stage       # T1 stage bundle
task bundle:assist      # T2 assist bundle
task bundle:timeline    # T3 timeline bundle
task bundle:activity    # T4 activity bundle

# Export all bundles
task bundles

# Install bundles into the exported app
task bundle:install

# Launch the exported windowed app (bundle load is observable)
task run:exported
```

See [bundle-hot-reload-verify](.agent/rules/bundle-hot-reload-verify.md) for
the full verification loop.

## Rust bridge

```sh
# Build the gdext iii bridge (debug) and stage it into the Godot host
task bridge:build
```

## iii workers

```sh
# Set up the Python virtual environment
task workers:setup

# Start the iii orchestration engine
task iii:engine

# Run workers (in another terminal)
task workers:run

# Fire a text-to-3D pipeline job
task pipeline:test PROMPT="a small red toy cube"
```

## Build & versioning

- **UnifyBuild** (`dotnet unify-build`) drives Godot exports and artifact
  packaging. Configuration lives in `build.config.json`.
- **GitVersion** derives semantic versions from git history (trunk-based
  development, mainline strategy). Tag with `v0.1.x` for releases.
- **git-cliff** generates `CHANGELOG.md` from conventional commits.

```sh
task build:godot          # Export for all configured platforms
task build:godot:desktop  # Export for desktop only
task changelog            # Generate changelog
```

## Documentation

Project documentation lives in `vault/`:

- `vault/architecture/` — Evergreen subsystem and design docs
- `vault/specs/` — Dated, concept-lock feature design specs
- `vault/plans/` — Dated implementation plans
- `vault/handover/` — Dated session records

Key architecture docs include the [service-tier architecture](vault/architecture/service-tier-architecture.md),
[cross-ALC rules](vault/architecture/cross-alc-rules.md), and the
[node-graph paradigm](vault/architecture/node-graph-paradigm.md).

## License

Proprietary — internal to the lunar-horse workspace.
