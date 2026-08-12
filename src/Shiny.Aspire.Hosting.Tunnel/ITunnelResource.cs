namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Anything that gives a local endpoint a public address.
/// <para>
/// Implemented both by tunnels run inside the AppHost and by container agents run beside it, so
/// <c>WithTunnelUrl</c> and <c>WithReference</c> read the same either way — which of the two a
/// tunnel is should be a deployment detail, not a different API.
/// </para>
/// </summary>
public interface ITunnelResource : IResource
{
    /// <summary>The endpoint being published, once one is attached.</summary>
    EndpointReference? TargetEndpoint { get; }

    /// <summary>
    /// Where the world can reach the target, once the tunnel is up. Null before that, and null
    /// again while a dropped tunnel is reconnecting.
    /// </summary>
    string? PublicUrl { get; }

    /// <summary>The public URL as an expression, for environment variables and connection strings.</summary>
    ReferenceExpression PublicUrlExpression { get; }

    /// <summary>
    /// The first public URL this tunnel is assigned. Resolves once and keeps that value: an
    /// environment variable handed to a process cannot be revised after the process has started, so
    /// pretending otherwise would only hide the staleness.
    /// </summary>
    Task<string> GetPublicUrlAsync(CancellationToken cancellationToken = default);
}
