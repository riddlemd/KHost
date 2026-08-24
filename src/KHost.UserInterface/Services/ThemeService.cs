using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services;

public class ThemeService : IThemeService
{
    private const string CacheKey = "theme";
    private const string DefaultTheme = "grape";

    private readonly ICacheService _cacheService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ThemeService> _logger;
    private readonly IMessageBroker _broker;


    public string CurrentTheme { get; private set; } = DefaultTheme;
    public IReadOnlyList<string> AvailableThemes { get; private set; } = [];

    public ThemeService(ICacheService cacheService, IWebHostEnvironment env, ILogger<ThemeService> logger,
        IMessageBroker broker)
    {
        _cacheService = cacheService;
        _env = env;
        _logger = logger;
        _broker = broker;
    }

    public async Task InitializeAsync()
    {
        var themesPath = Path.Combine(_env.WebRootPath, "css", "themes");

        if (!Directory.Exists(themesPath))
        {
            _logger.LogWarning("Themes folder not found at {ThemesPath}; AvailableThemes will be empty", themesPath);
            AvailableThemes = [];
        }
        else
        {
            AvailableThemes = Directory.GetFiles(themesPath, "*.css")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(t => t)
                .ToList();
        }

        var saved = await _cacheService.LoadAsync<string>(CacheKey);
        _logger.LogDebug("Theme cache load: saved={Saved} available={Count}", saved, AvailableThemes.Count);

        if (saved != null && AvailableThemes.Contains(saved))
            CurrentTheme = saved;
        else if (!AvailableThemes.Contains(DefaultTheme) && AvailableThemes.Count > 0)
            CurrentTheme = AvailableThemes[0];
    }

    public async Task SetThemeAsync(string themeName)
    {
        if (!AvailableThemes.Contains(themeName))
        {
            _logger.LogWarning("SetThemeAsync ignored — theme {ThemeName} not in AvailableThemes", themeName);
            return;
        }

        var previous = CurrentTheme;
        CurrentTheme = themeName;
        await _cacheService.SaveAsync(CacheKey, themeName);
        _logger.LogInformation("Theme changed: {From} -> {To}", previous, themeName);
        _ = _broker.PublishAsync(new ThemeChanged());
    }
}
