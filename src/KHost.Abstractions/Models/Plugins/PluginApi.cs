namespace KHost.Abstractions.Models.Plugins;

/// <summary>The range of plugin API versions this host runs.</summary>
/// <remarks>A plugin declares the version it was built against in
/// <see cref="PluginManifest.ApiVersion"/>; this host runs, installs and offers it only when that
/// value lies between <see cref="MinimumVersion"/> and <see cref="CurrentVersion"/>, both
/// inclusive.</remarks>
public static class PluginApi
{
    /// <summary>The newest plugin API this host offers. It moves whenever the contracts gain
    /// anything a plugin could call or implement, so a plugin built against something newer is
    /// refused with a reason rather than failing at run time.</summary>
    public const int CurrentVersion = 4;

    /// <summary>The oldest plugin API this host still runs. It moves only on a break: a change to
    /// something a plugin calls or implements, or a removal. A break moves
    /// <see cref="CurrentVersion"/> too.</summary>
    public const int MinimumVersion = 1;
}
