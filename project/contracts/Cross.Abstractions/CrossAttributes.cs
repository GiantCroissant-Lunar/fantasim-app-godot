using System;

namespace FantaSim.Cross;

/// <summary>
/// Marks a <c>partial</c> class as a binder that forwards the Tier-1 service interface
/// <paramref name="serviceInterface"/>. <c>FantaSim.Cross.SourceGenerators</c> emits, for every
/// <see cref="CrossDelegateAttribute"/>-marked method on that interface, a forwarding method plus a
/// <c>BindCrossTarget</c> / <c>UnbindCrossTarget</c> / <c>IsCrossBound</c> trio.
/// </summary>
/// <remarks>
/// This codegens the resident-to-collectible-ALC binder pattern. The partial class is a thin RESIDENT
/// binder - a plain C# class, or a Godot <c>Node</c> when it lives in the scene tree (its base type,
/// if any, comes from the hand-written part, so the generator stays engine-agnostic). The bound target
/// is the plain-C# Tier-3 implementation, which may live in a collectible bundle ALC and is re-bound on
/// hot-reload; <c>UnbindCrossTarget()</c> drops the only strong ref so the bundle ALC can collect. It is
/// the generated equivalent of App.Ui.Seam/ViewRenderer's hand-written <c>IViewSource</c> binder. See
/// vault/architecture/cross-alc-rules.md (R2, the binder pattern).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CrossServiceAttribute : Attribute
{
    public CrossServiceAttribute(Type serviceInterface)
        => ServiceInterface = serviceInterface ?? throw new ArgumentNullException(nameof(serviceInterface));

    /// <summary>The Tier-1 interface whose <see cref="CrossDelegateAttribute"/> methods are forwarded.</summary>
    public Type ServiceInterface { get; }
}

/// <summary>
/// Marks one interface method as part of the bound surface. Only methods carrying this attribute are
/// forwarded, so a Tier-1 contract can keep C#-only members (and base-interface members) outside it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CrossDelegateAttribute : Attribute
{
}
