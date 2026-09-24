using System.Security.Cryptography;
using System.Text;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Messaging;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services;

public class ThemeService : IThemeService
{
    private const string CacheKey = "theme";
    private const string StoreCacheKey = "themes";
    private const string DefaultTheme = "grape";

    private readonly ICacheService _cacheService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ThemeService> _logger;
    private readonly IMessageBroker _broker;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<string> _builtInIds = [];
    private ThemeStore _store = new();

    public string CurrentTheme { get; private set; } = DefaultTheme;

    public IReadOnlyList<string> AvailableThemes
        => [.. AllThemes.Where(t => t.IsEnabled).Select(t => t.Id)];

    public IReadOnlyList<ThemeDefinition> AllThemes
    {
        get
        {
            // One read of the field: a write swaps the whole store, so reading it again partway
            // through would pair a new custom list with an old disabled list.
            var store = _store;
            var disabled = store.Disabled.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var builtIns = _builtInIds.Select(id => new ThemeDefinition
            {
                Id = id,
                Name = TitleCase(id),
                IsBuiltIn = true,
                IsEnabled = !disabled.Contains(id)
            });

            var custom = store.Custom.Select(t => new ThemeDefinition
            {
                Id = t.Id,
                Name = t.Name,
                IsBuiltIn = false,
                IsEnabled = !disabled.Contains(t.Id),
                Variables = t.Variables
            });

            return [.. builtIns, .. custom];
        }
    }

