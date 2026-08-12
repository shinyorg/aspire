using Aspire.Hosting.ApplicationModel;
using Shiny.Net.HttpServer.Ssh;

namespace Aspire.Hosting;

/// <summary>
/// Publishing Aspire endpoints through SSH — a server you own, or a zero-account quick tunnel.
/// </summary>
public static class SshTunnelExtensions
{
    /// <summary>
    /// Adds a tunnel through a hosted endpoint that needs no account and nothing installed.
    /// <para>
    /// The address is assigned by the endpoint and changes on every reconnect, so read it from the
    /// dashboard or hand it to a resource with <c>WithTunnelUrl</c> rather than writing it down.
    /// Anonymous pinggy tunnels are capped at 60 minutes; pass an access token as
    /// <paramref name="subdomain"/> to lift that.
    /// </para>
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the tunnel.</param>
    /// <param name="host">Which hosted endpoint to use.</param>
    /// <param name="subdomain">Subdomain or access token to ask for, where the host supports one.</param>
    /// <param name="configure">Anything else on the options.</param>
    public static IResourceBuilder<SshTunnelResource> AddQuickTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        QuickTunnelHost host = QuickTunnelHost.Pinggy,
        string? subdomain = null,
        Action<SshTunnelOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = QuickTunnel.BuildOptions(host, subdomain);
        configure?.Invoke(options);

        return builder.AddSshTunnel(name, options);
    }

    /// <summary>
    /// Adds a tunnel through an SSH server you can log in to.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the tunnel.</param>
    /// <param name="options">Where to connect and what to ask for.</param>
    public static IResourceBuilder<SshTunnelResource> AddSshTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        SshTunnelOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        var resource = new SshTunnelResource(name, options);

        return builder
            .AddResource(resource)
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "Tunnel",
                    State = KnownResourceStates.NotStarted,
                    CreationTimeStamp = DateTime.UtcNow,
                    Properties = [new ResourcePropertySnapshot("tunnel.kind", "ssh")]
                }
            )
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Publishes this resource through a zero-account quick tunnel, creating the tunnel resource in
    /// the same line.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="host">Which hosted endpoint to use.</param>
    /// <param name="subdomain">Subdomain or access token to ask for, where the host supports one.</param>
    /// <param name="name">Resource name for the tunnel. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    /// <param name="configure">Anything else on the options.</param>
    public static IResourceBuilder<TTarget> WithQuickTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        QuickTunnelHost host = QuickTunnelHost.Pinggy,
        string? subdomain = null,
        string? name = null,
        string? endpointName = null,
        Action<SshTunnelOptions>? configure = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);

        var tunnel = target.ApplicationBuilder.AddQuickTunnel(
            name ?? $"{target.Resource.Name}-tunnel",
            host,
            subdomain,
            configure
        );

        return target.WithTunnel(tunnel, endpointName);
    }

    /// <summary>
    /// Publishes this resource through an SSH server you can log in to, creating the tunnel
    /// resource in the same line.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="sshHost">The SSH host, e.g. <c>tunnel.example.com</c>.</param>
    /// <param name="configure">
    /// The rest of the options — at minimum a username, a credential, and either a pinned host key
    /// fingerprint or <c>AcceptAnyHostKey</c>.
    /// </param>
    /// <param name="name">Resource name for the tunnel. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    public static IResourceBuilder<TTarget> WithSshTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        string sshHost,
        Action<SshTunnelOptions> configure,
        string? name = null,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(sshHost);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SshTunnelOptions { Host = sshHost };
        configure(options);

        var tunnel = target.ApplicationBuilder.AddSshTunnel(name ?? $"{target.Resource.Name}-tunnel", options);

        return target.WithTunnel(tunnel, endpointName);
    }
}
