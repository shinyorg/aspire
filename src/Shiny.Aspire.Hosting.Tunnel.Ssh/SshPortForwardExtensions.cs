using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;
using Shiny.Net.HttpServer.Ssh;

namespace Aspire.Hosting;

/// <summary>
/// Reaching a service on the far side of an SSH host from inside the app model.
/// </summary>
public static class SshPortForwardExtensions
{
    /// <summary>
    /// Opens a local port that SSH carries to <paramref name="remoteHost"/>, and hands referencing
    /// resources its address as a connection string.
    /// <para>
    /// For the service you cannot run locally and cannot reach directly: a staging database behind
    /// a bastion, an on-premises API, a queue on a private network. The app model treats it as an
    /// ordinary dependency.
    /// </para>
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name for the forward.</param>
    /// <param name="sshHost">The SSH host to tunnel through.</param>
    /// <param name="remoteHost">The service to reach, as named from the SSH host.</param>
    /// <param name="remotePort">The port to reach on <paramref name="remoteHost"/>.</param>
    /// <param name="configure">
    /// The rest of the SSH options — at minimum a username, a credential, and either a pinned host
    /// key fingerprint or <c>AcceptAnyHostKey</c>.
    /// </param>
    public static IResourceBuilder<SshPortForwardResource> AddSshPortForward(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string sshHost,
        string remoteHost,
        int remotePort,
        Action<SshTunnelOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sshHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteHost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(remotePort);

        var options = new SshTunnelOptions { Host = sshHost };
        configure?.Invoke(options);

        var resource = new SshPortForwardResource(name, options, remoteHost, remotePort);

        builder.Eventing.Subscribe<InitializeResourceEvent>(
            resource,
            (@event, cancellationToken) =>
            {
                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();
                var runner = new SshPortForwardRunner(resource, @event.Services);

                _ = Task.Run(() => runner.RunAsync(lifetime.ApplicationStopping), CancellationToken.None);

                return Task.CompletedTask;
            }
        );

        return builder
            .AddResource(resource)
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "SshPortForward",
                    State = KnownResourceStates.NotStarted,
                    CreationTimeStamp = DateTime.UtcNow,
                    Properties = []
                }
            )
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Fixes the local port instead of taking an ephemeral one — for the client that has the port
    /// written into a config file you would rather not touch.
    /// </summary>
    public static IResourceBuilder<SshPortForwardResource> WithLocalPort(
        this IResourceBuilder<SshPortForwardResource> builder,
        int port
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        builder.Resource.LocalPort = port;

        return builder;
    }

    /// <summary>
    /// Binds the forward on every interface so container resources can reach it. Loopback-only is
    /// the default because a forward on <c>0.0.0.0</c> is reachable by anything on the network the
    /// machine is attached to.
    /// </summary>
    public static IResourceBuilder<SshPortForwardResource> WithContainerAccess(
        this IResourceBuilder<SshPortForwardResource> builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.BindAddress = "0.0.0.0";

        return builder;
    }

    /// <summary>
    /// Shapes the connection string handed to referencing resources. Without it they get
    /// <c>host:port</c>, which suits a client that takes a host and a port and nothing else.
    /// </summary>
    /// <example>
    /// <code>
    /// var db = builder
    ///     .AddSshPortForward("staging-db", "bastion.example.com", "10.0.0.5", 5432, o => o.Username = "ops")
    ///     .WithConnectionString(f =&gt; ReferenceExpression.Create(
    ///         $"Host={f.Host};Port={f.Port};Database=app;Username=app;Password={password}"
    ///     ));
    /// </code>
    /// </example>
    public static IResourceBuilder<SshPortForwardResource> WithConnectionString(
        this IResourceBuilder<SshPortForwardResource> builder,
        Func<SshPortForwardResource, ReferenceExpression> template
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(template);

        builder.Resource.ConnectionStringTemplate = template;

        return builder;
    }
}
