using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Ui;

[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    Task ShowAsync(string viewId, CancellationToken cancellationToken = default);

    Task HideAsync(string viewId, CancellationToken cancellationToken = default);

    IReadOnlyList<string> ActiveViews { get; }

    event Action? ViewsChanged;
}
