using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class DurableRotationRestartToolContractTests
{
    [Fact]
    public void Tool_requires_fresh_write_then_server_restart_then_cold_read_proof()
    {
        var source = File.ReadAllText(ProjectFile("tools/verify-durable-rotation-restart.sh"));

        Assert.Contains("FANTASIM_ROTATION_RESTART_PROOF_PHASE=write", source, StringComparison.Ordinal);
        Assert.Contains("FANTASIM_ROTATION_RESTART_PROOF_PHASE=read", source, StringComparison.Ordinal);
        Assert.Contains("FANTASIM_ROTATION_RESTART_PROOF_RECEIPT", source, StringComparison.Ordinal);
        Assert.Contains("rocksdb:", source, StringComparison.Ordinal);
        Assert.Contains("pid_file=\"$evidence_dir/${label}-server.pid\"", source, StringComparison.Ordinal);
        Assert.Contains("start_server first", source, StringComparison.Ordinal);
        Assert.Contains("start_server second", source, StringComparison.Ordinal);
        Assert.Contains("kill -INT", source, StringComparison.Ordinal);
        Assert.Contains("wait", source, StringComparison.Ordinal);
        Assert.Contains("first_pid", source, StringComparison.Ordinal);
        Assert.Contains("second_pid", source, StringComparison.Ordinal);
        Assert.Contains("FANTASIM_ROTATION_RESTART_KEEP_EVIDENCE", source, StringComparison.Ordinal);
        Assert.Contains("trap cleanup EXIT", source, StringComparison.Ordinal);
    }

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file '{relativePath}'.");
    }
}
