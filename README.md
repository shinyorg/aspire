# Shiny Aspire Libraries

Zero-friction integration between [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) and [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) for ADO.NET storage backends. Automatically provisions Orleans database schemas and wires up clustering, grain persistence, and reminders from Aspire configuration — no manual SQL scripts or connection string plumbing required.

Also includes Aspire hosting integrations for [Gluetun](https://github.com/qdm12/gluetun) VPN containers, and for tunnelling — giving a project or container endpoint a public address, hosting the Shiny relay in the app model, and reaching services on the far side of a bastion.

## Supported Databases

- PostgreSQL
- SQL Server
- MySQL

## Packages

| Package | NuGet | Usage |
|---|---|---|
| `Shiny.Aspire.Orleans.Hosting` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Orleans.Hosting.svg)](https://www.nuget.org/packages/Shiny.Aspire.Orleans.Hosting) | Aspire AppHost — auto-runs Orleans schema scripts when the database becomes ready |
| `Shiny.Aspire.Orleans.Server` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Orleans.Server.svg)](https://www.nuget.org/packages/Shiny.Aspire.Orleans.Server) | Orleans silo — registers ADO.NET providers for clustering, grain storage, and reminders |
| `Shiny.Aspire.Orleans.Client` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Orleans.Client.svg)](https://www.nuget.org/packages/Shiny.Aspire.Orleans.Client) | Orleans client — registers ADO.NET clustering provider |
| `Shiny.Aspire.Hosting.Gluetun` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Gluetun.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Gluetun) | Aspire AppHost — adds a Gluetun VPN container and routes other containers through it |
| `Shiny.Aspire.Hosting.Tunnel` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Tunnel.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Tunnel) | Aspire AppHost — the tunnel resource, the pluggable provider, and the Shiny relay hosted in the app model |
| `Shiny.Aspire.Hosting.Tunnel.Ssh` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Tunnel.Ssh.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Tunnel.Ssh) | Zero-account quick tunnels, SSH remote forwarding, and local forwards to services behind a bastion |
| `Shiny.Aspire.Hosting.Tunnel.AzureRelay` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Tunnel.AzureRelay.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Tunnel.AzureRelay) | Azure Relay Hybrid Connections — a stable public address with no inbound connectivity |
| `Shiny.Aspire.Hosting.Tunnel.Cloudflare` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Tunnel.Cloudflare.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Tunnel.Cloudflare) | `cloudflared` as a container resource — quick tunnels and named tunnels |
| `Shiny.Aspire.Hosting.Tunnel.Ngrok` | [![NuGet](https://img.shields.io/nuget/v/Shiny.Aspire.Hosting.Tunnel.Ngrok.svg)](https://www.nuget.org/packages/Shiny.Aspire.Hosting.Tunnel.Ngrok) | The ngrok agent as a container resource, with the assigned address surfaced as a connection string |

## Quick Start

### 1. AppHost (Aspire Orchestrator)

Install `Shiny.Aspire.Orleans.Hosting` in your AppHost project.

```csharp
using Shiny.Aspire.Orleans.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("pg")
    .WithPgAdmin()
    .AddDatabase("orleans-db");

var orleans = builder.AddOrleans("cluster")
    .WithClustering(db)
    .WithGrainStorage("Default", db)
    .WithReminders(db)
    .WithDatabaseSetup(db); // <-- creates all Orleans tables automatically

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(orleans)
    .WaitFor(db);

builder.AddProject<Projects.MyApi>("api")
    .WithReference(orleans.AsClient())
    .WaitFor(db);

builder.Build().Run();
```

`WithDatabaseSetup` subscribes to Aspire's `ResourceReadyEvent` for the database resource. When the database container is up and accepting connections, it automatically executes the Orleans SQL schema scripts (clustering tables, persistence tables, reminders tables, stored procedures, and query registrations).

### 2. Orleans Silo

Install `Shiny.Aspire.Orleans.Server` in your silo project. The package registers Orleans provider builders for all supported database types (`PostgresDatabase`, `SqlServerDatabase`, `MySqlDatabase`) via assembly-level `[RegisterProvider]` attributes. Orleans' `ApplyConfiguration` automatically resolves these providers from the Aspire-injected configuration.

Call `silo.UseAdoNet()` inside `UseOrleans` for discoverability — provider registration is automatic when the package is referenced.

```csharp
using Shiny.Aspire.Orleans.Server;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo =>
{
    silo.UseAdoNet();
});

var app = builder.Build();
app.Run();
```

Because the extension is on `ISiloBuilder`, you can compose it with other Orleans features:

```csharp
using Shiny.Aspire.Orleans.Server;

builder.UseOrleans(silo =>
{
    silo.UseAdoNet();
    // add other silo configuration here
});
```

### 3. Orleans Client

Install `Shiny.Aspire.Orleans.Client` in your client project (e.g. an API gateway).

```csharp
using Shiny.Aspire.Orleans.Client;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleansClient(client =>
{
    client.UseAdoNetClient();
});

var app = builder.Build();

app.MapGet("/counter/{name}", async (string name, IClusterClient client) =>
{
    var grain = client.GetGrain<ICounterGrain>(name);
    var count = await grain.GetCount();
    return Results.Ok(new { name, count });
});

app.Run();
```

By default, `WithDatabaseSetup` creates schemas for all Orleans features. You can limit this using the `OrleansFeature` flags enum:

```csharp
// Only set up clustering and persistence tables (no reminders)
orleans.WithDatabaseSetup(db, OrleansFeature.Clustering | OrleansFeature.Persistence);

// Only set up clustering
orleans.WithDatabaseSetup(db, OrleansFeature.Clustering);
```

Available flags:

| Flag | Value | Description |
|---|---|---|
| `Clustering` | 1 | Membership tables for silo discovery |
| `Persistence` | 2 | Grain storage tables |
| `Reminders` | 4 | Reminder tables |
| `All` | 7 | All of the above (default) |

## Using Different Databases

The database type is auto-detected from the Aspire resource. Just swap the resource builder:

```csharp
// PostgreSQL
var db = builder.AddPostgres("pg").AddDatabase("orleans-db");

// SQL Server
var db = builder.AddSqlServer("sql").AddDatabase("orleans-db");

// MySQL
var db = builder.AddMySql("mysql").AddDatabase("orleans-db");
```

Everything else stays the same — the correct SQL scripts, connection provider, and ADO.NET invariant are selected automatically.

## How It Works

### Provider Registration

The Server and Client packages register Orleans provider builders via `[assembly: RegisterProvider]` attributes. When Orleans calls `ApplyConfiguration`, it reads the Aspire-injected configuration (e.g. `Orleans:Clustering:ProviderType = "PostgresDatabase"`) and resolves the matching provider builder automatically. The provider builder maps the database type to the correct ADO.NET invariant and connection string.

Registered provider names:

| ProviderType | Invariant | Kinds |
|---|---|---|
| `PostgresDatabase` | `Npgsql` | Clustering, GrainStorage, Reminders |
| `SqlServerDatabase` | `Microsoft.Data.SqlClient` | Clustering, GrainStorage, Reminders |
| `MySqlDatabase` | `MySql.Data.MySqlClient` | Clustering, GrainStorage, Reminders |

### Configuration Flow

Aspire automatically injects configuration into your silo and client projects when you use `.WithReference(orleans)`. The injected configuration looks like:

```
Orleans:Clustering:ProviderType = "PostgresDatabase"
Orleans:Clustering:ServiceKey   = "orleans-db"
Orleans:GrainStorage:Default:ProviderType = "PostgresDatabase"
Orleans:GrainStorage:Default:ServiceKey   = "orleans-db"
Orleans:Reminders:ProviderType  = "PostgresDatabase"
Orleans:Reminders:ServiceKey    = "orleans-db"
ConnectionStrings:orleans-db    = "Host=...;Database=..."
```

Orleans' `ApplyConfiguration` reads these sections and delegates to the registered provider builders, which configure the ADO.NET providers (`Npgsql`, `Microsoft.Data.SqlClient`, or `MySqlConnector`) with the correct connection strings and invariants.

### Schema Provisioning

`WithDatabaseSetup` runs embedded SQL scripts in order:

1. **Main** — creates the `OrleansQuery` table (Orleans' query registry)
2. **Clustering** — creates `OrleansMembershipVersionTable`, `OrleansMembershipTable`, and related stored procedures/functions
3. **Persistence** — creates the `OrleansStorage` table and related stored procedures/functions
4. **Reminders** — creates `OrleansRemindersTable` and related stored procedures/functions

Scripts are executed when Aspire raises the `ResourceReadyEvent` for the database, ensuring the database is accepting connections before any schema setup runs.

## Multiple Grain Storage Providers

The server package supports multiple named grain storage providers:

```csharp
// AppHost
var orleans = builder.AddOrleans("cluster")
    .WithClustering(db)
    .WithGrainStorage("Default", db)
    .WithGrainStorage("Archive", archiveDb)
    .WithDatabaseSetup(db);

// Grain
public class MyGrain(
    [PersistentState("state", "Default")] IPersistentState<MyState> state,
    [PersistentState("archive", "Archive")] IPersistentState<ArchiveState> archive
) : Grain, IMyGrain { }
```

## Sample

The `samples/` directory contains a complete working example:

| Project | Description |
|---|---|
| `Sample.AppHost` | Aspire orchestrator wiring PostgreSQL + PgAdmin, Orleans cluster, API, Gluetun VPN, and tunnels |
| `Sample.Silo` | Orleans silo with ADO.NET providers |
| `Sample.Api` | HTTP API with counter and reminder endpoints via `IClusterClient` |
| `Sample.GrainInterfaces` | `ICounterGrain` and `IReminderGrain` interfaces |
| `Sample.Grains` | `CounterGrain` (persistent state) and `ReminderGrain` (ADO.NET reminders) |

Run the sample:

```bash
dotnet run --project samples/Sample.AppHost
```

---

# Shiny.Aspire.Hosting.Gluetun

Aspire hosting integration for [Gluetun](https://github.com/qdm12/gluetun), a lightweight VPN client container supporting multiple providers. Models Gluetun as a first-class Aspire resource and lets other containers route their traffic through the VPN tunnel.

## Quick Start

Install `Shiny.Aspire.Hosting.Gluetun` in your AppHost project.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var vpn = builder.AddGluetun("vpn")
    .WithVpnProvider("mullvad")
    .WithWireGuard(builder.AddParameter("wireguard-key", secret: true))
    .WithServerCountries("US", "Canada");

var scraper = builder.AddContainer("scraper", "my-scraper")
    .WithHttpEndpoint(targetPort: 8080);

vpn.WithRoutedContainer(scraper);

builder.Build().Run();
```

This creates a Gluetun VPN container with Mullvad WireGuard, then routes the `scraper` container's traffic through it. At runtime the scraper joins the Gluetun network namespace (`--network container:vpn`). On Docker Compose publish, routed containers get `network_mode: "service:vpn"` and their ports are transferred to the Gluetun service.

## API Reference

### AddGluetun

Creates a Gluetun container resource with `NET_ADMIN` capability and `/dev/net/tun` device access.

```csharp
IResourceBuilder<GluetunResource> AddGluetun(
    this IDistributedApplicationBuilder builder,
    string name,
    int? httpProxyPort = null,
    int? shadowsocksPort = null)
```

The optional port parameters expose Gluetun's built-in HTTP proxy (default target 8888) and Shadowsocks proxy (default target 8388) endpoints.

### VPN Provider Configuration

```csharp
// Set the VPN service provider (required)
vpn.WithVpnProvider("mullvad");

// OpenVPN — string credentials
vpn.WithOpenVpn("username", "password");

// OpenVPN — Aspire parameter resources (recommended for secrets)
vpn.WithOpenVpn(
    builder.AddParameter("openvpn-user"),
    builder.AddParameter("openvpn-pass", secret: true));

// WireGuard — string key
vpn.WithWireGuard("my-private-key");

// WireGuard — Aspire parameter resource (recommended for secrets)
vpn.WithWireGuard(builder.AddParameter("wireguard-key", secret: true));
```

### Server Selection

```csharp
vpn.WithServerCountries("US", "Canada", "Germany");
vpn.WithServerCities("New York", "Toronto");
```

Values are comma-joined and set as `SERVER_COUNTRIES` / `SERVER_CITIES` environment variables.

### Proxy Features

```csharp
vpn.WithHttpProxy();           // enables Gluetun's built-in HTTP proxy (HTTPPROXY=on)
vpn.WithHttpProxy(false);      // disables it (HTTPPROXY=off)
vpn.WithShadowsocks();         // enables Shadowsocks proxy (SHADOWSOCKS=on)
vpn.WithShadowsocks(false);    // disables it (SHADOWSOCKS=off)
```

### Network & Firewall

```csharp
vpn.WithFirewallOutboundSubnets("10.0.0.0/8", "192.168.0.0/16");
vpn.WithTimezone("America/New_York");
```

### Generic Environment Variables

Pass any Gluetun environment variable not covered by the typed methods:

```csharp
vpn.WithGluetunEnvironment("DNS_ADDRESS", "1.1.1.1");
vpn.WithGluetunEnvironment("UPDATER_PERIOD", builder.AddParameter("updater-period"));
```

### Routing Containers Through the VPN

```csharp
vpn.WithRoutedContainer(scraper);
vpn.WithRoutedContainer(downloader);
```

Each call:
1. Adds a `GluetunRoutedResourceAnnotation` to the Gluetun resource
2. Sets `--network container:<vpn-name>` runtime args on the routed container
3. On Docker Compose publish, sets `network_mode: "service:<vpn-name>"` on the routed container and transfers its port mappings to the Gluetun service

You can route multiple containers through the same VPN.

## Docker Compose Publish

When you publish with `dotnet run --publisher manifest` or Docker Compose, routed containers automatically get:

```yaml
services:
  vpn:
    image: qmcgaw/gluetun:latest
    cap_add:
      - NET_ADMIN
    devices:
      - /dev/net/tun
    environment:
      - VPN_SERVICE_PROVIDER=mullvad
      - VPN_TYPE=wireguard
      - WIREGUARD_PRIVATE_KEY=${wireguard-key}
      - SERVER_COUNTRIES=US,Canada
    ports:
      - "8080:8080"    # forwarded from scraper
  scraper:
    image: my-scraper
    network_mode: "service:vpn"
    # ports moved to vpn service
```

## Supported VPN Providers

Gluetun supports 30+ VPN providers. See the [Gluetun wiki](https://github.com/qdm12/gluetun-wiki) for the full list and provider-specific environment variables. Use `WithGluetunEnvironment` for any provider-specific settings not covered by the typed methods.

---

# Shiny.Aspire.Hosting.Tunnel

A public address for something that is only listening on localhost.

The problem is the same one [Shiny.Net.HttpServer](https://github.com/shinyorg/httpserver) solves for
an embedded server, and the pieces are the same pieces: a pluggable `ITunnelProvider`, the Shiny
relay at both ends, SSH remote forwarding, zero-account quick tunnels, and Azure Relay. What changes
is who runs them — here it is the AppHost, on behalf of a resource in the app model.

A tunnel is a resource. Its public URL is its connection string, so everything Aspire already knows
how to do with a connection string works: `WithReference(tunnel)` injects it, `WithTunnelUrl` names
it, and both wait for the tunnel to actually open before the resource that needs it starts.

## Quick Start

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Api>("api");

// A public HTTPS address, no account, nothing installed.
api.WithQuickTunnel();

builder.Build().Run();
```

The address appears on the tunnel resource in the dashboard. To give it to the service that needs to
hand it out — an OAuth redirect URI, a webhook registration, a QR code:

```csharp
var tunnel = builder.AddQuickTunnel("public");

builder.AddProject<Projects.Api>("api")
    .WithTunnel(tunnel)
    .WithTunnelUrl("PUBLIC_URL", tunnel);
```

That is not circular. A tunnel opens as soon as its target's **endpoints are allocated**, which
happens before the target process is launched — so a service is allowed to need its own public URL.

## Choosing a provider

| | Address | Needs | Good for |
|---|---|---|---|
| `WithQuickTunnel()` | assigned, changes on reconnect | nothing | a webhook you are debugging right now, showing work to someone |
| `WithSshTunnel(host, …)` | yours | an SSH server you can log in to | a stable address on infrastructure you already own |
| `WithShinyRelayTunnel(relay)` | yours | the relay, hosted here or on your VPS | devices and embedded servers registering into a dev environment |
| `WithAzureRelayTunnel(cs)` | stable, yours | an Azure Relay namespace | enterprise networks; an integration configured once |
| `WithCloudflareTunnel()` | assigned, or your domain | Docker; an account for named tunnels | teams already on Cloudflare |
| `WithNgrokTunnel(token)` | assigned, or a reserved domain | Docker; an ngrok account | teams already on ngrok |

The first four run inside the AppHost — managed code, no daemon, no binary to install, identical on
every developer's machine and in CI. The last two run the vendor's agent in a container.

## Quick tunnels

```csharp
api.WithQuickTunnel();                                    // pinggy.io, the default
api.WithQuickTunnel(QuickTunnelHost.Sish);                // tuns.sh
api.WithQuickTunnel(subdomain: "<pinggy-access-token>");  // lifts the 60-minute anonymous cap
```

Traffic through a free tunnel passes through someone else's server, and these hosts publish no
stable key to pin — `AcceptAnyHostKey` is on for them, deliberately and visibly.

## SSH

```csharp
api.WithSshTunnel("tunnel.example.com", ssh =>
{
    ssh.Username = "deploy";
    ssh.PrivateKeyPath = "/Users/me/.ssh/id_ed25519";
    ssh.RemoteBindAddress = "0.0.0.0";        // needs GatewayPorts on the server
    ssh.RemotePort = 8080;
    ssh.PublicUrl = "https://api.example.com"; // where it really answers, if a proxy fronts it
    ssh.HostKeyFingerprints.Add("SHA256:47DEQpj8HBSa+…");
});
```

The AppHost dials out and asks the server to forward a port back down the connection — `ssh -R`, in
library form — pointed straight at the port Aspire allocated. Nothing connects in, so this works
from behind NAT, from a CI runner, and from a network that allows nothing but outbound 443.

## The Shiny relay

The relay is the public end of a Shiny tunnel: a control port where clients register, and a public
port where traffic arrives to be routed by Host header. Hosting it in the app model gives a MAUI app
running Shiny.Net.HttpServer somewhere to register — a phone's embedded server, reachable from your
development environment, with no cloud account and nothing listening on the phone.

```csharp
var relay = builder.AddShinyRelay("relay", controlPort: 5050, publicPort: 8080)
    .WithToken(builder.AddParameter("relay-token", secret: true))
    .WithDomain("localtest.me")
    .WithBindAddress("0.0.0.0", clientHost: "192.168.1.20");   // so the phone can reach it

api.WithShinyRelayTunnel(relay, subdomain: "api");
```

The relay's connection string is what a client needs to register —
`Host=…;Port=…;UseTls=…;Token=…`, matching the properties of `RelayTunnelOptions` on the other side.

Against a relay running somewhere else:

```csharp
api.WithShinyRelayTunnel("relay.example.com", port: 5050, token: token, subdomain: "api");
```

## Azure Relay

```csharp
var cs = builder.AddParameter("relay-cs", secret: true);

api.WithAzureRelayTunnel(cs, hybridConnectionName: "api");
```

## Container agents

```csharp
api.WithCloudflareTunnel();                                  // throwaway trycloudflare.com address
api.WithNgrokTunnel(builder.AddParameter("ngrok-token", secret: true));

// A named Cloudflare tunnel, on a host name you control:
builder.AddCloudflareTunnel("public")
    .WithNamedTunnel(builder.AddParameter("cf-token", secret: true), "https://api.example.com");
```

Both agents pick their address at runtime and announce it in their own output, so it is read back
out of the container's log stream and published from there.

## Reaching a service through a bastion

The other direction: something you cannot run locally and cannot reach directly.

```csharp
var db = builder
    .AddSshPortForward("staging-db", "bastion.example.com", "10.0.0.5", 5432, ssh =>
    {
        ssh.Username = "ops";
        ssh.PrivateKeyPath = "/Users/me/.ssh/id_ed25519";
        ssh.AcceptAnyHostKey = true;
    })
    .WithConnectionString(f => ReferenceExpression.Create(
        $"Host={f.Host};Port={f.Port};Database=app;Username=app;Password={password}"
    ));

builder.AddProject<Projects.Api>("api").WithReference(db);
```

The AppHost opens a local port that SSH carries to the far side, and the app model treats it as an
ordinary dependency. Container resources reach it too — add `.WithContainerAccess()`, which binds
every interface rather than loopback, and the host name resolves per caller.

## Writing your own provider

`AddTunnel` takes any `ITunnelProvider` — the same abstraction Shiny.Net.HttpServer tunnels through.
Everything it hands over is pumped into the attached endpoint, byte for byte, so WebSockets, SSE and
gRPC streaming pass through unchanged.

```csharp
var tunnel = builder.AddTunnel("public", "my-provider", (context, ct) =>
    ValueTask.FromResult<ITunnelProvider>(new MyTunnelProvider(context.TargetHost, context.TargetPort))
);

api.WithTunnel(tunnel);
```

## Notes

- **The target must be a cleartext endpoint.** TLS is terminated at the public end of the tunnel, so
  what arrives is plain HTTP. The `http` endpoint is chosen by default for exactly this reason; name
  another with `endpointName` only if it is also cleartext.
- **Tunnels are excluded from the manifest.** They are a development-time affordance for reaching a
  machine that is not on the internet; a deployed app has a real address.
- **A quick tunnel's address changes on reconnect.** An environment variable handed to a running
  process cannot be revised, so `WithTunnelUrl` resolves once and keeps that value. The dashboard
  shows the current one.
- **Anything reachable through a tunnel is reachable by anyone who has the URL.** These hostnames are
  unguessable, not private. Put authentication in front of anything that matters.

## Requirements

- .NET 10
- .NET Aspire 13.1+
- Microsoft Orleans 10.0+ (for Orleans packages only)
- Shiny.Net.HttpServer 1.0.0-beta.11+ (for tunnel packages only)
- Docker (for the Gluetun, Cloudflare and ngrok packages only)
