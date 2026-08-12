using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using Shiny.Net.HttpServer.Ssh;

namespace Shiny.Aspire.Hosting.Tunnel.Ssh.Internal;

/// <summary>
/// Turns <see cref="SshTunnelOptions"/> into an SSH connection.
/// <para>
/// The options type is Shiny.Net.HttpServer's, so an app model and a MAUI app describe the same
/// tunnel the same way. Building the connection from it is repeated here rather than reused because
/// the httpserver package keeps that step internal — it hands its connections to an HTTP server,
/// where this forwards them to a port Aspire allocated.
/// </para>
/// </summary>
sealed class SshCredentials(SshTunnelOptions options, ILogger logger)
{
    // Kept for the life of the tunnel so a reconnect presents the same identity — sish and friends
    // derive the assigned subdomain from the key, and a new key means a new address.
    PrivateKeyFile? ephemeralKey;

    public ConnectionInfo CreateConnectionInfo()
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            throw new InvalidOperationException($"{nameof(SshTunnelOptions.Host)} is required.");

        if (string.IsNullOrWhiteSpace(options.Username))
            throw new InvalidOperationException($"{nameof(SshTunnelOptions.Username)} is required.");

        if (!options.AcceptAnyHostKey && options.HostKeyFingerprints.Count == 0)
            throw new InvalidOperationException(
                $"No host key to verify against. Pin one in {nameof(SshTunnelOptions.HostKeyFingerprints)} "
                    + $"(ssh-keyscan -p {options.Port} {options.Host} | ssh-keygen -lf -), or set "
                    + $"{nameof(SshTunnelOptions.AcceptAnyHostKey)} if you accept that anything on the path can pose as the server."
            );

        var methods = new List<AuthenticationMethod>();

        if (options.PrivateKey is { Length: > 0 } keyBytes)
        {
            using var stream = new MemoryStream(keyBytes);
            methods.Add(new PrivateKeyAuthenticationMethod(options.Username, LoadKey(stream, options.PrivateKeyPassPhrase)));
        }
        else if (options.PrivateKeyPath is { Length: > 0 } path)
        {
            methods.Add(new PrivateKeyAuthenticationMethod(options.Username, LoadKey(path, options.PrivateKeyPassPhrase)));
        }

        if (options.Password is not null)
            methods.Add(new PasswordAuthenticationMethod(options.Username, options.Password));

        if (methods.Count == 0 && options.UseEphemeralKey)
            methods.Add(new PrivateKeyAuthenticationMethod(options.Username, this.EphemeralKey()));

        if (methods.Count == 0)
            methods.Add(new NoneAuthenticationMethod(options.Username));

        return new ConnectionInfo(options.Host, options.Port, options.Username, [.. methods])
        {
            Timeout = options.ConnectTimeout
        };
    }

    public void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        if (options.AcceptAnyHostKey)
        {
            logger.LogWarning(
                "Accepting an unverified host key ({Fingerprint}). Pin it in HostKeyFingerprints.",
                e.FingerPrintSHA256
            );

            e.CanTrust = true;
            return;
        }

        // ssh-keygen prints "SHA256:…"; the SDK gives the bare base64. Accept either, and compare
        // without the base64 padding some tools keep and others strip.
        e.CanTrust = options.HostKeyFingerprints.Any(pinned => Matches(pinned, e.FingerPrintSHA256));

        if (!e.CanTrust)
            logger.LogError(
                "Rejected the host key {Fingerprint}: it is not among the pinned fingerprints",
                e.FingerPrintSHA256
            );
    }

    static bool Matches(string pinned, string presented)
    {
        var left = Normalize(pinned);
        var right = Normalize(presented);

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    static string Normalize(string fingerprint)
    {
        var value = fingerprint.Trim();

        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value["SHA256:".Length..];

        return value.TrimEnd('=');
    }

    PrivateKeyFile EphemeralKey()
    {
        if (this.ephemeralKey is not null)
            return this.ephemeralKey;

        using var rsa = RSA.Create(2048);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(rsa.ExportRSAPrivateKeyPem()));

        return this.ephemeralKey = new PrivateKeyFile(stream);
    }

    static PrivateKeyFile LoadKey(string path, string? passPhrase) =>
        passPhrase is { Length: > 0 } ? new PrivateKeyFile(path, passPhrase) : new PrivateKeyFile(path);

    static PrivateKeyFile LoadKey(Stream stream, string? passPhrase) =>
        passPhrase is { Length: > 0 } ? new PrivateKeyFile(stream, passPhrase) : new PrivateKeyFile(stream);
}
