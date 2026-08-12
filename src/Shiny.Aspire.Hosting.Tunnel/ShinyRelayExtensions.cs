using System.Net;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shiny.Aspire.Hosting.Tunnel.Internal;
using Shiny.Net.HttpServer.Tunneling;

namespace Aspire.Hosting;

/// <summary>
/// Hosting the Shiny relay in the app model, and publishing resources through it.
/// </summary>
public static class ShinyRelayExtensions
{
    /// <summary>
    /// Runs a Shiny relay inside the AppHost: a control port for tunnel clients to register on, and
    /// a public port where traffic arrives to be routed by Host header.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the relay.</param>
    /// <param name="controlPort">Where tunnel clients register.</param>
    /// <param name="publicPort">Where public traffic arrives.</param>
    public static IResourceBuilder<ShinyRelayResource> AddShinyRelay(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int controlPort = 5050,
        int publicPort = 8080
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = new RelayServerOptions { ControlPort = controlPort, PublicPort = publicPort };
        var resource = new ShinyRelayResource(name, options);

        // Ports are the relay's own, not Aspire's: it listens in this process rather than being
        // launched as a container or an executable, so there is nothing for the orchestrator to
        // proxy and nothing to allocate.
        builder.Eventing.Subscribe<InitializeResourceEvent>(
            resource,
            (@event, cancellationToken) =>
            {
                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();
                var runner = new ShinyRelayRunner(resource, @event.Services);

                _ = Task.Run(() => runner.RunAsync(lifetime.ApplicationStopping), CancellationToken.None);

                return Task.CompletedTask;
            }
        );

        return builder
            .AddResource(resource)
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "ShinyRelay",
                    State = KnownResourceStates.NotStarted,
                    CreationTimeStamp = DateTime.UtcNow,
                    Properties = []
                }
            )
            .ExcludeFromManifest();
    }

    /// <summary>
    /// The shared secret a client must present to register. Null accepts anything, which is only
    /// reasonable while the relay is bound to loopback.
    /// </summary>
    public static IResourceBuilder<ShinyRelayResource> WithToken(
        this IResourceBuilder<ShinyRelayResource> builder,
        string token
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        builder.Resource.Options.Token = token;

        return builder;
    }

    /// <summary>The registration token, from a parameter so it stays out of the app model's source.</summary>
    public static IResourceBuilder<ShinyRelayResource> WithToken(
        this IResourceBuilder<ShinyRelayResource> builder,
        IResourceBuilder<ParameterResource> token
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(token);

        builder.Resource.TokenParameter = token.Resource;

        return builder;
    }

    /// <summary>
    /// Base domain for assigned hosts — <c>example.com</c> gives <c>abc123.example.com</c>.
    /// </summary>
    /// <param name="builder">The relay builder.</param>
    /// <param name="domain">The base domain.</param>
    /// <param name="scheme">Scheme advertised in the public URL.</param>
    /// <param name="includePort">
    /// Whether the public port belongs in the advertised URL. Set false when something in front of
    /// the relay answers on the scheme's default port.
    /// </param>
    public static IResourceBuilder<ShinyRelayResource> WithDomain(
        this IResourceBuilder<ShinyRelayResource> builder,
        string domain,
        string scheme = "http",
        bool includePort = true
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        builder.Resource.Options.Domain = domain;
        builder.Resource.Options.PublicScheme = scheme;
        builder.Resource.Options.IncludePortInPublicUrl = includePort;

        return builder;
    }

    /// <summary>
    /// The interface both listeners bind. Loopback by default; bind an outward-facing address when
    /// devices off this machine — a phone on the same Wi-Fi — have to register.
    /// </summary>
    /// <param name="builder">The relay builder.</param>
    /// <param name="address">The address to bind, e.g. <c>0.0.0.0</c>.</param>
    /// <param name="clientHost">
    /// What clients should dial. Defaults to <paramref name="address"/>, which is wrong for
    /// <c>0.0.0.0</c> — pass the machine's LAN address or host name there.
    /// </param>
    public static IResourceBuilder<ShinyRelayResource> WithBindAddress(
        this IResourceBuilder<ShinyRelayResource> builder,
        string address,
        string? clientHost = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        builder.Resource.Options.Address = IPAddress.Parse(address);
        builder.Resource.ClientHost = clientHost ?? address;

        return builder;
    }

    /// <summary>Anything the two helpers above do not cover.</summary>
    public static IResourceBuilder<ShinyRelayResource> ConfigureRelay(
        this IResourceBuilder<ShinyRelayResource> builder,
        Action<RelayServerOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(builder.Resource.Options);

        return builder;
    }

    /// <summary>
    /// Publishes this resource through a relay hosted in the same app model.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="relay">The relay added with <see cref="AddShinyRelay"/>.</param>
    /// <param name="subdomain">Subdomain to ask for. Null lets the relay assign one.</param>
    /// <param name="name">Resource name for the tunnel. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    public static IResourceBuilder<TTarget> WithShinyRelayTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<ShinyRelayResource> relay,
        string? subdomain = null,
        string? name = null,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(relay);

        var tunnel = target.ApplicationBuilder.AddTunnel(
            name ?? $"{target.Resource.Name}-tunnel",
            "shiny-relay",
            async (context, cancellationToken) =>
            {
                // The relay is in this process, so waiting for it is exact — no polling a port and
                // no race between a client dialling and the listener binding.
                await relay.Resource.WaitForListeningAsync(cancellationToken).ConfigureAwait(false);

                var options = new RelayTunnelOptions
                {
                    Host = relay.Resource.ClientHost,
                    Port = relay.Resource.Options.ControlPort,
                    Token = relay.Resource.Options.Token,
                    Subdomain = subdomain,
                    UseTls = relay.Resource.Options.ControlHttps is not null
                };

                return new RelayTunnelProvider(options, context.LoggerFactory.CreateLogger<RelayTunnelProvider>());
            }
        );

        tunnel.WithReferenceRelationship(relay.Resource);

        return target.WithTunnel(tunnel, endpointName);
    }

    /// <summary>
    /// Publishes this resource through a relay running somewhere else — the one on your own VPS,
    /// rather than one hosted by this app model.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="host">Relay host name.</param>
    /// <param name="port">Relay control port.</param>
    /// <param name="token">Registration token.</param>
    /// <param name="subdomain">Subdomain to ask for.</param>
    /// <param name="useTls">TLS on the control connection. On by default; registration carries the token.</param>
    /// <param name="name">Resource name for the tunnel. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    public static IResourceBuilder<TTarget> WithShinyRelayTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        string host,
        int port = 5050,
        string? token = null,
        string? subdomain = null,
        bool useTls = true,
        string? name = null,
        string? endpointName = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var tunnel = target.ApplicationBuilder.AddTunnel(
            name ?? $"{target.Resource.Name}-tunnel",
            "shiny-relay",
            (context, cancellationToken) =>
            {
                var options = new RelayTunnelOptions
                {
                    Host = host,
                    Port = port,
                    Token = token,
                    Subdomain = subdomain,
                    UseTls = useTls
                };

                return ValueTask.FromResult<ITunnelProvider>(
                    new RelayTunnelProvider(options, context.LoggerFactory.CreateLogger<RelayTunnelProvider>())
                );
            }
        );

        return target.WithTunnel(tunnel, endpointName);
    }
}
