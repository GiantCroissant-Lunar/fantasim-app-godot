using System.Collections.Generic;

namespace FantaSim.App.NodeGraph;

public static class FunctionProviderDetailFormatter
{
    public static List<string> Format(FunctionProviderMetadata? metadata, FunctionExecutionTraits? traits)
    {
        var lines = new List<string>();

        if (metadata != null)
        {
            var providerKind = metadata.ProviderKind;
            var providerId = metadata.ProviderId;
            lines.Add(!string.IsNullOrEmpty(providerId)
                ? $"provider: {providerKind} / {providerId}"
                : $"provider: {providerKind}");

            var runtime = metadata.RuntimeRequirement;
            if (!string.IsNullOrEmpty(runtime))
                lines.Add($"runtime: {runtime}");
        }

        if (traits != null)
        {
            var traitParts = new List<string>();
            if (traits.RequiresExternalProcess == true)
                traitParts.Add("external-process");
            if (traits.RequiresNetwork == true)
                traitParts.Add("network");
            if (traits.RequiresMainThread == true)
                traitParts.Add("main-thread");
            if (traits.SupportsCancellation == true)
                traitParts.Add("cancellable");
            if (traits.DefaultTimeoutSeconds is { } timeout)
                traitParts.Add($"timeout {timeout}s");

            if (traitParts.Count > 0)
                lines.Add($"traits: {string.Join(", ", traitParts)}");

            var artifact = traits.ArtifactShape;
            if (!string.IsNullOrEmpty(artifact))
                lines.Add($"artifact: {artifact}");
        }

        return lines;
    }
}
