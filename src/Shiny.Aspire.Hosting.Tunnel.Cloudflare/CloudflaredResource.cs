using System.Text.RegularExpressions;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A <c>cloudflared</c> agent, publishing a local endpoint through Cloudflare's edge.
/// <para>
/// Two ways to run it. A quick tunnel needs no account and is handed a throwaway
/// <c>trycloudflare.com</c> address, read back out of the agent's output. A named tunnel runs
/// against a token from your own Cloudflare account, answers on a host name you already control,
/// and keeps that address across restarts.
/// </para>
/// </summary>
public sealed partial class CloudflaredResource(string name) : ContainerTunnelResource(name, QuickTunnelUrl())
{
    /// <summary>Credential for a named tunnel. Null runs a quick tunnel instead.</summary>
    public ParameterResource? Token { get; internal set; }

    // cloudflared prints the assigned address inside an ASCII box on stderr; nothing else in that
    // banner is a trycloudflare.com address, so the host name alone is a safe anchor.
    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex QuickTunnelUrl();
}
