using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSeedPolicyTests
{
    [Fact]
    public void Same_branch_id_maps_to_the_same_seed()
        => Assert.Equal(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor("main"));

    [Fact]
    public void Distinct_branch_ids_map_to_distinct_seeds()
        => Assert.NotEqual(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor("import-b"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_branch_falls_back_to_main(string? branch)
        => Assert.Equal(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor(branch));

    [Fact]
    public void Seed_is_stable_across_runs()
        // Locks the encoding so a refactor cannot silently re-bend every tunnel.
        // Golden value is the FNV-1a-64 hash of "main" under the spec implementation
        // (offset basis 14695981039346656037, prime 1099511628211, UTF-16 low-then-high byte).
        // Corrected from the plan's draft constant 0xC29FDD00E9E48F0E, which did not match the
        // spec algorithm; the implementation itself was unchanged.
        => Assert.Equal(unchecked((long)0xd9454cea63131806UL), TunnelBoreSeedPolicy.SeedFor("main"));
}
