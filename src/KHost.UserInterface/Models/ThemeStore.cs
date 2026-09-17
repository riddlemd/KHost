namespace KHost.UserInterface.Models;

/// <summary>What the cache holds for themes; disabled ids cover built-ins too.</summary>
/// <remarks>A built-in is rediscovered fresh from disk each start, with no place to remember off.</remarks>
public sealed class ThemeStore
{
    public List<ThemeDefinition> Custom { get; set; } = [];

    public List<string> Disabled { get; set; } = [];
}
