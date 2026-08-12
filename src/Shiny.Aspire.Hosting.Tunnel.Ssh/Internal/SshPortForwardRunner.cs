using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;

/// <summary>Holds a local forward open for the life of the AppHost, rebuilding it after a drop.</summary>
sealed class SshPortForwardRunner(SshPortForwardResource resource, IServiceProvider services)
{
    static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(2);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var options = resource.Options;
        var credentials = new SshCredentials(options, logger);
        var remote = $"{resource.RemoteHost}:{resource.RemotePort}";
        var delay = options.ReconnectDelay;

        await Publish(notifications, KnownResourceStates.Starting, remote).ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SshClient? client = null;

                try
                {
                    client = new SshClient(credentials.CreateConnectionInfo())
                    {
                        KeepAliveInterval = options.KeepAliveInterval
                    };

                    client.HostKeyReceived += credentials.OnHostKeyReceived;

                    await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    // The port is reused verbatim on every reconnect. Anything that was handed the
                    // first one keeps working; taking a fresh ephemeral port would quietly strand it.
                    var port = resource.BoundPort != 0 ? resource.BoundPort : resource.LocalPort;

                    var forward = new ForwardedPortLocal(
                        resource.BindAddress,
                        (uint)port,
                        resource.RemoteHost,
                        (uint)resource.RemotePort
                    );

                    forward.Exception += (_, e) => logger.LogWarning(e.Exception, "A forwarded connection failed");
                    client.AddForwardedPort(forward);

                    await Task.Run(forward.Start, cancellationToken).ConfigureAwait(false);

                    resource.BoundPort = (int)forward.BoundPort;
                    resource.MarkBound();

                    logger.LogInformation(
                        "Forwarding {Bind}:{Port} to {Remote} through {Host}",
                        resource.BindAddress,
                        resource.BoundPort,
                        remote,
                        options.Host
                    );

                    await Publish(notifications, KnownResourceStates.Running, remote).ConfigureAwait(false);

                    delay = options.ReconnectDelay;

                    await MonitorAsync(client, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    logger.LogWarning("The SSH connection carrying {Remote} dropped", remote);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not forward {Remote}", remote);

                    if (!options.AutoReconnect)
                    {
                        resource.MarkFailed(ex);
                        await Publish(notifications, KnownResourceStates.FailedToStart, remote).ConfigureAwait(false);

                        return;
                    }
                }
                finally
                {
                    try
                    {
                        client?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "The SSH connection did not close cleanly");
                    }
                }

                if (!options.AutoReconnect)
                    break;

                await Publish(notifications, KnownResourceStates.Starting, remote).ConfigureAwait(false);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delay = delay < options.MaxReconnectDelay
                    ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxReconnectDelay.Ticks))
                    : options.MaxReconnectDelay;
            }
        }
        finally
        {
            await Publish(notifications, KnownResourceStates.Exited, remote).ConfigureAwait(false);
        }
    }

    Task Publish(ResourceNotificationService notifications, string state, string remote) =>
        notifications.PublishUpdateAsync(
            resource,
            snapshot => snapshot with
            {
                State = state,
                Properties =
                [
                    .. snapshot.Properties.Where(x => !x.Name.StartsWith("forward.", StringComparison.Ordinal)),
                    new ResourcePropertySnapshot("forward.remote", remote),
                    new ResourcePropertySnapshot("forward.local", $"{resource.BindAddress}:{resource.BoundPort}"),
                    new ResourcePropertySnapshot("forward.via", resource.Options.Host)
                ]
            }
        );

    static async Task MonitorAsync(SshClient client, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HealthPollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!client.IsConnected)
                return;
        }
    }
}
