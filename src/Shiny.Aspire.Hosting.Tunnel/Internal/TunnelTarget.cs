using Aspire.Hosting.ApplicationModel;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

static class TunnelTarget
{
    /// <summary>
    /// Picks the endpoint to publish. An explicit name wins; otherwise <c>http</c> is preferred and
    /// a resource with exactly one endpoint needs no naming at all.
    /// <para>
    /// http rather than https on purpose. TLS is terminated out at the public end of the tunnel, so
    /// what arrives here is cleartext — pointing it at the target's TLS port would put plaintext
    /// HTTP into a TLS listener and fail every request.
    /// </para>
    /// </summary>
    public static EndpointReference Resolve(IResourceWithEndpoints target, string? endpointName)
    {
        if (endpointName is { Length: > 0 })
            return target.GetEndpoint(endpointName);

        var endpoints = target.GetEndpoints().ToList();

        if (endpoints.Count == 0)
            throw new InvalidOperationException(
                $"Resource '{target.Name}' has no endpoints, so there is nothing for a tunnel to publish."
            );

        var chosen =
            endpoints.FirstOrDefault(x => x.EndpointName == "http")
            ?? endpoints.FirstOrDefault(x => x.EndpointName == "https");

        if (chosen is not null)
            return chosen;

        if (endpoints.Count > 1)
            throw new InvalidOperationException(
                $"Resource '{target.Name}' has several endpoints ({string.Join(", ", endpoints.Select(x => x.EndpointName))}) "
                    + "and none of them is named http or https. Name the one to publish."
            );

        return endpoints[0];
    }
}
