using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class AzureRelayTunnelExtensionsTests
{
    const string ConnectionString =
        "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=listen;SharedAccessKey=key;EntityPath=api";

    [Fact]
    public void AddAzureRelayTunnel_CreatesAnAzureRelayTunnel()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddAzureRelayTunnel("public", ConnectionString);

        tunnel.Resource.Kind.ShouldBe("azure-relay");
        tunnel.Resource.ShouldBeAssignableTo<ITunnelResource>();
    }

    [Fact]
    public void AddAzureRelayTunnel_AcceptsTheConnectionStringFromAParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var parameter = builder.AddParameter("relay-cs", ConnectionString, secret: true);
        var tunnel = builder.AddAzureRelayTunnel("public", parameter, "api");

        tunnel.Resource.Kind.ShouldBe("azure-relay");
    }

    [Fact]
    public void WithAzureRelayTunnel_AddsATunnelNamedAfterItsTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder
            .AddContainer("api", "my-api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithAzureRelayTunnel(ConnectionString, "api");

        builder.Resources.OfType<InProcessTunnelResource>().ShouldHaveSingleItem().Name.ShouldBe("api-tunnel");
    }

    [Fact]
    public void AddAzureRelayTunnel_EmptyConnectionString_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentException>(() => builder.AddAzureRelayTunnel("public", ""));
    }
}
