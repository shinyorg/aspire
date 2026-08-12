using Aspire.Hosting.ApplicationModel;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// The public-address half of a tunnel resource, kept in one place because the two kinds of tunnel
/// — in-process and container agent — have different base classes and identical URL semantics.
/// </summary>
sealed class TunnelUrlSource(ITunnelResource resource)
{
    // Completed the first time an address is known. Referencing resources await this, which is why
    // a tunnel is opened as soon as its target's endpoints are allocated rather than when the
    // target is running: a webhook receiver that needs its own public URL would otherwise be
    // waiting on itself.
    readonly TaskCompletionSource<string> opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string? Current { get; private set; }

    public ReferenceExpression Expression => ReferenceExpression.Create($"{new TunnelPublicUrlProvider(resource)}");

    public Task<string> WaitAsync(CancellationToken cancellationToken) => this.opened.Task.WaitAsync(cancellationToken);

    public void Set(string url)
    {
        this.Current = url;
        this.opened.TrySetResult(url);
    }

    public void Clear() => this.Current = null;

    /// <summary>
    /// Releases anything waiting on the address when the tunnel will never open. Without it a
    /// resource referencing a failed tunnel hangs at startup instead of reporting why.
    /// </summary>
    public void Fail(Exception exception) => this.opened.TrySetException(exception);
}
