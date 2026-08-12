using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shiny.Aspire.Hosting.Tunnel.Internal;
using Shiny.Net.HttpServer.Tunneling;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Everything a provider needs to open itself, resolved at the moment the tunnel starts rather than
/// when the app model was built — which is the only point at which the target's port is known.
/// </summary>
public sealed class TunnelProviderContext
{
    /// <summary>The AppHost's services.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>The tunnel being opened.</summary>
    public required TunnelResource Resource { get; init; }

    /// <summary>Writes to the tunnel resource's own log stream in the dashboard.</summary>
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>Host the tunnel should forward to, as the AppHost can reach it.</summary>
    public required string TargetHost { get; init; }

    /// <summary>Port the tunnel should forward to.</summary>
    public required int TargetPort { get; init; }
}

/// <summary>Builds the provider that will carry this tunnel's traffic.</summary>
public delegate ValueTask<ITunnelProvider> TunnelProviderFactory(
    TunnelProviderContext context,
    CancellationToken cancellationToken
);

/// <summary>
/// A tunnel run inside the AppHost process, on any <see cref="ITunnelProvider"/>.
/// <para>
/// The provider yields connections that arrived from somewhere other than a local socket; this
/// resource pumps each of them into the target's endpoint. Nothing about the target changes — it
/// keeps listening on localhost and never learns it is reachable from anywhere else.
/// </para>
/// </summary>
public sealed class InProcessTunnelResource(string name, string kind, TunnelProviderFactory factory)
    : TunnelResource(name)
{
    bool attached;

    /// <summary>Short name for the provider, shown as the resource type in the dashboard.</summary>
    public string Kind { get; } = kind;

    internal TunnelProviderFactory Factory { get; } = factory;

    /// <inheritdoc />
    protected internal override void AttachTarget(
        IDistributedApplicationBuilder builder,
        IResourceWithEndpoints target,
        string? endpointName
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);

        if (this.attached)
            throw new InvalidOperationException(
                $"Tunnel '{this.Name}' already publishes an endpoint. Add a second tunnel rather than retargeting this one."
            );

        this.attached = true;

        // Endpoint allocation, not readiness. The port is reserved before the target process is
        // launched, so the tunnel can be open — and its URL already handed to whoever referenced
        // it — by the time the target answers. A resource is allowed to need its own public URL.
        builder.Eventing.Subscribe<ResourceEndpointsAllocatedEvent>(
            target,
            (@event, cancellationToken) =>
            {
                var endpoint = TunnelTarget.Resolve(target, endpointName);
                this.TargetEndpoint = endpoint;

                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();
                var runner = new InProcessTunnelRunner(this, endpoint.Host, endpoint.Port, @event.Services);

                // Not awaited: opening a tunnel takes seconds and startup is not the place to spend
                // them. Anything that needs the address awaits the connection string instead.
                _ = Task.Run(() => runner.RunAsync(lifetime.ApplicationStopping), CancellationToken.None);

                return Task.CompletedTask;
            }
        );
    }
}
