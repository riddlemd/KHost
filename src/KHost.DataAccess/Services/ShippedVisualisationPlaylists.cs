using KHost.Abstractions.Models;

namespace KHost.DataAccess.Services;

/// <summary>The visualisation playlists every install ships. Both ids carry the built-in prefix, so
/// neither can be deleted; both can be renamed, shuffled and edited.</summary>
public static class ShippedVisualisationPlaylists
{
    /// <summary>The ambient scenes drawn with shapes; every new venue starts on it.</summary>
    public static readonly Guid BasicId = VisualisationPlaylist.DefaultId;

    public const string BasicName = "Basic Backgrounds";

    /// <summary>The ambient scenes drawn by a shader, which need WebGL.</summary>
    public static readonly Guid AdvancedId = new("00000000-0000-0000-0000-000000000002");

    public const string AdvancedName = "Advanced Backgrounds";

    /// <summary>The shader-drawn ambient scenes, by the names <c>screen-ui/eq-visualisers.js</c>
    /// keys <c>AMBIENT_FIELDS</c> by; a test holds them together. Every other ambient scene is basic.</summary>
    public static readonly IReadOnlyList<string> AdvancedScenes =
    [
        "ambient-clouds",
        "ambient-nebula",
        "ambient-aurora",
        "ambient-smoke",
        "ambient-ink",
        "ambient-gasgiant",
        "ambient-haze",
        "ambient-storm",
        "ambient-silk",
    ];
}
