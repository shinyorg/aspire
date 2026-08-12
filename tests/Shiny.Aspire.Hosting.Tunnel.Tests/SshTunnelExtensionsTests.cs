using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shiny.Net.HttpServer.Ssh;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class SshTunnelExtensionsTests
{
    [Fact]
    public void AddQuickTunnel_DefaultsToPinggy()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddQuickTunnel("public");

        tunnel.Resource.Options.Host.ShouldBe("a.pinggy.io");
        tunnel.Resource.Options.Port.ShouldBe(443);
        tunnel.Resource.Options.CaptureUrlFromSession.ShouldBeTrue();

        // pinggy wants a key but does not care which, and will not accept "none".
        tunnel.Resource.Options.UseEphemeralKey.ShouldBeTrue();
    }

    [Fact]
    public void AddQuickTunnel_PassesTheTokenAsTheUsername()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddQuickTunnel("public", QuickTunnelHost.Pinggy, subdomain: "my-token");

        tunnel.Resource.Options.Username.ShouldBe("my-token");
    }

    [Fact]
    public void AddQuickTunnel_ConfigureRunsAfterTheHostPreset()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddQuickTunnel("public", configure: o => o.AutoReconnect = false);

        tunnel.Resource.Options.AutoReconnect.ShouldBeFalse();
    }

    [Fact]
    public void AddQuickTunnel_OtherHostsCarryTheirOwnPresets()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddQuickTunnel("a", QuickTunnelHost.Sish).Resource.Options.Host.ShouldBe("tuns.sh");
        builder.AddQuickTunnel("b", QuickTunnelHost.Serveo).Resource.Options.Host.ShouldBe("serveo.net");
        builder.AddQuickTunnel("c", QuickTunnelHost.LocalhostRun).Resource.Options.Host.ShouldBe("localhost.run");
    }

    [Fact]
    public void WithQuickTunnel_AddsATunnelNamedAfterItsTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080).WithQuickTunnel();

        builder.Resources.OfType<SshTunnelResource>().ShouldHaveSingleItem().Name.ShouldBe("api-tunnel");
    }

    [Fact]
    public void WithSshTunnel_TakesTheHostAndTheRestFromTheCallback()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder
            .AddContainer("api", "my-api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithSshTunnel(
                "tunnel.example.com",
                o =>
                {
                    o.Username = "deploy";
                    o.RemotePort = 8080;
                    o.PublicUrl = "https://api.example.com";
                    o.AcceptAnyHostKey = true;
                }
            );

        var tunnel = builder.Resources.OfType<SshTunnelResource>().ShouldHaveSingleItem();

        tunnel.Options.Host.ShouldBe("tunnel.example.com");
        tunnel.Options.Username.ShouldBe("deploy");
        tunnel.Options.RemotePort.ShouldBe(8080);
        tunnel.Options.PublicUrl.ShouldBe("https://api.example.com");
    }

    [Fact]
    public void AddSshTunnel_MakesTheTunnelATunnelResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddSshTunnel("public", new SshTunnelOptions { Host = "example.com", Username = "me" });

        tunnel.Resource.ShouldBeAssignableTo<TunnelResource>();
        tunnel.Resource.ShouldBeAssignableTo<ITunnelResource>();
    }

    [Fact]
    public void AddSshTunnel_NullOptions_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentNullException>(() => builder.AddSshTunnel("public", null!));
    }
}
