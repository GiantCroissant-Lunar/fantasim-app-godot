using BoomHud.Abstractions.Runtime;

namespace FantaSim.App.Ui;

public interface IViewSource
{
    string ViewId { get; }

    RuntimeSurfaceDocument BuildDocument();

    event Action? Changed;

    void Dispatch(string action, string? componentId);
}
