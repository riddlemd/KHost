namespace KHost.UserInterface.Messaging;

/// <summary>
/// The set of themes moved — one was created, edited, cloned, deleted, enabled or disabled.
/// Distinct from <see cref="ThemeChanged"/>, which says the console is now painted differently.
/// </summary>
public sealed record ThemesChanged;
