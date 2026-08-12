using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using Shiny.Net.HttpServer.AzureRelay;
using Shiny.Net.HttpServer.Tunneling;

namespace Aspire.Hosting;

/// <summary>
/// Publishing Aspire endpoints through Azure Relay Hybrid Connections.
/// </summary>
public static class AzureRelayTunnelExtensions
{
    /// <summary>
    /// Adds a tunnel that terminates at an Azure Relay hybrid connection.
    /// <para>
    /// The address is stable and yours, which is what separates this from a quick tunnel: a webhook
    /// registered against it survives restarts, network changes and reconnects, so it suits the
    /// integration you configure once at a provider that will not let you change the URL casually.
    /// </para>
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the tunnel.</param>
    /// <param name="connectionString">
    /// A relay connection string — namespace, key name and key, and optionally the
    /// <c>EntityPath</c> naming the hybrid connection.
    /// </param>
    /// <param name="hybridConnectionName">
    /// The hybrid connection to listen on, when the connection string does not name one.
    /// </param>
    /// <param name="configure">Anything else on the options.</param>
    public static IResourceBuilder<InProcessTunnelResource> AddAzureRelayTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string connectionString,
        string? hybridConnectionName = null,
        Action<AzureRelayOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.AddAzureRelayTunnelCore(
            name,
            _ => ValueTask.FromResult<string?>(connectionString),
            hybridConnectionName,
            configure
        );
    }

    /// <summary>
    /// Adds an Azure Relay tunnel whose connection string comes from a parameter, so the key stays
    /// out of the app model's source.
    /// </summary>
    public static IResourceBuilder<InProcessTunnelResource> AddAzureRelayTunnel(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource> connectionString,
        string? hybridConnectionName = null,
        Action<AzureRelayOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionString);

        return builder.AddAzureRelayTunnelCore(
            name,
            connectionString.Resource.GetValueAsync,
            hybridConnectionName,
            configure
        );
    }

    /// <summary>
    /// Publishes this resource through an Azure Relay hybrid connection, creating the tunnel
    /// resource in the same line.
    /// </summary>
    /// <param name="target">The resource to publish.</param>
    /// <param name="connectionString">The relay connection string.</param>
    /// <param name="hybridConnectionName">The hybrid connection to listen on.</param>
    /// <param name="name">Resource name for the tunnel. Defaults to <c>{target}-tunnel</c>.</param>
    /// <param name="endpointName">Endpoint to publish. Defaults to <c>http</c>.</param>
    /// <param name="configure">Anything else on the options.</param>
    public static IResourceBuilder<TTarget> WithAzureRelayTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        string connectionString,
        string? hybridConnectionName = null,
        string? name = null,
        string? endpointName = null,
        Action<AzureRelayOptions>? configure = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);

        var tunnel = target.ApplicationBuilder.AddAzureRelayTunnel(
            name ?? $"{target.Resource.Name}-tunnel",
            connectionString,
            hybridConnectionName,
            configure
        );

        return target.WithTunnel(tunnel, endpointName);
    }

    /// <summary>
    /// Publishes this resource through an Azure Relay hybrid connection, taking the connection
    /// string from a parameter.
    /// </summary>
    public static IResourceBuilder<TTarget> WithAzureRelayTunnel<TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<ParameterResource> connectionString,
        string? hybridConnectionName = null,
        string? name = null,
        string? endpointName = null,
        Action<AzureRelayOptions>? configure = null
    )
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(target);

        var tunnel = target.ApplicationBuilder.AddAzureRelayTunnel(
            name ?? $"{target.Resource.Name}-tunnel",
            connectionString,
            hybridConnectionName,
            configure
        );

        return target.WithTunnel(tunnel, endpointName);
    }

    static IResourceBuilder<InProcessTunnelResource> AddAzureRelayTunnelCore(
        this IDistributedApplicationBuilder builder,
        string name,
        Func<CancellationToken, ValueTask<string?>> connectionString,
        string? hybridConnectionName,
        Action<AzureRelayOptions>? configure
    ) =>
        builder.AddTunnel(
            name,
            "azure-relay",
            async (context, cancellationToken) =>
            {
                var options = new AzureRelayOptions
                {
                    ConnectionString = await connectionString(cancellationToken).ConfigureAwait(false),
                    HybridConnectionName = hybridConnectionName
                };

                configure?.Invoke(options);

                return new AzureRelayTunnelProvider(
                    options,
                    context.LoggerFactory.CreateLogger<AzureRelayTunnelProvider>()
                );
            }
        );
}
