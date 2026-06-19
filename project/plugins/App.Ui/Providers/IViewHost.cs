namespace FantaSim.App.Ui.Providers;

public interface IViewHost
{
    void Mount(string viewId);

    void Unmount(string viewId);
}
