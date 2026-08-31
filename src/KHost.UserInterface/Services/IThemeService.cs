using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public interface IThemeService
{
    string CurrentTheme { get; }

    /// <summary>Ids a host may switch to — enabled only, so the pickers hide what the manager disabled.</summary>
    IReadOnlyList<string> AvailableThemes { get; }

    /// <summary>Every theme, enabled or not, built-in first. The manager page's list.</summary>
    IReadOnlyList<ThemeDefinition> AllThemes { get; }

    /// <summary>Stylesheet URL for the current theme; built-ins are static files, custom ones are rendered.</summary>
    string CurrentThemeHref { get; }

    Task InitializeAsync();
    Task SetThemeAsync(string themeName);

    ThemeDefinition? Read(string id);

    /// <summary>Resolves a theme's editable values, parsing the compiled stylesheet for a built-in.</summary>
    Task<Dictionary<string, string>> ReadVariablesAsync(string id);

    /// <summary>Creates or updates a custom theme. Built-ins are read-only and are rejected.</summary>
    Task SaveAsync(ThemeDefinition theme);

    Task DeleteAsync(string id);

    Task SetEnabledAsync(string id, bool enabled);

    /// <summary>Copies any theme, built-in or not, into a new editable one.</summary>
    Task<ThemeDefinition?> CloneAsync(string sourceId);

    /// <summary>A unique, filename-safe id for a display name.</summary>
    string BuildId(string name, string? ignoreId = null);

    string DisplayNameFor(string id);
}
