using System.IO.Pipelines;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Shiny.Net.HttpServer.Transports;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// Joins a connection that arrived through a tunnel to the endpoint it was meant for.
/// <para>
/// Deliberately byte-for-byte and protocol-blind. The tunnel providers carry whatever the caller
/// sent, and the target is an ordinary ASP.NET Core app that will parse it — putting an HTTP
/// implementation in between would only add a version to disagree about, and would break WebSockets
/// and gRPC streaming in the process.
/// </para>
/// </summary>
static class TunnelConnectionPump
{
    public static async Task PumpAsync(
        IConnection connection,
        string host,
        int port,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            // Routine while the target is still starting: the tunnel opens as soon as the port is
            // allocated, which is before anything is listening on it.
            logger.LogDebug(ex, "Tunnelled connection {Id} could not reach {Host}:{Port}", connection.ConnectionId, host, port);
            connection.Abort();
            await connection.DisposeAsync().ConfigureAwait(false);

            return;
        }

        client.NoDelay = true;

        try
        {
            var stream = client.GetStream();

            var toTarget = ToTargetAsync(connection, client, stream, cancellationToken);
            var toCaller = ToCallerAsync(connection, stream, cancellationToken);

            await Task.WhenAll(toTarget, toCaller).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Either end hanging up mid-exchange is normal traffic, not a fault worth a stack trace.
            logger.LogDebug(ex, "Tunnelled connection {Id} ended", connection.ConnectionId);
            connection.Abort();
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    static async Task ToTargetAsync(IConnection connection, TcpClient client, NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await connection.Input.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await connection.Input.CompleteAsync().ConfigureAwait(false);

            // Half-close so the target sees the end of the request body it is waiting on, rather
            // than a connection that simply stops talking.
            try
            {
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }
        }
    }

    static async Task ToCallerAsync(IConnection connection, NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await stream.CopyToAsync(connection.Output, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await connection.Output.CompleteAsync().ConfigureAwait(false);
        }
    }
}
