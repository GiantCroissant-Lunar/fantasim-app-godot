namespace FantaSim.App.Resource;

public interface IResourceManaged
{
    IReadOnlyList<IManagedAssembly> Assemblies { get; }
}
