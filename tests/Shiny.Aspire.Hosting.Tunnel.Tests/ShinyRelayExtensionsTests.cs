using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class ShinyRelayExtensionsTests
{
    [Fact]
    public void AddShinyRelay_UsesTheGivenPorts()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay", controlPort: 6000, publicPort: 6001);

        relay.Resource.Options.ControlPort.ShouldBe(6000);
        relay.Resource.Options.PublicPort.ShouldBe(6001);
    }

    [Fact]
    public void AddShinyRelay_BindsLoopbackByDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay");

        relay.Resource.Options.Address.ShouldBe(IPAddress.Loopback);
        relay.Resource.ClientHost.ShouldBe("localhost");
    }

    [Fact]
    public void WithBindAddress_SetsTheAddressAndWhatClientsDial()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay").WithBindAddress("0.0.0.0", clientHost: "192.168.1.20");

        relay.Resource.Options.Address.ShouldBe(IPAddress.Any);
        relay.Resource.ClientHost.ShouldBe("192.168.1.20");
    }

    [Fact]
    public void WithDomain_ShapesThePublicUrl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay", publicPort: 8080).WithDomain("localtest.me");

        relay.Resource.PublicBaseUrl.ShouldBe("http://localtest.me:8080");
    }

    [Fact]
    public void WithDomain_CanLeaveThePortOutWhenSomethingElseTerminates()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder
            .AddShinyRelay("relay", publicPort: 8080)
            .WithDomain("tunnels.example.com", scheme: "https", includePort: false);

        relay.Resource.PublicBaseUrl.ShouldBe("https://tunnels.example.com");
    }

    [Fact]
    public async Task ConnectionString_CarriesWhatAClientNeedsToRegister()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay", controlPort: 5050).WithToken("s3cret");

        var value = await relay.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);

        value.ShouldBe("Host=localhost;Port=5050;UseTls=false;Token=s3cret");
    }

    [Fact]
    public void ConfigureRelay_ReachesTheRestOfTheOptions()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay").ConfigureRelay(o => o.MaxTunnels = 5);

        relay.Resource.Options.MaxTunnels.ShouldBe(5);
    }

    [Fact]
    public void WithShinyRelayTunnel_AddsATunnelNamedAfterItsTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay");
        builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080).WithShinyRelayTunnel(relay);

        var tunnel = builder
            .Resources.OfType<InProcessTunnelResource>()
            .ShouldHaveSingleItem();

        tunnel.Name.ShouldBe("api-tunnel");
        tunnel.Kind.ShouldBe("shiny-relay");
    }

    [Fact]
    public void WithShinyRelayTunnel_AgainstAnExternalRelay_NeedsNoRelayResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder
            .AddContainer("api", "my-api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithShinyRelayTunnel("relay.example.com", port: 5050, token: "s3cret", subdomain: "api");

        builder.Resources.OfType<InProcessTunnelResource>().ShouldHaveSingleItem().Kind.ShouldBe("shiny-relay");
        builder.Resources.OfType<ShinyRelayResource>().ShouldBeEmpty();
    }

    [Fact]
    public void AddShinyRelay_EmptyName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentException>(() => builder.AddShinyRelay(""));
    }

    [Fact]
    public void WithToken_EmptyToken_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var relay = builder.AddShinyRelay("relay");

        Should.Throw<ArgumentException>(() => relay.WithToken(""));
    }
}
