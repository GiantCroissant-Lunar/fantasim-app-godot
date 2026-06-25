namespace FantaSim.App.NodeGraph;

/// <summary>Well-known values for <see cref="FunctionProviderMetadata.ProviderKind"/>.
/// These are provider execution strategies, not separate node or data identities.</summary>
public static class FunctionProviderKinds
{
    public const string CSharp = "csharp";
    public const string Iii = "iii";
    public const string Akka = "akka";
    public const string Remote = "remote";
    public const string GodotImport = "godot-import";
}
