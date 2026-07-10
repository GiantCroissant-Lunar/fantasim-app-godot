using FantaSim.App.Common;
using Xunit;

namespace FantaSim.App.Common.Tests;

public class AppConfigLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("app-config-locator-").FullName;

    private string MakeDir(string name, bool withAppJson)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (withAppJson)
        {
            Directory.CreateDirectory(Path.Combine(dir, "config"));
            File.WriteAllText(Path.Combine(dir, "config", "app.json"), "{}");
        }
        return dir;
    }

    [Fact]
    public void PrefersBaseDirectoryWhenPresent()
    {
        var baseDir = MakeDir("base", withAppJson: true);
        var exeDir = MakeDir("exe", withAppJson: true);
        Assert.Equal(
            Path.Combine(baseDir, "config", "app.json"),
            AppConfigLocator.ResolveAppJsonPath(baseDir, exeDir));
    }

    [Fact]
    public void FallsBackToExeDirectoryInExports()
    {
        // Godot macOS export: BaseDirectory = per-arch data dir with no config/;
        // the provisioned config/ lives next to the executable (G26).
        var baseDir = MakeDir("data_osx_arm64", withAppJson: false);
        var exeDir = MakeDir("MacOS", withAppJson: true);
        Assert.Equal(
            Path.Combine(exeDir, "config", "app.json"),
            AppConfigLocator.ResolveAppJsonPath(baseDir, exeDir));
    }

    [Fact]
    public void NeitherPresentReturnsBaseDirectoryPath()
    {
        // Editor/dev runs may legitimately have no app.json; the optional load must
        // keep no-opping on the same path it always used.
        var baseDir = MakeDir("base", withAppJson: false);
        var exeDir = MakeDir("exe", withAppJson: false);
        Assert.Equal(
            Path.Combine(baseDir, "config", "app.json"),
            AppConfigLocator.ResolveAppJsonPath(baseDir, exeDir));
    }

    [Fact]
    public void NullOrEmptyExeDirectoryIsSkipped()
    {
        var baseDir = MakeDir("base", withAppJson: false);
        Assert.Equal(
            Path.Combine(baseDir, "config", "app.json"),
            AppConfigLocator.ResolveAppJsonPath(baseDir, null));
        Assert.Equal(
            Path.Combine(baseDir, "config", "app.json"),
            AppConfigLocator.ResolveAppJsonPath(baseDir, ""));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
