using Shiny.Aspire.Hosting.Tunnel.Internal;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A public address for a resource that is only listening locally, carried by the AppHost itself.
/// <para>
/// The resource carries its public URL as its connection string, so everything Aspire already knows
/// how to do with a connection string works here: <c>WithReference(tunnel)</c> injects it,
/// <c>WithEnvironment("X", tunnel)</c> names it, and both block until the tunnel is actually open.
/// </para>
/// </summary>
public abstract class TunnelResource : Resource, ITunnelResource, IResourceWithConnectionString, IResourceWithWaitSupport
{
    readonly TunnelUrlSource url;

    /// <param name="name">Resource name for the tunnel.</param>
    protected TunnelResource(string name) : base(name) => this.url = new TunnelUrlSource(this);

    /// <inheritdoc />
    public EndpointReference? TargetEndpoint { get; internal set; }

    /// <inheritdoc />
    public string? PublicUrl => this.url.Current;

    /// <inheritdoc />
    public ReferenceExpression PublicUrlExpression => this.url.Expression;

    /// <summary>The public URL, as the connection string.</summary>
    public ReferenceExpression ConnectionStringExpression => this.url.Expression;

    /// <inheritdoc />
    public Task<string> GetPublicUrlAsync(CancellationToken cancellationToken = default) =>
        this.url.WaitAsync(cancellationToken);

    /// <summary>
    /// Attaches the endpoint this tunnel publishes. Called by <c>WithTunnel</c>, once; an
    /// implementation opens itself from here, by subscribing to the target's endpoint allocation.
    /// </summary>
    protected internal abstract void AttachTarget(
        IDistributedApplicationBuilder builder,
        IResourceWithEndpoints target,
        string? endpointName
    );

    internal void SetPublicUrl(string publicUrl) => this.url.Set(publicUrl);

    internal void ClearPublicUrl() => this.url.Clear();

    internal void SetFailed(Exception exception) => this.url.Fail(exception);
}
