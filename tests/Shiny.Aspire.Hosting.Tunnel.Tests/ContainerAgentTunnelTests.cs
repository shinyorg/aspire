using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class ContainerAgentTunnelTests
{
    [Fact]
    public void AddCloudflareTunnel_UsesTheOfficialImage()
    {
        var builder = DistributedApplication.CreateBuilder();
        var agent = builder.AddCloudflareTunnel("tunnel");

        var image = agent.Resource.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();

        image.Image.ShouldBe("cloudflare/cloudflared");
        image.Registry.ShouldBe("docker.io");
    }

    [Fact]
    public async Task Cloudflared_QuickTunnelArgsPointAtTheOrigin()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080);

        builder.AddCloudflareTunnel("tunnel").WithOrigin(api.GetEndpoint("http"));

        var args = await TunnelExtensionsTests.GetArgsAsync(
            builder.Resources.OfType<CloudflaredResource>().ShouldHaveSingleItem()
        );

        args.ShouldContain("tunnel");
        args.ShouldContain("--no-autoupdate");
        args.ShouldContain("--url");
        args.OfType<ReferenceExpression>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Cloudflared_WithNothingToPublish_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddCloudflareTunnel("tunnel");

        var agent = builder.Resources.OfType<CloudflaredResource>().ShouldHaveSingleItem();

        await Should.ThrowAsync<InvalidOperationException>(() => TunnelExtensionsTests.GetArgsAsync(agent));
    }

    [Fact]
    public async Task Cloudflared_NamedTunnelRunsFromATokenAndKnowsItsAddress()
    {
        var builder = DistributedApplication.CreateBuilder();
        var token = builder.AddParameter("cf-token", "token-value", secret: true);

        var agent = builder.AddCloudflareTunnel("tunnel").WithNamedTunnel(token, "https://api.example.com");

        var args = await TunnelExtensionsTests.GetArgsAsync(agent.Resource);

        args.ShouldContain("run");
        args.ShouldContain("--token");
        args.ShouldNotContain("--url");

        agent.Resource.PublicUrl.ShouldBe("https://api.example.com");
        (await agent.Resource.GetPublicUrlAsync(CancellationToken.None)).ShouldBe("https://api.example.com");
    }

    [Fact]
    public void WithCloudflareTunnel_AddsAnAgentNamedAfterItsTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080).WithCloudflareTunnel();

        builder.Resources.OfType<CloudflaredResource>().ShouldHaveSingleItem().Name.ShouldBe("api-tunnel");
    }

    [Theory]
    [InlineData("https://tidy-cats-run.trycloudflare.com")]
    [InlineData("2026-08-12T10:00:00Z INF |  https://tidy-cats-run.trycloudflare.com  |")]
    public void Cloudflared_ReadsItsAddressOutOfTheBanner(string line)
    {
        var resource = new CloudflaredResource("tunnel");

        resource.UrlPattern.Match(line).Value.ShouldBe("https://tidy-cats-run.trycloudflare.com");
    }

    [Fact]
    public void AddNgrokTunnel_UsesTheOfficialImageAndCarriesTheAuthToken()
    {
        var builder = DistributedApplication.CreateBuilder();
        var token = builder.AddParameter("ngrok-token", "token-value", secret: true);
        var agent = builder.AddNgrokTunnel("tunnel", token);

        var image = agent.Resource.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();

        image.Image.ShouldBe("ngrok/ngrok");
        agent.Resource.AuthToken.ShouldBe(token.Resource);
    }

    [Fact]
    public async Task Ngrok_ArgsAskForJsonLogsOnStdout()
    {
        var builder = DistributedApplication.CreateBuilder();
        var token = builder.AddParameter("ngrok-token", "token-value", secret: true);
        var api = builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080);

        var agent = builder.AddNgrokTunnel("tunnel", token).WithOrigin(api.GetEndpoint("http"));

        var args = await TunnelExtensionsTests.GetArgsAsync(agent.Resource);

        args.ShouldContain("http");
        args.ShouldContain("--log");
        args.ShouldContain("stdout");
        args.ShouldContain("--log-format");
        args.ShouldContain("json");
    }

    [Fact]
    public async Task Ngrok_WithDomainAsksForTheReservedOne()
    {
        var builder = DistributedApplication.CreateBuilder();
        var token = builder.AddParameter("ngrok-token", "token-value", secret: true);
        var api = builder.AddContainer("api", "my-api").WithHttpEndpoint(targetPort: 8080);

        var agent = builder
            .AddNgrokTunnel("tunnel", token)
            .WithOrigin(api.GetEndpoint("http"))
            .WithDomain("api.ngrok.app");

        var args = await TunnelExtensionsTests.GetArgsAsync(agent.Resource);

        args.ShouldContain("--url");
        args.ShouldContain("https://api.ngrok.app");
    }

    [Theory]
    [InlineData("{\"lvl\":\"info\",\"msg\":\"started tunnel\",\"url\":\"https://1a2b3c.ngrok-free.app\"}")]
    [InlineData("url=https://api.ngrok.app")]
    [InlineData("Forwarding https://1a2b-3c.ngrok.io -> http://host.docker.internal:8080")]
    public void Ngrok_ReadsItsAddressOutOfTheLog(string line)
    {
        var builder = DistributedApplication.CreateBuilder();
        var token = builder.AddParameter("ngrok-token", "token-value", secret: true);
        var resource = builder.AddNgrokTunnel("tunnel", token).Resource;

        resource.UrlPattern.Match(line).Success.ShouldBeTrue();
    }

    [Fact]
    public void ContainerAgents_AreTunnelResourcesToo()
    {
        var builder = DistributedApplication.CreateBuilder();
        var agent = builder.AddCloudflareTunnel("tunnel");

        agent.Resource.ShouldBeAssignableTo<ITunnelResource>();
        agent.Resource.ShouldBeAssignableTo<IResourceWithConnectionString>();
        agent.Resource.ShouldBeAssignableTo<ContainerResource>();
    }

    [Fact]
    public void AddNgrokTunnel_NullAuthToken_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentNullException>(() => builder.AddNgrokTunnel("tunnel", null!));
    }
}
