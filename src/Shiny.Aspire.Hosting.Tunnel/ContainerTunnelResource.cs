using System.Text.RegularExpressions;
using Shiny.Aspire.Hosting.Tunnel.Internal;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A tunnel run by an agent in a container beside the app, rather than by the AppHost itself.
/// <para>
/// The agent is an ordinary container resource — Aspire starts it, stops it and shows its logs like
/// any other. What it is not ordinary about is its address: the agent chooses one at runtime and
/// announces it in its own output, so the URL is read back out of the log stream and published from
/// there.
/// </para>
/// </summary>
public abstract class ContainerTunnelResource : ContainerResource, ITunnelResource, IResourceWithConnectionString
{
    readonly TunnelUrlSource url;

    /// <param name="name">Resource name for the agent.</param>
    /// <param name="urlPattern">Picks the assigned address out of the agent's output.</param>
    protected ContainerTunnelResource(string name, Regex urlPattern) : base(name)
    {
        ArgumentNullException.ThrowIfNull(urlPattern);

        this.url = new TunnelUrlSource(this);
        this.UrlPattern = urlPattern;
    }

    /// <summary>Picks the assigned address out of the agent's output.</summary>
    public Regex UrlPattern { get; }

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

    internal void SetPublicUrl(string publicUrl) => this.url.Set(publicUrl);
}
