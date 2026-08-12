using Aspire.Hosting.ApplicationModel;
using Shiny.Aspire.Hosting.Tunnel.Internal;


namespace Aspire.Hosting;

/// <summary>
/// Publishing Aspire endpoints through Cloudflare Tunnel.
/// </summary>
public static class CloudflareTunnelExtensions
{
    const string Registry = "docker.io";
    const string Image = "cloudflare/cloudflared";
    const string Tag = "latest";

    /// <summary>
    /// Adds a <c>cloudflared</c> agent. Point it at an endpoint with <see cref="WithOrigin"/>, or
    /// let <see cref="WithCloudflareTunnel{TTarget}"/> do both in one line.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the agent.</param>
    public static IResourceBuilder<CloudflaredResource> AddCloudflareTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new CloudflaredResource(name);

        ContainerTunnelUrlWatcher.Watch(builder, resource);

        return builder
            .AddResource(resource)
            .WithImage(Image, Tag)
            .WithImageRegistry(Registry)
            .WithArgs(context =>
            {
                context.Args.Add("tunnel");

                // The agent updating itself mid-session is a surprise nobody asked for, and in a
                // container it is undone on the next pull anyway.
                context.Args.Add("--no-autoupdate");

                if (resource.Token is not null)
                {
                    // A named tunnel takes its ingress rules from the Cloudflare account, so there
                    // is no origin to pass here — the token names the tunnel and the tunnel knows.
                    context.Args.Add("run");
                    context.Args.Add("--token");
                    context.Args.Add(resource.Token);

                    return;
                }

                if (resource.TargetEndpoint is not { } endpoint)
                    throw new InvalidOperationException(
                        $"Tunnel '{resource.Name}' has nothing to publish. Call WithOrigin(endpoint), "
                            + "or WithNamedTunnel(token, url) for a tunnel configured in Cloudflare."
                    );

                context.Args.Add("--url");
                context.Args.Add(ReferenceExpression.Create($"http://{endpoint.Property(EndpointProperty.HostAndPort)}"));
            })
            .ExcludeFromManifest();
    }

    /// <summary>Points the agent at the endpoint it should publish.</summary>
    public static IResourceBuilder<CloudflaredResource> WithOrigin(
        this IResourceBuilder<CloudflaredResource> agent,
        EndpointReference endpoint
    )
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(endpoint);

        agent.Resource.TargetEndpoint = endpoint;

        // Deliberately not waiting for the target: the agent may as well be up and holding an
        // address before the thing it fronts is listening. cloudflared answers 502 until then,
        // which is the honest answer and a great deal more useful than no address at all.
        return agent.WithParentRelationship(endpoint.Resource);
    }

    /// <summary>
    /// Runs a named tunnel from your own Cloudflare account instead of a throwaway one.
    /// <para>
    /// The address is yours and stable, which is the point: a webhook registered against it keeps
    /// working after a restart. Ingress is configured in Cloudflare rather than here, so the URL is
    /// stated rather than discovered.
    /// </para>
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="token">The tunnel token, from a parameter.</param>
    /// <param name="publicUrl">The host name Cloudflare routes to this tunnel.</param>
    public static IResourceBuilder<CloudflaredResource> WithNamedTunnel(
        this IResourceBuilder<CloudflaredResource> agent,
        IResourceBuilder<ParameterResource> token,
        string publicUrl
    )
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicUrl);

        agent.Resource.Token = token.Resource;
        agent.Resource.SetPublicUrl(publicUrl);

        return agent;
    }

    /// <summary>
    /// Publishes this resource through a quick Cloudflare tunnel, creating the agent in the same line.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="name">Resource name for the agent. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    public static IResourceBuilder<TTarget> WithCloudflareTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        string? name = null,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);

        target
            .ApplicationBuilder.AddCloudflareTunnel(name ?? $"{target.Resource.Name}-tunnel")
            .WithOrigin(target.GetEndpoint(endpointName ?? "http"));

        return target;
    }
}
