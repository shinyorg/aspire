using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Shiny.Aspire.Hosting.Tunnel.Internal;

namespace Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;

/// <summary>
/// Holds an SSH remote forward open for the life of the AppHost, rebuilding it after a drop.
/// </summary>
sealed class SshTunnelRunner(
    SshTunnelResource resource,
    string targetHost,
    int targetPort,
    IServiceProvider services
)
{
    static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(2);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var options = resource.Options;
        var credentials = new SshCredentials(options, logger);
        var target = $"{targetHost}:{targetPort}";
        var delay = options.ReconnectDelay;

        await TunnelStatus
            .PublishAsync(notifications, resource, KnownResourceStates.Starting, target: target)
            .ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SshClient? client = null;
                ShellStream? session = null;

                try
                {
                    (client, session) = await this.ConnectAsync(credentials, logger, cancellationToken)
                        .ConfigureAwait(false);

                    if (resource.PublicUrl is { Length: > 0 } url)
                        logger.LogInformation("SSH tunnel open at {Url}, forwarding to {Target}", url, target);

                    await TunnelStatus
                        .PublishAsync(notifications, resource, KnownResourceStates.Running, resource.PublicUrl, target)
                        .ConfigureAwait(false);

                    delay = options.ReconnectDelay;

                    await MonitorAsync(client, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    logger.LogWarning("The SSH tunnel dropped");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The SSH tunnel could not be opened");

                    if (!options.AutoReconnect)
                    {
                        resource.SetFailed(ex);

                        await TunnelStatus
                            .PublishAsync(notifications, resource, KnownResourceStates.FailedToStart, target: target)
                            .ConfigureAwait(false);

                        return;
                    }
                }
                finally
                {
                    Disconnect(client, session, logger);
                }

                if (!options.AutoReconnect)
                    break;

                // The address is dead the moment the connection is: most hosted endpoints assign a
                // new one on reconnect, so showing the old one would be showing a broken link.
                resource.ClearPublicUrl();

                await TunnelStatus
                    .PublishAsync(notifications, resource, KnownResourceStates.Starting, target: target)
                    .ConfigureAwait(false);

                logger.LogInformation("Reconnecting the SSH tunnel in {Delay}", delay);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Back off rather than hammering a server that is down, or an auth failure that
                // will fail identically every time.
                delay = delay < options.MaxReconnectDelay
                    ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxReconnectDelay.Ticks))
                    : options.MaxReconnectDelay;
            }
        }
        finally
        {
            resource.ClearPublicUrl();

            await TunnelStatus
                .PublishAsync(notifications, resource, KnownResourceStates.Exited, target: target)
                .ConfigureAwait(false);
        }
    }

    async Task<(SshClient Client, ShellStream? Session)> ConnectAsync(
        SshCredentials credentials,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var options = resource.Options;
        var client = new SshClient(credentials.CreateConnectionInfo()) { KeepAliveInterval = options.KeepAliveInterval };

        client.HostKeyReceived += credentials.OnHostKeyReceived;

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            var forward = new ForwardedPortRemote(
                options.RemoteBindAddress,
                (uint)options.RemotePort,
                targetHost,
                (uint)targetPort
            );

            forward.Exception += (_, e) => logger.LogWarning(e.Exception, "A forwarded connection failed");
            client.AddForwardedPort(forward);

            // Start() blocks until the server answers the forwarding request, and answers by
            // closing the channel if it refuses — which is how a missing GatewayPorts or a
            // permitopen restriction shows up.
            await Task.Run(forward.Start, cancellationToken).ConfigureAwait(false);

            resource.RemotePort = (int)forward.BoundPort;

            ShellStream? session = null;
            string? url;

            if (options.PublicUrl is { Length: > 0 } configured)
            {
                url = configured;
            }
            else if (options.CaptureUrlFromSession)
            {
                (url, session) = await this.CaptureUrlAsync(client, logger, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Best effort when nothing better is known: right for a direct forward, wrong the
                // moment a reverse proxy terminates TLS in front of it.
                url = string.Create(CultureInfo.InvariantCulture, $"http://{options.Host}:{resource.RemotePort}");
            }

            if (url is { Length: > 0 })
                resource.SetPublicUrl(url);
            else
                logger.LogWarning(
                    "The forward to {RemoteBind}:{RemotePort} is up, but no public address is known. "
                        + "Set PublicUrl if you know where this endpoint answers.",
                    options.RemoteBindAddress,
                    resource.RemotePort
                );

            return (client, session);
        }
        catch
        {
            client.HostKeyReceived -= credentials.OnHostKeyReceived;
            client.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Reads the URL a hosted endpoint assigns. sish, pinggy and Serveo all print it on the session
    /// channel once forwarding is up, and there is no other way to learn it — the address is the
    /// server's to choose. The channel stays open afterwards, because some providers tear the
    /// forward down when it closes.
    /// </summary>
    async Task<(string? Url, ShellStream? Session)> CaptureUrlAsync(
        SshClient client,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var options = resource.Options;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.UrlCaptureTimeout);

        ShellStream? session = null;

        var capture = Task.Run(
            async () =>
            {
                // Opening the session channel is a blocking call inside SSH.NET that waits for the
                // server to confirm the shell request. Kept off the caller's thread so a server
                // that never confirms one cannot hold the connect path open past the timeout.
                var shell = client.CreateShellStreamNoTerminal(bufferSize: 4096);
                session = shell;

                var buffer = new byte[1024];
                var text = new StringBuilder();

                while (!timeout.Token.IsCancellationRequested)
                {
                    var read = await shell.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);

                    if (read == 0)
                        break;

                    text.Append(Encoding.UTF8.GetString(buffer, 0, read));

                    var match = options.UrlPattern.Match(text.ToString());

                    if (match.Success)
                        // A URL in prose brings the punctuation around it along: providers print
                        // theirs inside brackets, in quotes, or at the end of a sentence.
                        return match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '>', '"', '\'');
                }

                return null;
            },
            CancellationToken.None
        );

        // Nobody awaits an abandoned capture, and a blocking SSH call that throws after that would
        // surface as an unobserved exception rather than the warning it deserves.
        _ = capture.ContinueWith(
            static t => t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        try
        {
            var url = await capture.WaitAsync(options.UrlCaptureTimeout, cancellationToken).ConfigureAwait(false);

            if (url is { Length: > 0 })
                logger.LogInformation("The SSH endpoint assigned {Url}", url);

            return (url, session);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(
                "No URL appeared within {Timeout}. Some hosted endpoints — localhost.run among them — "
                    + "never confirm the session request that carries it, in which case the address "
                    + "cannot be read at all and PublicUrl has to be set explicitly.",
                options.UrlCaptureTimeout
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the assigned URL from the session");
        }

        return (null, session);
    }

    static async Task MonitorAsync(SshClient client, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HealthPollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!client.IsConnected)
                return;
        }
    }

    static void Disconnect(SshClient? client, ShellStream? session, ILogger logger)
    {
        try
        {
            session?.Dispose();
            client?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The SSH tunnel did not close cleanly");
        }
    }
}
