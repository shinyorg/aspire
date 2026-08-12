using Shiny.Net.HttpServer.Tunneling;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// The public end of a Shiny tunnel, running inside the AppHost.
/// <para>
/// Two listeners: a control port where tunnel clients register, and a public port where traffic
/// arrives to be routed by Host header. It is the same relay Shiny.Net.HttpServer dials out to, so
/// a server embedded in a MAUI app on a phone can register into a development environment and be
/// reachable from it — no cloud account, no inbound connectivity to the phone.
/// </para>
/// </summary>
public sealed class ShinyRelayResource(string name, RelayServerOptions options)
    : Resource(name), IResourceWithConnectionString, IResourceWithWaitSupport
{
    readonly TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The relay's own options, as Shiny.Net.HttpServer defines them.</summary>
    public RelayServerOptions Options { get; } = options;

    /// <summary>
    /// Host name clients are told to dial for the control port. Loopback by default, which is right
    /// for tunnel clients in the same app model and wrong for a phone — set it to the machine's LAN
    /// address when something off-box has to register.
    /// </summary>
    public string ClientHost { get; set; } = "localhost";

    /// <summary>The token clients must present, when it comes from a parameter.</summary>
    public ParameterResource? TokenParameter { get; internal set; }

    /// <summary>Where relayed traffic arrives from the outside.</summary>
    public string PublicBaseUrl =>
        this.Options.IncludePortInPublicUrl
            ? $"{this.Options.PublicScheme}://{this.Options.Domain}:{this.Options.PublicPort}"
            : $"{this.Options.PublicScheme}://{this.Options.Domain}";

    /// <summary>
    /// What a tunnel client needs to register: <c>Host</c>, <c>Port</c>, <c>Token</c> and
    /// <c>UseTls</c>, matching the properties of <see cref="RelayTunnelOptions"/>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression
    {
        get
        {
            var head = $"Host={this.ClientHost};Port={this.Options.ControlPort.ToString()};UseTls={(this.Options.ControlHttps is not null).ToString().ToLowerInvariant()};Token=";

            return this.TokenParameter is null
                ? ReferenceExpression.Create($"{head}{this.Options.Token ?? string.Empty}")
                : ReferenceExpression.Create($"{head}{this.TokenParameter}");
        }
    }

    /// <summary>Completes once both listeners are bound, so a client knows when it may dial.</summary>
    public Task WaitForListeningAsync(CancellationToken cancellationToken = default) =>
        this.listening.Task.WaitAsync(cancellationToken);

    internal void MarkListening() => this.listening.TrySetResult();

    internal void MarkFailed(Exception exception) => this.listening.TrySetException(exception);
}
