using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shiny.Aspire.Hosting.Tunnel.Internal;
using Shiny.Net.HttpServer.Tunneling;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

/// <summary>
/// The whole mechanism, end to end and in one process: a relay, a tunnel client dialling it, the
/// pump joining what arrives to a local listener, and a real HTTP request making the round trip.
/// </summary>
public class TunnelConnectionPumpTests
{
    [Fact]
    public async Task ARequestToTheRelayReachesTheTargetAndComesBack()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = cancellation.Token;

        var controlPort = FreePort();
        var publicPort = FreePort();

        await using var relay = new RelayServer(
            new RelayServerOptions
            {
                Address = IPAddress.Loopback,
                ControlPort = controlPort,
                PublicPort = publicPort,
                Domain = "localhost",
                Token = "test-token"
            }
        );

        await relay.StartAsync(token);

        // The thing being published: an ordinary socket answering ordinary HTTP.
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
        var serving = ServeOneAsync(target, "hello from behind the tunnel", token);

        await using var provider = new RelayTunnelProvider(
            new RelayTunnelOptions
            {
                Host = "localhost",
                Port = controlPort,
                Token = "test-token",
                Subdomain = "api",
                UseTls = false,
                ReconnectDelay = null
            }
        );

        await provider.BindAsync(token);

        provider.PublicUrl.ShouldNotBeNull();

        var pumping = PumpAsync(provider, targetPort, token);

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{publicPort}/");

            // The relay routes on the Host header, which is what a public caller would really send.
            request.Headers.Host = new Uri(provider.PublicUrl!).Host;

            using var response = await client.SendAsync(request, token);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(token)).ShouldBe("hello from behind the tunnel");
        }
        finally
        {
            await cancellation.CancelAsync();
            target.Stop();

            await Task.WhenAny(Task.WhenAll(pumping, serving), Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
    }

    [Fact]
    public async Task AConnectionToATargetThatIsNotListeningIsDroppedRatherThanThrown()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Nothing is listening here: the case where the tunnel is open before the target has
        // started, which is the normal state of affairs for the first second or two.
        var connection = new Shiny.Net.HttpServer.Transports.DuplexPipeConnection("test");

        await TunnelConnectionPump.PumpAsync(
            connection,
            "127.0.0.1",
            FreePort(),
            NullLogger.Instance,
            cancellation.Token
        );
    }

    static async Task PumpAsync(ITunnelProvider provider, int targetPort, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await provider.AcceptAsync(cancellationToken);

                if (connection is null)
                    break;

                _ = TunnelConnectionPump.PumpAsync(
                    connection,
                    "127.0.0.1",
                    targetPort,
                    NullLogger.Instance,
                    cancellationToken
                );
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    static async Task ServeOneAsync(TcpListener listener, string body, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer, cancellationToken);

            // One read is enough: the request head arrives in a single segment here, and the test
            // only cares that something came through.
            if (read == 0)
                return;

            var response =
                "HTTP/1.1 200 OK\r\n"
                + "Content-Type: text/plain\r\n"
                + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                + "Connection: close\r\n"
                + "\r\n"
                + body;

            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
