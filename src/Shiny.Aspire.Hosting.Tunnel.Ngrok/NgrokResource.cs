using System.Text.RegularExpressions;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// An ngrok agent, publishing a local endpoint at an ngrok address.
/// </summary>
public sealed partial class NgrokResource(string name) : ContainerTunnelResource(name, ForwardingUrl())
{
    /// <summary>The account's auth token. ngrok refuses to start without one.</summary>
    public ParameterResource? AuthToken { get; internal set; }

    /// <summary>A reserved domain to answer on, for accounts that have one.</summary>
    public string? Domain { get; internal set; }

    // The agent logs a JSON line carrying url=https://…; the free tiers land on ngrok-free.app or
    // ngrok.app, paid and reserved domains on ngrok.io and ngrok.dev. Anchored on the ngrok host
    // suffixes so the agent's own dashboard and update URLs cannot be mistaken for the tunnel.
    [GeneratedRegex(@"https://[a-z0-9-]+(?:\.[a-z0-9-]+)*\.ngrok(?:-free)?\.(?:app|dev|io)", RegexOptions.IgnoreCase)]
    private static partial Regex ForwardingUrl();
}
