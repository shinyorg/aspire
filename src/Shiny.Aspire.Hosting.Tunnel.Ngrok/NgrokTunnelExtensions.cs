using Aspire.Hosting.ApplicationModel;
using Shiny.Aspire.Hosting.Tunnel.Internal;

namespace Aspire.Hosting;

/// <summary>
/// Publishing Aspire endpoints through ngrok.
/// </summary>
public static class NgrokTunnelExtensions
{
    const string Registry = "docker.io";
    const string Image = "ngrok/ngrok";
    const string Tag = "latest";
    const string AuthTokenVariable = "NGROK_AUTHTOKEN";

    /// <summary>
    /// Adds an ngrok agent. Point it at an endpoint with <see cref="WithOrigin"/>, or let
    /// <see cref="WithNgrokTunnel{TTarget}"/> do both in one line.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the agent.</param>
    /// <param name="authToken">
    /// The account auth token. ngrok will not start an anonymous tunnel, so this is required.
    /// </param>
    public static IResourceBuilder<NgrokResource> AddNgrokTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource> authToken
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(authToken);

        var resource = new NgrokResource(name) { AuthToken = authToken.Resource };

        ContainerTunnelUrlWatcher.Watch(builder, resource);

        return builder
            .AddResource(resource)
            .WithImage(Image, Tag)
            .WithImageRegistry(Registry)
            .WithEnvironment(AuthTokenVariable, authToken)
            .WithArgs(context =>
            {
                if (resource.TargetEndpoint is not { } endpoint)
                    throw new InvalidOperationException(
                        $"Tunnel '{resource.Name}' has nothing to publish. Call WithOrigin(endpoint)."
                    );

                context.Args.Add("http");
                context.Args.Add(ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.HostAndPort)}"));

                if (resource.Domain is { Length: > 0 } domain)
                {
                    context.Args.Add("--url");
                    context.Args.Add($"https://{domain}");
                }

                // Structured output on stdout, where the log watcher can read the assigned address
                // off it. The agent's default is an interactive terminal UI, which prints nothing
                // useful to a container log.
                context.Args.Add("--log");
                context.Args.Add("stdout");
                context.Args.Add("--log-format");
                context.Args.Add("json");
            })
            .ExcludeFromManifest();
    }

    /// <summary>Points the agent at the endpoint it should publish.</summary>
    public static IResourceBuilder<NgrokResource> WithOrigin(
        this IResourceBuilder<NgrokResource> agent,
        EndpointReference endpoint
    )
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(endpoint);

        agent.Resource.TargetEndpoint = endpoint;

        return agent.WithParentRelationship(endpoint.Resource);
    }

    /// <summary>
    /// Answers on a domain reserved in your ngrok account rather than the random one the agent is
    /// otherwise assigned — which is what makes a registered webhook survive a restart.
    /// </summary>
    public static IResourceBuilder<NgrokResource> WithDomain(this IResourceBuilder<NgrokResource> agent, string domain)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        agent.Resource.Domain = domain;

        return agent;
    }

    /// <summary>
    /// Publishes this resource through ngrok, creating the agent in the same line.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="authToken">The ngrok account auth token.</param>
    /// <param name="name">Resource name for the agent. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    public static IResourceBuilder<TTarget> WithNgrokTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<ParameterResource> authToken,
        string? name = null,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(authToken);

        target
            .ApplicationBuilder.AddNgrokTunnel(name ?? $"{target.Resource.Name}-tunnel", authToken)
            .WithOrigin(target.GetEndpoint(endpointName ?? "http"));

        return target;
    }
}
