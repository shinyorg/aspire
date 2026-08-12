using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shiny.Net.HttpServer.Tunneling;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// Opens an in-process tunnel and keeps it pumping until the AppHost stops.
/// </summary>
sealed class InProcessTunnelRunner(
    InProcessTunnelResource resource,
    string targetHost,
    int targetPort,
    IServiceProvider services
)
{
    // A provider that reconnects gets a new address each time on most free hosts. Polling for it is
    // provider-agnostic, where an event would only work for the two providers that raise one.
    static readonly TimeSpan UrlPollInterval = TimeSpan.FromSeconds(2);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var target = $"{targetHost}:{targetPort}";

        await TunnelStatus
            .PublishAsync(notifications, resource, KnownResourceStates.Starting, target: target)
            .ConfigureAwait(false);

        ITunnelProvider provider;

        try
        {
            var context = new TunnelProviderContext
            {
                Services = services,
                Resource = resource,
                LoggerFactory = new ResourceLoggerFactory(logger),
                TargetHost = targetHost,
                TargetPort = targetPort
            };

            provider = await resource.Factory(context, cancellationToken).ConfigureAwait(false);
            await provider.BindAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TunnelStatus.PublishAsync(notifications, resource, KnownResourceStates.Exited).ConfigureAwait(false);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The {Kind} tunnel could not be opened", resource.Kind);
            resource.SetFailed(ex);

            await TunnelStatus
                .PublishAsync(notifications, resource, KnownResourceStates.FailedToStart, target: target)
                .ConfigureAwait(false);

            return;
        }

        try
        {
            if (provider.PublicUrl is { Length: > 0 } url)
            {
                resource.SetPublicUrl(url);
                logger.LogInformation("{Kind} tunnel open at {Url}, forwarding to {Target}", resource.Kind, url, target);
            }
            else
            {
                logger.LogWarning(
                    "The {Kind} tunnel is open but reported no public address, so nothing can reference it",
                    resource.Kind
                );
            }

            await TunnelStatus
                .PublishAsync(notifications, resource, KnownResourceStates.Running, provider.PublicUrl, target)
                .ConfigureAwait(false);

            // Linked, not the caller's token: a provider that closes for good ends the accept loop
            // without anything being cancelled, and the watcher would otherwise keep polling — and
            // keep this method from tidying up — until the AppHost shut down.
            using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var watcher = this.WatchUrlAsync(provider, notifications, logger, target, running.Token);

            await this.AcceptAsync(provider, logger, cancellationToken).ConfigureAwait(false);

            await running.CancelAsync().ConfigureAwait(false);
            await watcher.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await provider.UnbindAsync(CancellationToken.None).ConfigureAwait(false);
                await provider.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "The {Kind} tunnel did not close cleanly", resource.Kind);
            }

            resource.ClearPublicUrl();

            await TunnelStatus
                .PublishAsync(notifications, resource, KnownResourceStates.Exited, target: target)
                .ConfigureAwait(false);
        }
    }

    async Task AcceptAsync(ITunnelProvider provider, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await provider.AcceptAsync(cancellationToken).ConfigureAwait(false);

                if (connection is null)
                    break;

                _ = Task.Run(
                    () => TunnelConnectionPump.PumpAsync(connection, targetHost, targetPort, logger, cancellationToken),
                    CancellationToken.None
                );
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The {Kind} tunnel stopped accepting connections", resource.Kind);
        }
    }

    async Task WatchUrlAsync(
        ITunnelProvider provider,
        ResourceNotificationService notifications,
        ILogger logger,
        string target,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(UrlPollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var current = provider.PublicUrl;

                if (current == resource.PublicUrl)
                    continue;

                if (current is { Length: > 0 })
                {
                    resource.SetPublicUrl(current);
                    logger.LogInformation("{Kind} tunnel address is now {Url}", resource.Kind, current);
                }
                else
                {
                    resource.ClearPublicUrl();
                    logger.LogWarning("The {Kind} tunnel dropped; its address is no longer valid", resource.Kind);
                }

                await TunnelStatus
                    .PublishAsync(
                        notifications,
                        resource,
                        current is { Length: > 0 } ? KnownResourceStates.Running : KnownResourceStates.Starting,
                        current,
                        target
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
