using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;

/// <summary>
/// The host a forwarded service answers on, which depends on who is asking.
/// <para>
/// The forward is bound by the AppHost, on the developer's machine. A project reaches it over
/// loopback; a container has its own loopback and has to come back across the bridge, so it is told
/// the container host name instead.
/// </para>
/// </summary>
sealed class SshForwardHostProvider(SshPortForwardResource resource) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression => $"{{{resource.Name}.host}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        await resource.WaitForBoundAsync(cancellationToken).ConfigureAwait(false);

        return "localhost";
    }

    public async ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default)
    {
        await resource.WaitForBoundAsync(cancellationToken).ConfigureAwait(false);

        return context.Caller is ContainerResource ? KnownHostNames.DockerDesktopHostBridge : "localhost";
    }
}

/// <summary>The port the forward bound, once it has one.</summary>
sealed class SshForwardPortProvider(SshPortForwardResource resource) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression => $"{{{resource.Name}.port}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        await resource.WaitForBoundAsync(cancellationToken).ConfigureAwait(false);

        return resource.BoundPort.ToString(CultureInfo.InvariantCulture);
    }
}
