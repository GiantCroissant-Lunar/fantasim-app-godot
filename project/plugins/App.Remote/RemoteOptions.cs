namespace FantaSim.App.Remote;

public sealed record RemoteOptions(bool Enabled, string BindAddress, string? Token)
{
    public const string EnabledEnvVar = "FANTASIM_REMOTE_ENABLED";
    public const string BindEnvVar = "FANTASIM_REMOTE_BIND";
    public const string TokenEnvVar = "FANTASIM_REMOTE_TOKEN";
    public const string DefaultBindAddress = "127.0.0.1:19292";

    public static RemoteOptions FromEnvironment()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable(EnabledEnvVar),
            "1",
            StringComparison.Ordinal);

        var bindAddress = Environment.GetEnvironmentVariable(BindEnvVar);
        if (string.IsNullOrWhiteSpace(bindAddress))
            bindAddress = DefaultBindAddress;

        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token))
            token = null;

        return new RemoteOptions(enabled, bindAddress, token);
    }
}
