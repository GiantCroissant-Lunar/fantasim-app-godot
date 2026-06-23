using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FantaSim.Cross.SourceGenerators;

/// <summary>
/// For a <c>partial</c> class annotated <c>[CrossService(typeof(IFoo))]</c>, emits the binder
/// boilerplate that forwards the Tier-1 interface <c>IFoo</c>:
/// <list type="bullet">
///   <item>a bindable <c>__crossTarget</c> field,</item>
///   <item><c>BindCrossTarget(IFoo)</c> / <c>UnbindCrossTarget()</c> / <c>IsCrossBound</c>,</item>
///   <item>one null-safe forwarding method per <c>[CrossDelegate]</c>-marked method on <c>IFoo</c>.</item>
/// </list>
/// <para>
/// This codegens the resident-to-collectible-ALC binder pattern (cross-alc-rules R2): a RESIDENT
/// object forwards a stable method surface to a hot-reloadable Tier-3 implementation in a collectible
/// bundle ALC. <c>UnbindCrossTarget()</c> drops the only strong ref so the ALC can collect on reload;
/// <c>BindCrossTarget(newTarget)</c> re-attaches after. It is the generated equivalent of the
/// hand-written binder in App.Ui.Seam/ViewRenderer (which holds an <c>IViewSource</c> and disposes it
/// before unload).
/// </para>
/// <para>
/// The generator is engine-agnostic - it never references Godot. The partial class can be any resident
/// type (a plain C# binder, or a Godot <c>Node</c> when it lives in the scene tree); the base type, if
/// any, is supplied by the hand-written part.
/// </para>
/// <para>
/// Distinct from the ServiceArchi <c>[RealizeService]</c> T2 proxy, which resolves a Tier-3 from the
/// registry per call (a locator). Use Cross when a concrete resident instance must explicitly
/// bind/unbind a target and the drop timing is load-bearing for ALC collection. Only direct
/// <c>[CrossDelegate]</c> members are forwarded, so a Tier-1 contract can keep C#-only members outside
/// the bound surface.
/// </para>
/// </summary>
[Generator]
public sealed class CrossForwardingGenerator : IIncrementalGenerator
{
    private const string CrossServiceAttributeName = "FantaSim.Cross.CrossServiceAttribute";
    private const string CrossDelegateAttributeName = "FantaSim.Cross.CrossDelegateAttribute";

    private static readonly SymbolDisplayFormat FqFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // All codegen happens in Build (the transform), so the pipeline carries only the
        // equatable GenResult (strings + an equatable LocationInfo) - never Roslyn symbols.
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrossServiceAttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => Build(ctx))
            .Where(static r => r is not null);

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            if (result!.Diagnostic is { } diagnostic)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            if (result.HintName is { } hint && result.Source is { } source)
                spc.AddSource(hint, source);
        });
    }

    private static GenResult? Build(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
            return null;
        var declaration = (ClassDeclarationSyntax)context.TargetNode;

        // The single [CrossService(Type)] ctor argument names the Tier-1 interface to forward.
        var attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol serviceInterface)
            return null;

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return new GenResult(null, null, new DiagnosticInfo(
                Id: "CROSS001",
                Title: "[CrossService] requires a partial class",
                MessageFormat: "Cross surface '{0}' must be declared 'partial' for forwarding generation",
                Category: "FantaSim.Cross",
                Location: LocationInfo.From(declaration.Identifier.GetLocation()),
                MessageArg: type.Name));
        }

        var iface = serviceInterface.ToDisplayString(FqFormat);
        var methods = serviceInterface.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && HasCrossDelegate(m))
            .ToList();

        return new GenResult(HintName(type), Render(type, iface, methods), Diagnostic: null);
    }

    private static bool HasCrossDelegate(IMethodSymbol method) =>
        method.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == CrossDelegateAttributeName);

    private static string Render(INamedTypeSymbol type, string iface, List<IMethodSymbol> methods)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var indent = ns is null ? "" : "    ";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns is not null)
        {
            sb.Append("namespace ").AppendLine(ns);
            sb.AppendLine("{");
        }

        sb.Append(indent).Append(AccessibilityKeyword(type.DeclaredAccessibility))
            .Append(" partial class ").AppendLine(type.Name);
        sb.Append(indent).AppendLine("{");

        sb.Append(indent).Append("    private ").Append(iface).AppendLine("? __crossTarget;");
        sb.AppendLine();
        sb.Append(indent).AppendLine("    /// <summary>Bind the Tier-3 target this surface forwards to. Re-callable to swap the target on hot-reload.</summary>");
        sb.Append(indent).Append("    public void BindCrossTarget(").Append(iface).AppendLine(" target) => __crossTarget = target;");
        sb.AppendLine();
        sb.Append(indent).AppendLine("    /// <summary>Detach the Tier-3 target (call before its collectible ALC unloads).</summary>");
        sb.Append(indent).AppendLine("    public void UnbindCrossTarget() => __crossTarget = null;");
        sb.AppendLine();
        sb.Append(indent).AppendLine("    /// <summary>True while a Tier-3 target is bound.</summary>");
        sb.Append(indent).AppendLine("    public bool IsCrossBound => __crossTarget is not null;");

        foreach (var method in methods)
        {
            var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(FqFormat);
            var parameters = string.Join(", ", method.Parameters.Select(p =>
                p.Type.ToDisplayString(FqFormat) + " " + Escape(p.Name)));
            var arguments = string.Join(", ", method.Parameters.Select(p => Escape(p.Name)));

            sb.AppendLine();
            sb.Append(indent).Append("    public ").Append(returnType).Append(' ')
                .Append(method.Name).Append('(').Append(parameters).AppendLine(")");
            sb.Append(indent).AppendLine("    {");
            if (method.ReturnsVoid)
            {
                sb.Append(indent).Append("        __crossTarget?.").Append(method.Name)
                    .Append('(').Append(arguments).AppendLine(");");
            }
            else
            {
                var defaultExpr = method.ReturnType.IsReferenceType
                    || method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated
                        ? "default!"
                        : "default";
                sb.Append(indent).Append("        return __crossTarget is null ? ").Append(defaultExpr)
                    .Append(" : __crossTarget.").Append(method.Name).Append('(').Append(arguments).AppendLine(");");
            }
            sb.Append(indent).AppendLine("    }");
        }

        sb.Append(indent).AppendLine("}");
        if (ns is not null)
            sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None
            ? name
            : "@" + name;

    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => "internal",
    };

    private static string HintName(INamedTypeSymbol type)
    {
        var full = type.ToDisplayString(FqFormat).Replace("global::", string.Empty);
        var sb = new StringBuilder(full.Length + 20);
        foreach (var ch in full)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        sb.Append(".CrossForwarding.g.cs");
        return sb.ToString();
    }
}

/// <summary>Equatable codegen result carried through the incremental pipeline (no Roslyn symbols).</summary>
internal sealed record GenResult(string? HintName, string? Source, DiagnosticInfo? Diagnostic);

/// <summary>Equatable diagnostic payload; rebuilt into a real <see cref="Diagnostic"/> at output time.</summary>
internal sealed record DiagnosticInfo(
    string Id, string Title, string MessageFormat, string Category, LocationInfo? Location, string MessageArg)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(
        new DiagnosticDescriptor(Id, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true),
        Location?.ToLocation(),
        MessageArg);
}

/// <summary>Equatable location (file path + spans) so the pipeline never caches a SyntaxTree reference.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location location)
        => location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}
