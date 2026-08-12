using Shiny.Aspire.Orleans.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// --- Parameters for VPN credentials (optional, for Gluetun demo) ---
var vpnUser = builder.AddParameter("vpn-user", secret: true);
var vpnPassword = builder.AddParameter("vpn-password", secret: true);

// --- Database ---
var db = builder
    .AddPostgres("pg")
    .WithPgAdmin()
    .AddDatabase("orleans-db");

// --- Orleans cluster (clustering, persistence, reminders all on Postgres) ---
var orleans = builder.AddOrleans("cluster")
    .WithClustering(db)
    .WithGrainStorage("Default", db)
    .WithReminders(db)
    .WithDatabaseSetup(db);

// --- Silo (full server with dashboard) ---
var silo = builder.AddProject<Projects.Sample_Silo>("silo")
    .WithReference(orleans)
    .WithHttpsEndpoint(port: 8080, name: "dashboard")
    .WaitFor(db);

// --- API (client) ---
var api = builder.AddProject<Projects.Sample_Api>("api")
    .WithReference(orleans.AsClient())
    .WaitFor(silo);

// --- Tunnelling ---
// A public HTTPS address for the API, with no account and nothing installed. The address changes
// on every reconnect, so it is handed to the API rather than written down anywhere.
api.WithQuickTunnel(name: "api-public");

// The relay hosted right here, which is what a MAUI app running Shiny.Net.HttpServer registers
// into — control on 5050, public traffic on 8080.
var relay = builder.AddShinyRelay("relay")
    .WithToken("dev-token")
    .WithDomain("localtest.me");

// The same API published a second way, through that relay.
api.WithShinyRelayTunnel(relay, subdomain: "api", name: "api-relayed");

// ...and cloudflared in a container, for comparison:
// api.WithCloudflareTunnel(name: "api-cloudflare");

// --- Gluetun VPN container (demo) ---
var gluetun = builder.AddGluetun("vpn")
    .WithVpnProvider("nordvpn")
    .WithOpenVpn(vpnUser, vpnPassword)
    .WithServerCountries("United States")
    .WithHttpProxy();

builder.Build().Run();
