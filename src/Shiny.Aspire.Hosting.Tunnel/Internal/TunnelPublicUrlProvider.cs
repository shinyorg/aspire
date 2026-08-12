using Aspire.Hosting.ApplicationModel;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// Hands a tunnel's public URL to whatever asked for it, waiting for the tunnel to open first.
/// </summary>
sealed class TunnelPublicUrlProvider(ITunnelResource resource) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression => $"{{{resource.Name}.connectionString}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default) =>
        await resource.GetPublicUrlAsync(cancellationToken).ConfigureAwait(false);
}
