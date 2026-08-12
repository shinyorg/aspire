using Aspire.Hosting.ApplicationModel;
using Shiny.Net.HttpServer.Tunneling;

namespace Aspire.Hosting;

/// <summary>
/// Publishing a locally-bound endpoint at a public address.
/// </summary>
public static class TunnelExtensions
{
    /// <summary>
    /// Adds a tunnel backed by an <see cref="ITunnelProvider"/> of your own.
    /// <para>
    /// The provider is the same abstraction Shiny.Net.HttpServer tunnels through: a source of
    /// connections that arrived from somewhere other than a local socket. Everything the provider
    /// hands over is pumped into the endpoint attached with <c>WithTunnel</c>.
    /// </para>
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the tunnel.</param>
    /// <param name="kind">Short provider name, shown in the dashboard.</param>
    /// <param name="factory">Builds the provider once the target's port is known.</param>
    public static IResourceBuilder<InProcessTunnelResource> AddTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string kind,
        TunnelProviderFactory factory
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(factory);

        var resource = new InProcessTunnelResource(name, kind, factory);

        return builder
            .AddResource(resource)
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "Tunnel",
                    State = KnownResourceStates.NotStarted,
                    CreationTimeStamp = DateTime.UtcNow,
                    Properties = [new ResourcePropertySnapshot("tunnel.kind", kind)]
                }
            )
            // A tunnel is a development-time affordance for reaching a machine that is not on the
            // internet. Deployed apps have real addresses, so this has no place in the manifest.
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Publishes one of this resource's endpoints through the given tunnel.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="tunnel">The tunnel that will carry it.</param>
    /// <param name="endpointName">
    /// Endpoint to publish. Left null, the <c>http</c> endpoint is used — what arrives from a tunnel
    /// is cleartext, so the target's TLS port is the wrong end to point at.
    /// </param>
    public static IResourceBuilder<TTarget> WithTunnel<TTarget, TTunnel>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<TTunnel> tunnel,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
        where TTunnel : TunnelResource
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tunnel);

        tunnel.Resource.AttachTarget(target.ApplicationBuilder, target.Resource, endpointName);
        tunnel.WithParentRelationship(target.Resource);

        return target;
    }

    /// <summary>
    /// Publishes an endpoint through this tunnel — the same wiring as
    /// <c>WithTunnel</c>, written from the tunnel's side.
    /// </summary>
    public static IResourceBuilder<TTunnel> WithTargetEndpoint<TTunnel>(
        this IResourceBuilder<TTunnel> tunnel,
        EndpointReference endpoint
    )
        where TTunnel : TunnelResource
    {
        ArgumentNullException.ThrowIfNull(tunnel);
        ArgumentNullException.ThrowIfNull(endpoint);

        tunnel.Resource.AttachTarget(tunnel.ApplicationBuilder, endpoint.Resource, endpoint.EndpointName);
        tunnel.WithParentRelationship(endpoint.Resource);

        return tunnel;
    }

    /// <summary>
    /// Sets an environment variable on this resource to a tunnel's public URL, waiting for the
    /// tunnel to open before the resource starts.
    /// <para>
    /// The usual reason is a service that has to hand out its own address — an OAuth redirect URI,
    /// a webhook registration, a QR code — which it cannot derive from the port it is listening on.
    /// </para>
    /// </summary>
    public static IResourceBuilder<T> WithTunnelUrl<T, TTunnel>(
        this IResourceBuilder<T> builder,
        string name,
        IResourceBuilder<TTunnel> tunnel
    )
        where T : IResourceWithEnvironment
        where TTunnel : ITunnelResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tunnel);

        return builder.WithEnvironment(name, tunnel.Resource.PublicUrlExpression);
    }
}
