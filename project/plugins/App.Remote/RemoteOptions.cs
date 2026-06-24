namespace FantaSim.App.Remote;

public sealed record RemoteOptions(bool Enabled, string BindAddress, string? Token)
{
    public const string DefaultBindAddress = "127.0.0.1:19292";

    // Layered crosscut config: remote:enabled + remote:bind come from app.json (Env-overridable via
    // remote__enabled / remote__bind). The bearer remote:token is a SECRET: never in committed JSON --
    // supply it via the Env layer only (set remote__token).
    public static RemoteOptions FromConfig(CrosscutFoundation.Config.IService config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var enabled = config.GetValue("remote:enabled", false);

        var bindAddress = config.Get("remote:bind");
        if (string.IsNullOrWhiteSpace(bindAddress))
            bindAddress = DefaultBindAddress;

        var token = config.Get("remote:token");
        if (string.IsNullOrWhiteSpace(token))
            token = null;

        return new RemoteOptions(enabled, bindAddress, token);
    }
}
