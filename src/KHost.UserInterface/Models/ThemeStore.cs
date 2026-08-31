namespace KHost.UserInterface.Models;

/// <summary>
/// What the cache holds for themes. Disabled ids cover built-ins too, which is why the flag lives
/// here rather than on the definition: a built-in is discovered fresh from disk on every start and
/// has nowhere of its own to remember that a host switched it off.
/// </summary>
public sealed class ThemeStore
{
    public List<ThemeDefinition> Custom { get; set; } = [];

    public List<string> Disabled { get; set; } = [];
}
