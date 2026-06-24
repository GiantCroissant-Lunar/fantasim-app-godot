using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common;

/// <summary>
/// Immutable context handed to each plugin's HostComposition.ComposeX module during the host
/// composition sequence. Carries the resident app kernel. Godot-free by design (this project is
/// Microsoft.NET.Sdk); T4 seam modules receive Godot handles (SceneTree/Node) as explicit params.
/// </summary>
/// <remarks>
/// Modules MUST NOT retain this context beyond the ComposeX call (no static field holding it):
/// read what you need, register services into <see cref="Registry"/>, return host-owned handles to
/// the caller. There is deliberately no shared mutable state bag on this type — App.Common cannot
/// reference the domain plugins (App.World/App.Ecs/App.GpuCompute), so host-owned handles flow as
/// return values and explicit parameters instead, keeping the data flow visible in signatures.
/// </remarks>
public sealed class HostCompositionContext
{
    public HostCompositionContext(AppComposition composition)
    {
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public AppComposition Composition { get; }

    public IRegistry Registry => Composition.Bootstrap.Registry;

    public ILoggerFactory LoggerFactory => Composition.Bootstrap.LoggerFactory;
}
