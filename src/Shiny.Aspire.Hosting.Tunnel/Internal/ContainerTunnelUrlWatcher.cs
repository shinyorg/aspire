using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// Reads a container agent's assigned address back out of its log stream.
/// <para>
/// Not a preference. cloudflared's quick tunnels and ngrok's free tunnels pick a hostname per run
/// and announce it on stdout; there is no other channel that reports it and no way to know it in
/// advance.
/// </para>
/// </summary>
static class ContainerTunnelUrlWatcher
{
    public static void Watch(IDistributedApplicationBuilder builder, ContainerTunnelResource resource)
    {
        // Before the container starts, so the announcement cannot be missed between the agent
        // printing it and this subscribing.
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(
            resource,
            (@event, cancellationToken) =>
            {
                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();

                _ = Task.Run(
                    () => WatchAsync(resource, @event.Services, lifetime.ApplicationStopping),
                    CancellationToken.None
                );

                return Task.CompletedTask;
            }
        );
    }

    static async Task WatchAsync(ContainerTunnelResource resource, IServiceProvider services, CancellationToken cancellationToken)
    {
        var loggers = services.GetRequiredService<ResourceLoggerService>();
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = loggers.GetLogger(resource);

        try
        {
            await foreach (var batch in loggers.WatchAsync(resource).WithCancellation(cancellationToken))
            {
                foreach (var line in batch)
                {
                    var match = resource.UrlPattern.Match(line.Content);

                    if (!match.Success)
                        continue;

                    var url = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '>', '"', '\'');

                    if (url == resource.PublicUrl)
                        continue;

                    resource.SetPublicUrl(url);
                    logger.LogInformation("Tunnel address is {Url}", url);

                    await notifications
                        .PublishUpdateAsync(
                            resource,
                            snapshot => snapshot with
                            {
                                Urls =
                                [
                                    .. snapshot.Urls.Where(x => x.Name != "public"),
                                    new UrlSnapshot("public", url, IsInternal: false)
                                ],
                                Properties =
                                [
                                    .. snapshot.Properties.Where(x => x.Name != TunnelStatus.PublicUrlProperty),
                                    new ResourcePropertySnapshot(TunnelStatus.PublicUrlProperty, url)
                                ]
                            }
                        )
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
