using Microsoft.Extensions.Logging;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>
/// Points everything a provider logs at one resource's log stream, so a tunnel's own diagnostics
/// land in the dashboard beside it rather than in the AppHost's console.
/// </summary>
sealed class ResourceLoggerFactory(ILogger logger) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
