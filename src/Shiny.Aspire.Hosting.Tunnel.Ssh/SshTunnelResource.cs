using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shiny.Aspire.Hosting.Tunnel.Internal;
using Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;
using Shiny.Net.HttpServer.Ssh;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A public address for a local endpoint, by way of an SSH server.
/// <para>
/// The AppHost opens an ordinary outbound SSH connection and asks the server to forward a port back
/// down it — <c>ssh -R</c>, in library form — pointed straight at the port Aspire allocated for the
/// target. Nothing has to connect in, so this works from a laptop behind NAT, a CI runner, or a
/// corporate network that allows nothing but outbound 443.
/// </para>
/// </summary>
public sealed class SshTunnelResource(string name, SshTunnelOptions options) : TunnelResource(name)
{
    bool attached;

    /// <summary>The tunnel's options, as Shiny.Net.HttpServer.Ssh defines them.</summary>
    public SshTunnelOptions Options { get; } = options;

    /// <summary>The port the SSH server bound, once forwarding is up. Useful when it allocated one.</summary>
    public int RemotePort { get; internal set; }

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

        builder.Eventing.Subscribe<ResourceEndpointsAllocatedEvent>(
            target,
            (@event, cancellationToken) =>
            {
                var endpoint = TunnelTarget.Resolve(target, endpointName);
                this.TargetEndpoint = endpoint;

                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();
                var runner = new SshTunnelRunner(this, endpoint.Host, endpoint.Port, @event.Services);

                _ = Task.Run(() => runner.RunAsync(lifetime.ApplicationStopping), CancellationToken.None);

                return Task.CompletedTask;
            }
        );
    }
}
