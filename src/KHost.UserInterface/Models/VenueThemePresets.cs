namespace KHost.UserInterface.Models;

/// <summary>A ready-made venue theme: the four theme colours as <c>#rrggbb</c>.</summary>
public sealed record VenueThemePreset(string Id, string Name, string Primary, string Highlight, string Text, string Shadow);

/// <summary>The themes a host can start a venue from.</summary>
/// <remarks>Picking one only fills the venue's four theme colours; nothing records which was picked, so
/// editing any colour afterwards leaves the venue on a theme of its own.</remarks>
public static class VenueThemePresets
{
    public static readonly IReadOnlyList<VenueThemePreset> All =
    [
        new("neon", "Neon Dive", "#ff2e88", "#22e0ff", "#f6f0ff", "#12061f"),
        new("pub", "Irish Pub", "#1f8a4c", "#f2c14e", "#fff6e5", "#0d1a12"),
        new("tiki", "Tiki Lounge", "#ff7a2f", "#2fd3c3", "#fff3e0", "#1d0f0a"),
        new("saloon", "Honky-Tonk Saloon", "#b5532a", "#e8c07d", "#fbeedd", "#1c120c"),
        new("wine", "Wine Bar", "#8e1b3a", "#e6b8a2", "#f7ebe6", "#160a0e"),
        new("sports", "Sports Bar", "#1f5fd6", "#ffb000", "#ffffff", "#0a1224"),
        new("diner", "Retro Diner", "#e0283a", "#39c6d8", "#fff8ec", "#1a1416"),
        new("midnight", "Midnight Lounge", "#6c63ff", "#c8b6ff", "#ecebff", "#0b0a1a"),
        new("beach", "Beach Shack", "#ff6f61", "#ffd166", "#fffaf0", "#0b2233"),
        new("holiday", "Holiday Party", "#c8102e", "#f5c242", "#ffffff", "#0d1a10"),
    ];

    /// <summary>The preset whose four colours these are, as lowercase <c>#rrggbb</c>, or null.</summary>
    public static VenueThemePreset? Matching(string? primary, string? highlight, string? text, string? shadow)
        => All.FirstOrDefault(p => p.Primary == primary && p.Highlight == highlight && p.Text == text && p.Shadow == shadow);
}
