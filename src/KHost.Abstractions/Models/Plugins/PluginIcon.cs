namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// What a manifest's <see cref="PluginManifest.Icon"/> may say. A plugin either names a Bootstrap
/// Icons glyph or asks for its own image, and the image's name is fixed here rather than given in
/// the manifest: the host joins this to the plugin's own folder, so nothing a plugin writes can
/// steer the path it reads.
/// </summary>
public static class PluginIcon
{
    /// <summary>The value <c>Icon</c> takes to mean "use the file shipped beside my manifest".</summary>
    public const string ImageSpecifier = "image";

    public const string FileName = "plugin.icon.png";

    /// <summary>
    /// Both dimensions, in pixels. The row draws it small; the cap is what stops a plugin handing
    /// the console a photograph to scale down on every render.
    /// </summary>
    public const int MaxDimension = 128;
}
