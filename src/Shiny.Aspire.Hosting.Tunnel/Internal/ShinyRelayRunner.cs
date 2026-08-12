using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shiny.Net.HttpServer.Tunneling;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>Runs a <see cref="RelayServer"/> for the life of the AppHost.</summary>
sealed class ShinyRelayRunner(ShinyRelayResource resource, IServiceProvider services)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);

        await notifications
            .PublishUpdateAsync(resource, snapshot => snapshot with { State = KnownResourceStates.Starting })
            .ConfigureAwait(false);

        RelayServer server;

        try
        {
            if (resource.TokenParameter is not null)
                resource.Options.Token = await resource.TokenParameter
                    .GetValueAsync(cancellationToken)
                    .ConfigureAwait(false);

            server = new RelayServer(resource.Options, new ResourceLoggerFactory(logger));
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The relay could not start");
            resource.MarkFailed(ex);

            await notifications
                .PublishUpdateAsync(resource, snapshot => snapshot with { State = KnownResourceStates.FailedToStart })
                .ConfigureAwait(false);

            return;
        }

        try
        {
            resource.MarkListening();

            logger.LogInformation(
                "Relay listening — control {Control}, public {Public}",
                server.ControlUrl,
                resource.PublicBaseUrl
            );

            await notifications
                .PublishUpdateAsync(
                    resource,
                    snapshot => snapshot with
                    {
                        State = KnownResourceStates.Running,
                        Urls = [new UrlSnapshot("public", resource.PublicBaseUrl, IsInternal: false)],
                        Properties =
                        [
                            .. snapshot.Properties.Where(x => !x.Name.StartsWith("relay.", StringComparison.Ordinal)),
                            new ResourcePropertySnapshot("relay.controlPort", resource.Options.ControlPort),
                            new ResourcePropertySnapshot("relay.publicPort", server.PublicPort),
                            new ResourcePropertySnapshot("relay.domain", resource.Options.Domain)
                        ]
                    }
                )
                .ConfigureAwait(false);

            // Nothing to poll: the relay serves itself. This just holds the resource open so the
            // finally block runs on shutdown rather than on the next garbage collection.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await server.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "The relay did not stop cleanly");
            }

            await notifications
                .PublishUpdateAsync(resource, snapshot => snapshot with { State = KnownResourceStates.Exited, Urls = [] })
                .ConfigureAwait(false);
        }
    }
}
