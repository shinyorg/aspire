using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;

namespace Shiny.Aspire.Hosting.Tunnel.Internal;

/// <summary>What the dashboard is told about a tunnel.</summary>
static class TunnelStatus
{
    public const string PublicUrlProperty = "tunnel.publicUrl";
    public const string TargetProperty = "tunnel.target";

    public static ValueTask PublishAsync(
        ResourceNotificationService notifications,
        IResource resource,
        string state,
        string? publicUrl = null,
        string? target = null
    ) =>
        new(notifications.PublishUpdateAsync(
            resource,
            snapshot => snapshot with
            {
                State = state,
                Urls = publicUrl is { Length: > 0 }
                    ? [new UrlSnapshot("public", publicUrl, IsInternal: false)]
                    : [],
                Properties = Merge(snapshot.Properties, publicUrl, target)
            }
        ));

    static ImmutableArray<ResourcePropertySnapshot> Merge(
        ImmutableArray<ResourcePropertySnapshot> existing,
        string? publicUrl,
        string? target
    )
    {
        var properties = existing
            .Where(x => x.Name is not (PublicUrlProperty or TargetProperty))
            .ToList();

        // A tunnel that has dropped shows no address rather than the dead one it used to have.
        properties.Add(new ResourcePropertySnapshot(PublicUrlProperty, publicUrl ?? ""));

        if (target is { Length: > 0 })
            properties.Add(new ResourcePropertySnapshot(TargetProperty, target));
        else if (existing.FirstOrDefault(x => x.Name == TargetProperty) is { } carried)
            properties.Add(carried);

        return [.. properties];
    }
}
