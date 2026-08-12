using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shiny.Aspire.Hosting.Tunnel.Internal;
using Shiny.Net.HttpServer.Tunneling;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class TunnelExtensionsTests
{
    [Fact]
    public void AddTunnel_CreatesInProcessTunnelResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        tunnel.Resource.ShouldBeOfType<InProcessTunnelResource>();
        tunnel.Resource.Name.ShouldBe("public");
        tunnel.Resource.Kind.ShouldBe("test");
    }

    [Fact]
    public void AddTunnel_IsExcludedFromManifest()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        tunnel.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>().ShouldNotBeEmpty();
    }

    [Fact]
    public void AddTunnel_StartsWithNoPublicUrl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        tunnel.Resource.PublicUrl.ShouldBeNull();
        tunnel.Resource.TargetEndpoint.ShouldBeNull();
    }

    [Fact]
    public void WithTunnel_MakesTheTunnelAChildOfItsTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());
        var api = builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080);

        api.WithTunnel(tunnel);

        tunnel
            .Resource.Annotations.OfType<ResourceRelationshipAnnotation>()
            .ShouldContain(x => x.Resource.Name == "api" && x.Type == "Parent");
    }

    [Fact]
    public void WithTunnel_TwiceOnTheSameTunnel_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());
        var one = builder.AddContainer("one", "my-api").WithHttpEndpoint(targetPort: 8080);
        var two = builder.AddContainer("two", "my-api").WithHttpEndpoint(targetPort: 8080);

        one.WithTunnel(tunnel);

        Should.Throw<InvalidOperationException>(() => two.WithTunnel(tunnel));
    }

    [Fact]
    public async Task ConnectionString_ResolvesToThePublicUrl()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        tunnel.Resource.SetPublicUrl("https://abc123.example.com");

        var value = await tunnel.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);

        value.ShouldBe("https://abc123.example.com");
    }

    [Fact]
    public async Task ConnectionString_WaitsForTheTunnelToOpen()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        var pending = tunnel.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None).AsTask();

        pending.IsCompleted.ShouldBeFalse();

        tunnel.Resource.SetPublicUrl("https://abc123.example.com");

        (await pending).ShouldBe("https://abc123.example.com");
    }

    [Fact]
    public async Task ConnectionString_SurfacesTheFailureRatherThanHanging()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());

        tunnel.Resource.SetFailed(new InvalidOperationException("no route"));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await tunnel.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task WithTunnelUrl_SetsTheEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();
        var tunnel = builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException());
        var api = builder.AddContainer("api", "my-api").WithTunnelUrl("PUBLIC_URL", tunnel);

        tunnel.Resource.SetPublicUrl("https://abc123.example.com");

        var variables = await GetEnvironmentVariablesAsync(builder, api.Resource);

        variables.ShouldContainKey("PUBLIC_URL");
    }

    [Fact]
    public void AddTunnel_NullBuilder_Throws()
    {
        IDistributedApplicationBuilder builder = null!;

        Should.Throw<ArgumentNullException>(
            () => builder.AddTunnel("public", "test", (_, _) => throw new NotSupportedException())
        );
    }

    [Fact]
    public void AddTunnel_EmptyName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentException>(() => builder.AddTunnel("", "test", (_, _) => throw new NotSupportedException()));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public void TunnelTarget_PrefersHttpOverHttps(string _)
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder
            .AddContainer("api", "my-api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithHttpsEndpoint(targetPort: 8443);

        // Cleartext arrives from the tunnel, so the TLS port would fail every request.
        TunnelTarget.Resolve(api.Resource, null).EndpointName.ShouldBe("http");
    }

    [Fact]
    public void TunnelTarget_NamedEndpointWins()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder
            .AddContainer("api", "my-api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithEndpoint(targetPort: 9000, name: "admin");

        TunnelTarget.Resolve(api.Resource, "admin").EndpointName.ShouldBe("admin");
    }

    [Fact]
    public void TunnelTarget_SingleUnnamedEndpointNeedsNoNaming()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "my-api").WithEndpoint(targetPort: 9000, name: "admin");

        TunnelTarget.Resolve(api.Resource, null).EndpointName.ShouldBe("admin");
    }

    [Fact]
    public void TunnelTarget_AmbiguousEndpoints_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder
            .AddContainer("api", "my-api")
            .WithEndpoint(targetPort: 9000, name: "admin")
            .WithEndpoint(targetPort: 9001, name: "metrics");

        Should.Throw<InvalidOperationException>(() => TunnelTarget.Resolve(api.Resource, null));
    }

    [Fact]
    public void TunnelTarget_NoEndpoints_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "my-api");

        Should.Throw<InvalidOperationException>(() => TunnelTarget.Resolve(api.Resource, null));
    }

    internal static async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(
        IDistributedApplicationBuilder builder,
        IResource resource
    )
    {
        var variables = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, variables, CancellationToken.None);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await annotation.Callback(context);

        return variables.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "");
    }

    internal static async Task<List<object>> GetArgsAsync(IResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, CancellationToken.None);

        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await annotation.Callback(context);

        return args;
    }
}
