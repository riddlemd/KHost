using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Hosting;

namespace KHost.UserInterface.Services;

public class ThemeService : IThemeService
{
    private const string CacheKey = "theme";
    private const string DefaultTheme = "grape";

    private readonly ICacheService _cacheService;
    private readonly IWebHostEnvironment _env;

    public event EventHandler? StateChanged;

    public string CurrentTheme { get; private set; } = DefaultTheme;
    public IReadOnlyList<string> AvailableThemes { get; private set; } = [];

    public ThemeService(ICacheService cacheService, IWebHostEnvironment env)
    {
        _cacheService = cacheService;
        _env = env;
    }

    public async Task InitializeAsync()
    {
        var themesPath = Path.Combine(_env.WebRootPath, "css", "themes");
        AvailableThemes = Directory.Exists(themesPath)
            ? Directory.GetFiles(themesPath, "*.css")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(t => t)
                .ToList()
            : [];

        var saved = await _cacheService.LoadAsync<string>(CacheKey);
        if (saved != null && AvailableThemes.Contains(saved))
            CurrentTheme = saved;
        else if (!AvailableThemes.Contains(DefaultTheme) && AvailableThemes.Count > 0)
            CurrentTheme = AvailableThemes[0];
    }

    public async Task SetThemeAsync(string themeName)
    {
        if (!AvailableThemes.Contains(themeName)) return;
        CurrentTheme = themeName;
        await _cacheService.SaveAsync(CacheKey, themeName);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
