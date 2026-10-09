using KHost.DataAccess.Services;

namespace KHost.UserInterface.Models;

/// <summary>Which shipped visualisation playlist a new venue starts on.</summary>
public enum VenueBackgrounds
{
    /// <summary>"Basic Backgrounds": scenes drawn with shapes, which any screen can draw.</summary>
    Basic = 0,

    /// <summary>"Advanced Backgrounds": clouds, gas and nebulas drawn by a shader, which needs WebGL.</summary>
    Advanced = 1,
}

public static class VenueBackgroundsExtensions
{
    /// <summary>The shipped playlist a choice names.</summary>
    public static Guid PlaylistId(this VenueBackgrounds backgrounds)
        => backgrounds == VenueBackgrounds.Advanced ? ShippedVisualisationPlaylists.AdvancedId : ShippedVisualisationPlaylists.BasicId;
}
