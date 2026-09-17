namespace KHost.UserInterface.Messaging;

/// <summary>The set of themes moved: created, edited, cloned, deleted, enabled or disabled.</summary>
/// <remarks>Distinct from <see cref="ThemeChanged"/>, which says the console now looks different.</remarks>
public sealed record ThemesChanged;
