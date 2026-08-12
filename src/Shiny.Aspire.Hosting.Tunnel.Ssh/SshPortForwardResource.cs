using Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;
using Shiny.Net.HttpServer.Ssh;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A service on the far side of an SSH host, reachable from this app model as if it were local.
/// <para>
/// The opposite direction to <see cref="SshTunnelResource"/>: instead of publishing something local
/// to the internet, this opens a local port that SSH carries to <see cref="RemoteHost"/> — the
/// staging database behind a bastion, an on-premises API, anything you can already reach over SSH
/// but your app cannot reach directly.
/// </para>
/// </summary>
public sealed class SshPortForwardResource : Resource, IResourceWithConnectionString, IResourceWithWaitSupport
{
    readonly TaskCompletionSource bound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly SshForwardHostProvider hostProvider;
    readonly SshForwardPortProvider portProvider;

    /// <param name="name">Resource name for the forward.</param>
    /// <param name="options">How to reach the SSH host.</param>
    /// <param name="remoteHost">The service to reach, as named from the SSH host.</param>
    /// <param name="remotePort">The port to reach on <paramref name="remoteHost"/>.</param>
    public SshPortForwardResource(string name, SshTunnelOptions options, string remoteHost, int remotePort)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteHost);

        this.Options = options;
        this.RemoteHost = remoteHost;
        this.RemotePort = remotePort;
        this.hostProvider = new SshForwardHostProvider(this);
        this.portProvider = new SshForwardPortProvider(this);
    }

    /// <summary>How to reach the SSH host. Only the connection settings apply; nothing is forwarded back.</summary>
    public SshTunnelOptions Options { get; }

    /// <summary>The service to reach, as named from the SSH host.</summary>
    public string RemoteHost { get; }

    /// <summary>The port to reach on <see cref="RemoteHost"/>.</summary>
    public int RemotePort { get; }

    /// <summary>
    /// Interface the local end binds. Loopback keeps it to this machine; <c>0.0.0.0</c> is required
    /// for container resources, which reach the host across a bridge rather than over loopback.
    /// </summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>Local port to bind. Zero takes an ephemeral one and keeps it across reconnects.</summary>
    public int LocalPort { get; set; }

    /// <summary>The port actually bound, once the forward is up.</summary>
    public int BoundPort { get; internal set; }

    /// <summary>The local host, as the resource asking for it can reach it.</summary>
    public ReferenceExpression Host => ReferenceExpression.Create($"{this.hostProvider}");

    /// <summary>The local port.</summary>
    public ReferenceExpression Port => ReferenceExpression.Create($"{this.portProvider}");

    /// <summary>
    /// What referencing resources get. Defaults to <c>host:port</c>; set it to whatever the client
    /// on the other end expects — a Postgres connection string, an amqp URI, an http base address.
    /// </summary>
    public Func<SshPortForwardResource, ReferenceExpression>? ConnectionStringTemplate { get; set; }

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression =>
        this.ConnectionStringTemplate?.Invoke(this) ?? ReferenceExpression.Create($"{this.Host}:{this.Port}");

    /// <summary>Completes once the local port is listening.</summary>
    public Task WaitForBoundAsync(CancellationToken cancellationToken = default) =>
        this.bound.Task.WaitAsync(cancellationToken);

    internal void MarkBound() => this.bound.TrySetResult();

    internal void MarkFailed(Exception exception) => this.bound.TrySetException(exception);
}
