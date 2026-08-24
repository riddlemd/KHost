namespace KHost.UserInterface.Services;

public interface IThemeService
{
    string CurrentTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }
    Task InitializeAsync();
    Task SetThemeAsync(string themeName);
}
