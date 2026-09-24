namespace KHost.Abstractions.Models.Plugins;

/// <summary>What <see cref="PluginManifest.Icon"/> may say: a glyph, or its own image.</summary>
public static class PluginIcon
{
    /// <summary>The value <c>Icon</c> takes to mean "use the file shipped beside my manifest".</summary>
    public const string ImageSpecifier = "image";

    /// <summary>The file name a plugin must ship its icon under, beside its manifest, when
    /// <see cref="PluginManifest.Icon"/> is <see cref="ImageSpecifier"/>.</summary>
    public const string FileName = "plugin.icon.png";

    /// <summary>Both dimensions in pixels, so a plugin cannot hand over a photograph to scale.</summary>
    public const int MaxDimension = 128;
}
