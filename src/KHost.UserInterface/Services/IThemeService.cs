namespace KHost.UserInterface.Services;

public interface IThemeService
{
    string CurrentTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }
    event EventHandler StateChanged;
    Task InitializeAsync();
    Task SetThemeAsync(string themeName);
}
