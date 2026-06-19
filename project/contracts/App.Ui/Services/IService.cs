using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Ui;

[ServiceContract]
public interface IService
{
    Task ShowAsync(string viewId, CancellationToken cancellationToken = default);

    Task HideAsync(string viewId, CancellationToken cancellationToken = default);

    IReadOnlyList<string> ActiveViews { get; }

    event Action? ViewsChanged;
}
