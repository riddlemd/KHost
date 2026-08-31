using System.Text.Json;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Messaging;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Services;

public class ThemeServiceTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "khost-themes-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCache _cache = new();
    private readonly IMessageBroker _broker = new MessageBroker(NullLogger<MessageBroker>.Instance);

    public ThemeServiceTests()
    {
        var themes = Path.Combine(_webRoot, "css", "themes");
        Directory.CreateDirectory(themes);

        File.WriteAllText(Path.Combine(themes, "grape.css"),
            ":root { --kh-primary: #5D2B90; --kh-text: #DDD4F0; --kh-border: rgba(93, 43, 144, 0.28); }");
        File.WriteAllText(Path.Combine(themes, "cherry.css"),
            ":root { --kh-primary: #dc2626; --kh-text: #fce7e7; }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);

        GC.SuppressFinalize(this);
    }

    private ThemeService CreateService()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(_webRoot);

        return new ThemeService(_cache, env, NullLogger<ThemeService>.Instance, _broker);
    }

    private async Task<ThemeService> CreateInitialisedAsync()
    {
        var service = CreateService();
        await service.InitializeAsync();
        return service;
    }

    [Fact]
    public async Task InitializeAsync_DiscoversBuiltInThemesFromTheWebRoot()
    {
        var service = await CreateInitialisedAsync();

        Assert.Equal(["cherry", "grape"], service.AllThemes.Select(t => t.Id));
        Assert.All(service.AllThemes, t => Assert.True(t.IsBuiltIn));
        Assert.Equal("grape", service.CurrentTheme);
    }

    [Fact]
    public async Task AvailableThemes_ExcludesADisabledTheme()
    {
        var service = await CreateInitialisedAsync();

        await service.SetEnabledAsync("cherry", false);

        Assert.DoesNotContain("cherry", service.AvailableThemes);
        Assert.Contains("cherry", service.AllThemes.Select(t => t.Id));
        Assert.False(service.Read("cherry")!.IsEnabled);
    }

    [Fact]
    public async Task SetEnabledAsync_ForTheThemeInUse_IsRefused()
    {
        var service = await CreateInitialisedAsync();

        await service.SetEnabledAsync("grape", false);

        Assert.True(service.Read("grape")!.IsEnabled);
        Assert.Contains("grape", service.AvailableThemes);
    }

    [Fact]
    public async Task SaveAsync_CannotDisableTheThemeInUse()
    {
        var service = await CreateInitialisedAsync();
        var clone = await service.CloneAsync("cherry");
        await service.SetThemeAsync(clone!.Id);

        await service.SaveAsync(new ThemeDefinition
        {
            Id = clone.Id,
            Name = clone.Name,
            IsEnabled = false,
            Variables = clone.Variables
        });

        Assert.True(service.Read(clone.Id)!.IsEnabled);
        Assert.Contains(clone.Id, service.AvailableThemes);
    }

    [Fact]
    public async Task SetEnabledAsync_DisablingAndReEnabling_SurvivesARestart()
    {
        var service = await CreateInitialisedAsync();
        await service.SetEnabledAsync("cherry", false);

        var restarted = await CreateInitialisedAsync();

        Assert.DoesNotContain("cherry", restarted.AvailableThemes);

        await restarted.SetEnabledAsync("cherry", true);

        Assert.Contains("cherry", (await CreateInitialisedAsync()).AvailableThemes);
    }

    [Fact]
    public async Task SetEnabledAsync_Announces()
    {
        var service = await CreateInitialisedAsync();
        var announced = new TaskCompletionSource();
        using var subscription = _broker.Subscribe<ThemesChanged>(_ => announced.TrySetResult());

        await service.SetEnabledAsync("cherry", false);

        Assert.Same(announced.Task, await Task.WhenAny(announced.Task, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task SetThemeAsync_ForADisabledTheme_IsIgnored()
    {
        var service = await CreateInitialisedAsync();
        await service.SetEnabledAsync("cherry", false);

        await service.SetThemeAsync("cherry");

        Assert.Equal("grape", service.CurrentTheme);
    }

    [Fact]
    public async Task InitializeAsync_WhenTheSavedThemeIsDisabled_DoesNotSelectIt()
    {
        var service = await CreateInitialisedAsync();
        await service.SetThemeAsync("cherry");
        await service.SetThemeAsync("grape");
        await service.SetEnabledAsync("cherry", false);
        await _cache.SaveAsync("theme", "cherry");

        var restarted = await CreateInitialisedAsync();

        Assert.NotEqual("cherry", restarted.CurrentTheme);
    }

    [Fact]
    public async Task ReadVariablesAsync_ForABuiltIn_ReadsItsCompiledStylesheet()
    {
        var service = await CreateInitialisedAsync();

        var values = await service.ReadVariablesAsync("cherry");

        Assert.Equal("#dc2626", values["--kh-primary"]);
        // Absent from that stylesheet, so it has to arrive from the catalog rather than go missing.
        Assert.Equal(ThemeVariableCatalog.FallbackFor("--kh-radius"), values["--kh-radius"]);
    }

    [Fact]
    public async Task CloneAsync_FromABuiltIn_CopiesItsValuesIntoAnEditableTheme()
    {
        var service = await CreateInitialisedAsync();

        var clone = await service.CloneAsync("cherry");

        Assert.NotNull(clone);
        Assert.False(clone!.IsBuiltIn);
        Assert.Equal("Cherry (copy)", clone.Name);
        Assert.Equal("cherry-copy", clone.Id);
        Assert.Equal("#dc2626", clone.Variables["--kh-primary"]);
        Assert.Contains(clone.Id, service.AvailableThemes);
    }

    [Fact]
    public async Task CloneAsync_Twice_ProducesDistinctThemes()
    {
        var service = await CreateInitialisedAsync();

        var first = await service.CloneAsync("cherry");
        var second = await service.CloneAsync("cherry");

        Assert.NotEqual(first!.Id, second!.Id);
        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal(4, service.AllThemes.Count);
    }

    [Fact]
    public async Task CloneAsync_ForAnUnknownTheme_ReturnsNull()
    {
        var service = await CreateInitialisedAsync();

        Assert.Null(await service.CloneAsync("nope"));
    }

    [Fact]
    public async Task SaveAsync_ForABuiltIn_IsRejected()
    {
        var service = await CreateInitialisedAsync();

        await service.SaveAsync(new ThemeDefinition { Id = "grape", Name = "Hijacked", IsBuiltIn = true });

        // Asserting on Read alone would pass even if a custom row were stored, because built-ins
        // are listed first — so check nothing was added at all.
        Assert.Equal(2, service.AllThemes.Count);
        Assert.DoesNotContain(service.AllThemes, t => t.Name == "Hijacked");
    }

    [Fact]
    public async Task SaveAsync_DropsValuesThatCouldEscapeTheStylesheet()
    {
        var service = await CreateInitialisedAsync();

        await service.SaveAsync(new ThemeDefinition
        {
            Id = "custom",
            Name = "Custom",
            Variables = new Dictionary<string, string>
            {
                ["--kh-primary"] = "#123456",
                ["--kh-bg"] = "red; } body { display: none",
                ["--not-a-theme-variable"] = "#000000"
            }
        });

        var saved = service.Read("custom")!;

        Assert.Equal("#123456", saved.Variables["--kh-primary"]);
        Assert.False(saved.Variables.ContainsKey("--kh-bg"));
        Assert.False(saved.Variables.ContainsKey("--not-a-theme-variable"));
    }

    [Fact]
    public async Task SaveAsync_AnExistingTheme_UpdatesRatherThanAddsIt()
    {
        var service = await CreateInitialisedAsync();
        var clone = await service.CloneAsync("cherry");

        await service.SaveAsync(new ThemeDefinition
        {
            Id = clone!.Id,
            Name = "Renamed",
            Variables = new Dictionary<string, string> { ["--kh-primary"] = "#000000" }
        });

        Assert.Equal(3, service.AllThemes.Count);
        Assert.Equal("Renamed", service.Read(clone.Id)!.Name);
    }

    [Fact]
    public async Task DeleteAsync_ForABuiltIn_IsRejected()
    {
        var service = await CreateInitialisedAsync();

        await service.DeleteAsync("grape");

        Assert.Contains("grape", service.AllThemes.Select(t => t.Id));
    }

    [Fact]
    public async Task DeleteAsync_ForTheThemeInUse_FallsBackToAnother()
    {
        var service = await CreateInitialisedAsync();
        var clone = await service.CloneAsync("cherry");
        await service.SetThemeAsync(clone!.Id);

        await service.DeleteAsync(clone.Id);

        Assert.Equal("grape", service.CurrentTheme);
        Assert.DoesNotContain(clone.Id, service.AllThemes.Select(t => t.Id));
    }

    [Fact]
    public async Task CurrentThemeHref_ForABuiltIn_PointsAtTheStaticFile()
    {
        var service = await CreateInitialisedAsync();

        Assert.Equal("/css/themes/grape.css", service.CurrentThemeHref);
    }

    [Fact]
    public async Task CurrentThemeHref_ForACustomTheme_ChangesWhenItsValuesDo()
    {
        var service = await CreateInitialisedAsync();
        var clone = await service.CloneAsync("cherry");
        await service.SetThemeAsync(clone!.Id);

        var before = service.CurrentThemeHref;

        Assert.StartsWith($"/css/themes/custom/{clone.Id}.css?v=", before);

        var edited = new Dictionary<string, string>(clone.Variables) { ["--kh-primary"] = "#010203" };
        await service.SaveAsync(new ThemeDefinition { Id = clone.Id, Name = clone.Name, Variables = edited });

        Assert.NotEqual(before, service.CurrentThemeHref);
    }

    [Fact]
    public async Task BuildId_WhenTheSlugIsAlreadyTaken_Suffixes()
    {
        var service = await CreateInitialisedAsync();

        Assert.Equal("grape-2", service.BuildId("Grape"));
        Assert.Equal("my-theme", service.BuildId("My Theme!"));
        Assert.Equal("grape", service.BuildId("Grape", ignoreId: "grape"));
    }

    [Fact]
    public async Task DisplayNameFor_UsesTheThemesOwnNameAndTitleCasesABuiltIn()
    {
        var service = await CreateInitialisedAsync();
        await service.SaveAsync(new ThemeDefinition { Id = "night-shift", Name = "Night Shift" });

        Assert.Equal("Night Shift", service.DisplayNameFor("night-shift"));
        Assert.Equal("Grape", service.DisplayNameFor("grape"));
    }

    // Mirrors JsonFileCacheService's own options, so what these tests see written is the shape that
    // reaches cache/themes.json.
    [Fact]
    public async Task SaveAsync_DropsAColourThatIsNotAHexLiteral()
    {
        var service = await CreateInitialisedAsync();

        await service.SaveAsync(new ThemeDefinition
        {
            Id = "named",
            Name = "Named",
            Variables = new Dictionary<string, string>(ThemeVariableCatalog.Defaults())
            {
                ["--kh-primary"] = "rebeccapurple",
                ["--kh-radius"] = "10px"
            }
        });

        var saved = service.Read("named")!;

        Assert.False(saved.Variables.ContainsKey("--kh-primary"));
        // A non-colour field is untouched by the rule.
        Assert.Equal("10px", saved.Variables["--kh-radius"]);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistTheComputedEnabledFlag()
    {
        var service = await CreateInitialisedAsync();

        await service.SaveAsync(new ThemeDefinition { Id = "night", Name = "Night", IsEnabled = true });

        using var document = JsonDocument.Parse(_cache.Raw("themes")!);
        var stored = document.RootElement.GetProperty("custom")[0];

        Assert.False(stored.TryGetProperty("isEnabled", out _));
        Assert.Equal("night", stored.GetProperty("id").GetString());
    }

    [Fact]
    public async Task InitializeAsync_IgnoresAnEnabledFlagLeftInTheStoredFile()
    {
        // What a file written before the flag was dropped looks like: the disabled list is the only
        // authority, so a stale isEnabled must not switch a theme off.
        _cache.Seed("themes",
            """{"custom":[{"id":"night","name":"Night","isEnabled":false,"variables":{}}],"disabled":[]}""");

        var service = await CreateInitialisedAsync();

        Assert.True(service.Read("night")!.IsEnabled);
        Assert.Contains("night", service.AvailableThemes);

        // And the stale field is dropped on the next write rather than carried forward.
        await service.SetEnabledAsync("cherry", false);

        Assert.DoesNotContain("isEnabled", _cache.Raw("themes"));
    }

    /// <summary>
    /// AllThemes walks the stored lists without taking the write lock, so a save must publish a new
    /// store rather than grow the one a render is midway through.
    /// </summary>
    [Fact]
    public async Task AllThemes_CanBeReadWhileThemesAreBeingSaved()
    {
        var service = await CreateInitialisedAsync();
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                _ = service.AllThemes.Count(t => t.IsEnabled);
        });

        for (var i = 0; i < 200; i++)
            await service.SaveAsync(new ThemeDefinition { Id = $"theme-{i}", Name = $"Theme {i}" });

        stop.Cancel();
        await reader;
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, string> _store = [];

        public string? Raw(string key) => _store.GetValueOrDefault(key);

        public void Seed(string key, string json) => _store[key] = json;

        public Task<T?> LoadAsync<T>(string key)
            => Task.FromResult(_store.TryGetValue(key, out var json)
                ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)
                : default);

        public Task SaveAsync<T>(string key, T state)
        {
            _store[key] = JsonSerializer.Serialize(state, JsonSerializerOptions.Web);
            return Task.CompletedTask;
        }
    }
}
