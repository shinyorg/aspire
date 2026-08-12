using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;

namespace Shiny.Aspire.Hosting.Tunnel.Tests;

public class SshPortForwardExtensionsTests
{
    [Fact]
    public void AddSshPortForward_CapturesWhatToReachAndHow()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("staging-db", "bastion.example.com", "10.0.0.5", 5432, o => o.Username = "ops");

        db.Resource.Options.Host.ShouldBe("bastion.example.com");
        db.Resource.Options.Username.ShouldBe("ops");
        db.Resource.RemoteHost.ShouldBe("10.0.0.5");
        db.Resource.RemotePort.ShouldBe(5432);
    }

    [Fact]
    public void AddSshPortForward_BindsLoopbackByDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432);

        db.Resource.BindAddress.ShouldBe("127.0.0.1");
        db.Resource.LocalPort.ShouldBe(0);
    }

    [Fact]
    public void WithContainerAccess_BindsEveryInterface()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432).WithContainerAccess();

        db.Resource.BindAddress.ShouldBe("0.0.0.0");
    }

    [Fact]
    public void WithLocalPort_FixesThePort()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432).WithLocalPort(15432);

        db.Resource.LocalPort.ShouldBe(15432);
    }

    [Fact]
    public async Task ConnectionString_DefaultsToHostAndPort()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432).WithLocalPort(15432);

        db.Resource.BoundPort = 15432;
        db.Resource.MarkBound();

        var value = await db.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);

        value.ShouldBe("localhost:15432");
    }

    [Fact]
    public async Task WithConnectionString_ShapesItForTheClientOnTheOtherEnd()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder
            .AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432)
            .WithLocalPort(15432)
            .WithConnectionString(f => ReferenceExpression.Create($"Host={f.Host};Port={f.Port};Database=app"));

        db.Resource.BoundPort = 15432;
        db.Resource.MarkBound();

        var value = await db.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None);

        value.ShouldBe("Host=localhost;Port=15432;Database=app");
    }

    [Fact]
    public async Task ConnectionString_WaitsForTheForwardToBind()
    {
        var builder = DistributedApplication.CreateBuilder();
        var db = builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 5432);

        var pending = db.Resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None).AsTask();

        pending.IsCompleted.ShouldBeFalse();

        db.Resource.BoundPort = 49152;
        db.Resource.MarkBound();

        (await pending).ShouldBe("localhost:49152");
    }

    [Fact]
    public void AddSshPortForward_ZeroRemotePort_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentOutOfRangeException>(
            () => builder.AddSshPortForward("db", "bastion.example.com", "10.0.0.5", 0)
        );
    }

    [Fact]
    public void AddSshPortForward_EmptyRemoteHost_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();

        Should.Throw<ArgumentException>(() => builder.AddSshPortForward("db", "bastion.example.com", "", 5432));
    }
}
