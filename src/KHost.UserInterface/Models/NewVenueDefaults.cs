using KHost.Abstractions.Models;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Models;

/// <summary>What a venue starts with when it is added, from App Settings.</summary>
public sealed record NewVenueDefaults(Guid VisualisationPlaylistId, Guid? PlaceholderImageMediaId, ImageScaling? PlaceholderImageScaling = null)
{
    public static NewVenueDefaults From(AppSettings settings)
        => new(settings.NewVenueBackgrounds.PlaylistId(), settings.NewVenuePlaceholderImageId, settings.NewVenuePlaceholderImageScaling);
}