    public string CurrentThemeHref
    {
        get
        {
            var custom = _store.Custom.FirstOrDefault(t => IdMatches(t.Id, CurrentTheme));

            // A custom theme's URL never changes on its own, so without the content stamp an edit
            // would repaint nothing until the browser dropped the stylesheet from cache.
            return custom is null
                ? $"/css/themes/{CurrentTheme}.css"
                : $"/css/themes/custom/{custom.Id}.css?v={Stamp(custom)}";
        }
    }

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
            _logger.LogWarning("Themes folder not found at {ThemesPath}; only custom themes will be offered", themesPath);
            _builtInIds = [];
        }
        else
        {
            _builtInIds = [.. Directory.GetFiles(themesPath, "*.css")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];
        }

        _store = await _cacheService.LoadAsync<ThemeStore>(StoreCacheKey) ?? new ThemeStore();

        var saved = await _cacheService.LoadAsync<string>(CacheKey);
        var available = AvailableThemes;
        _logger.LogDebug("Theme cache load: saved={Saved} builtIn={BuiltIn} custom={Custom}",
            saved, _builtInIds.Count, _store.Custom.Count);

        if (saved != null && available.Contains(saved))
            CurrentTheme = saved;
        else if (!available.Contains(DefaultTheme) && available.Count > 0)
            CurrentTheme = available[0];
    }

    public async Task SetThemeAsync(string themeName)
    {
        if (!AvailableThemes.Contains(themeName))
        {
            _logger.LogWarning("SetThemeAsync ignored — theme {ThemeName} is not available", themeName);
            return;
        }

        var previous = CurrentTheme;
        CurrentTheme = themeName;
        await _cacheService.SaveAsync(CacheKey, themeName);
        _logger.LogInformation("Theme changed: {From} -> {To}", previous, themeName);
        _broker.Announce(new ThemeChanged());
    }

    public ThemeDefinition? Read(string id)
        => AllThemes.FirstOrDefault(t => IdMatches(t.Id, id));

    public async Task<Dictionary<string, string>> ReadVariablesAsync(string id)
    {
        var values = ThemeVariableCatalog.Defaults();

        var custom = _store.Custom.FirstOrDefault(t => IdMatches(t.Id, id));

        if (custom is not null)
        {
            foreach (var (key, value) in custom.Variables)
                values[key] = value;

            return values;
        }

        // Only a discovered built-in reaches the filesystem, so the id can never steer the path.
        var builtIn = _builtInIds.FirstOrDefault(t => IdMatches(t, id));

        if (builtIn is null)
            return values;

        var path = Path.Combine(_env.WebRootPath, "css", "themes", builtIn + ".css");

        if (!File.Exists(path))
        {
            _logger.LogWarning("Theme stylesheet missing for built-in {ThemeId}; seeding from defaults", builtIn);
            return values;
        }

        foreach (var (key, value) in ThemeCss.Parse(await File.ReadAllTextAsync(path)))
            values[key] = value;

        return values;
    }

    public async Task SaveAsync(ThemeDefinition theme)
    {
        if (theme.IsBuiltIn)
        {
            _logger.LogWarning("SaveAsync ignored — {ThemeId} is a built-in theme", theme.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name))
        {
            _logger.LogWarning("SaveAsync ignored — a theme needs both an id and a name");
            return;
        }

        // The editor's Enabled box must not switch off the theme on screen; that is the same
        // stranding SetEnabledAsync refuses, reached through the other door.
        var enabled = theme.IsEnabled || IdMatches(CurrentTheme, theme.Id);

        var stored = new ThemeDefinition
        {
            Id = theme.Id,
            Name = theme.Name.Trim(),
            IsBuiltIn = false,
            Variables = theme.Variables
                .Where(v => ThemeVariableCatalog.Find(v.Key) is { } field && ThemeCss.IsValidFor(field, v.Value))
                .ToDictionary(v => v.Key, v => v.Value.Trim(), StringComparer.Ordinal)
        };

        await CommitAsync(store =>
        {
            // Rebuilt rather than mutated: AllThemes walks these lists without taking the lock, so
            // one must never grow under a reader midway through a render.
            var custom = new List<ThemeDefinition>(store.Custom);
            var index = custom.FindIndex(t => IdMatches(t.Id, stored.Id));

            if (index >= 0)
                custom[index] = stored;
            else
                custom.Add(stored);

            var disabled = store.Disabled.Where(d => !IdMatches(d, stored.Id)).ToList();

            if (!enabled)
                disabled.Add(stored.Id);

            return new ThemeStore { Custom = custom, Disabled = disabled };
        });

        _logger.LogInformation("Theme saved: {ThemeId}", theme.Id);
        Announce(theme.Id);
    }

    public async Task DeleteAsync(string id)
    {
        var custom = _store.Custom.FirstOrDefault(t => IdMatches(t.Id, id));

        if (custom is null)
        {
            _logger.LogWarning("DeleteAsync ignored — {ThemeId} is not a custom theme", id);
            return;
        }

        await CommitAsync(store => new ThemeStore
        {
            Custom = [.. store.Custom.Where(t => !IdMatches(t.Id, id))],
            Disabled = [.. store.Disabled.Where(d => !IdMatches(d, id))]
        });

        _logger.LogInformation("Theme deleted: {ThemeId}", id);

        if (IdMatches(CurrentTheme, id))
            await FallBackAsync();

        _broker.Announce(new ThemesChanged());
    }

    public async Task SetEnabledAsync(string id, bool enabled)
    {
        // Guards live inside the lock, not read ahead of it: two concurrent disables racing on the
        // same theme would otherwise both pass on the pre-commit state and the second one — already
        // past the check — would redundantly disable and re-announce a theme the first one settled.
        var committed = await CommitAsync(store =>
        {
            var theme = Read(id);

            if (theme is null || theme.IsEnabled == enabled)
                return null;

            // Both guards exist so the pickers can never be emptied out from underneath a running
            // show: the theme on screen has to stay reachable, and something has to remain to
            // switch to.
            if (!enabled && IdMatches(CurrentTheme, id))
            {
                _logger.LogWarning("SetEnabledAsync refused — {ThemeId} is the theme in use", id);
                return null;
            }

            if (!enabled && AvailableThemes.Count <= 1)
            {
                _logger.LogWarning("SetEnabledAsync refused — {ThemeId} is the last enabled theme", id);
                return null;
            }

            var disabled = store.Disabled.Where(d => !IdMatches(d, id)).ToList();

            if (!enabled)
                disabled.Add(theme.Id);

            // Custom is handed on by reference: nothing mutates a stored list once it is published.
            return new ThemeStore { Custom = store.Custom, Disabled = disabled };
        });

        if (!committed)
            return;

        _logger.LogInformation("Theme {ThemeId} {State}", id, enabled ? "enabled" : "disabled");
        _broker.Announce(new ThemesChanged());
    }

    public async Task<ThemeDefinition?> CloneAsync(string sourceId)
    {
        var source = Read(sourceId);

        if (source is null)
            return null;

        var name = BuildCopyName(source.Name);

        var clone = new ThemeDefinition
        {
            Id = BuildId(name),
            Name = name,
            IsBuiltIn = false,
            IsEnabled = true,
            Variables = await ReadVariablesAsync(sourceId)
        };

        await SaveAsync(clone);
        return clone;
    }

    public string BuildId(string name, string? ignoreId = null)
    {
        var slug = Slugify(name);

        var taken = AllThemes
            .Where(t => ignoreId is null || !IdMatches(t.Id, ignoreId))
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(slug))
            return slug;

        for (var attempt = 2; attempt <= 1000; attempt++)
        {
            var candidate = $"{slug}-{attempt}";

            if (!taken.Contains(candidate))
                return candidate;
        }

        return $"{slug}-{Guid.NewGuid():N}"[..40];
    }

    public string DisplayNameFor(string id)
        => Read(id)?.Name ?? TitleCase(id);

    private void Announce(string themeId)
    {
        _broker.Announce(new ThemesChanged());

        if (IdMatches(CurrentTheme, themeId))
            _broker.Announce(new ThemeChanged());
    }

    /// <summary>Rebuilds the theme store under the lock and publishes it, or no-ops if <paramref
    /// name="build"/> returns null.</summary>
    /// <remarks>The callback runs while the lock is held, so a guard inside it — unlike one read
    /// ahead of <see cref="_lock"/> — sees the result of whatever the previous holder committed.</remarks>
    private async Task<bool> CommitAsync(Func<ThemeStore, ThemeStore?> build)
    {
        await _lock.WaitAsync();

        try
        {
            var next = build(_store);

            if (next is null)
                return false;

            await _cacheService.SaveAsync(StoreCacheKey, next);
            _store = next;
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task FallBackAsync()
    {
        var available = AvailableThemes;
        var next = available.Contains(DefaultTheme) ? DefaultTheme : available.FirstOrDefault();

        if (next is null)
            return;

        CurrentTheme = next;
        await _cacheService.SaveAsync(CacheKey, next);
        _logger.LogInformation("Theme fell back to {ThemeId}", next);
        _broker.Announce(new ThemeChanged());
    }

    private string BuildCopyName(string baseName)
    {
        var taken = AllThemes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= 1000; attempt++)
        {
            var candidate = attempt == 1 ? $"{baseName} (copy)" : $"{baseName} (copy {attempt})";

            if (!taken.Contains(candidate))
                return candidate;
        }

        return $"{baseName} (copy {Guid.NewGuid():N})";
    }

    private static bool IdMatches(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Slugify(string name)
    {
        var builder = new StringBuilder();

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length == 0 ? "theme" : slug;
    }

    private static string TitleCase(string id)
        => string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string Stamp(ThemeDefinition theme)
    {
        var payload = string.Join(';', theme.Variables.OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => $"{v.Key}={v.Value}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..8].ToLowerInvariant();
    }
}
