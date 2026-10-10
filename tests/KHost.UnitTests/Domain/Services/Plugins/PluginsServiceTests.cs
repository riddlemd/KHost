using Microsoft.Extensions.Configuration;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginsServiceTests
{
    private readonly ILogger<PluginsService> _logger = Substitute.For<ILogger<PluginsService>>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IPluginRegistry _registry = Substitute.For<IPluginRegistry>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PluginSettingsConfiguration _settings = new();
    private readonly PluginsService _service;

    private PluginsState? _savedState;

    public PluginsServiceTests()
    {
        _cache.LoadAsync<PluginsState>(PluginsState.CacheKey).Returns(_ => _savedState);
        _cache.SaveAsync(PluginsState.CacheKey, Arg.Do<PluginsState>(s => _savedState = s))
            .Returns(Task.CompletedTask);

        _service = new PluginsService(_logger, _cache, _registry, _broker, _settings);
    }

    [Fact]
    public async Task SetEnabledAsync_Enable_AddsIdAndPersists()
    {
        await _service.SetEnabledAsync("khost.youtube", true);

        Assert.Equal(["khost.youtube"], _savedState!.EnabledPluginIds);
    }

    [Fact]
    public async Task SetEnabledAsync_EnableTwice_DoesNotDuplicate()
    {
        await _service.SetEnabledAsync("khost.youtube", true);
        await _service.SetEnabledAsync("khost.youtube", true);

        Assert.Equal(["khost.youtube"], _savedState!.EnabledPluginIds);
    }

    [Fact]
    public async Task SetEnabledAsync_DisableWithDifferentCase_RemovesId()
    {
        _savedState = new PluginsState { EnabledPluginIds = ["khost.YouTube"] };

        await _service.SetEnabledAsync("khost.youtube", false);

        Assert.Empty(_savedState.EnabledPluginIds);
    }

    [Fact]
    public async Task SetEnabledAsync_AnyChange_SetsRestartRequiredAndAnnouncesPluginsChanged()
    {
        var stateChangedCount = 0;
        using var subscription = _broker.Subscribe<PluginsChanged>(_ => stateChangedCount++);

        Assert.False(_service.RestartRequired);

        await _service.SetEnabledAsync("khost.youtube", true);

        Assert.True(_service.RestartRequired);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public async Task ReadEnabledIdsAsync_NoStateFile_ReturnsEmpty()
    {
        var ids = await _service.ReadEnabledIdsAsync();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsValuesForPlugin()
    {
        var values = new Dictionary<string, JsonElement> { ["apiKey"] = JsonSerializer.SerializeToElement("abc") };

        await _service.SaveSettingsAsync("khost.youtube", values);

        Assert.Equal("abc", _savedState!.Settings["khost.youtube"]["apiKey"].GetString());
    }

    // ── settings that apply without a restart ──────────────────────────────────────────

    private const string PluginId = "00700000-0000-4000-8000-000000000799";

    /// <summary>A plugin reads its settings through options this save moves, so nothing waits on a restart.</summary>
    [Fact]
    public async Task SaveSettingsAsync_NeverAsksForARestart()
    {
        await _service.SaveSettingsAsync(PluginId, new() { ["a"] = JsonSerializer.SerializeToElement(1) });

        Assert.False(_service.RestartRequired);
    }

    /// <summary>The configuration is what the running plugin's options bind from, so this is what makes a save reach it.</summary>
    [Fact]
    public async Task SaveSettingsAsync_PutsTheValuesWhereTheRunningPluginsOptionsBindFrom()
    {
        var configuration = new ConfigurationBuilder().Add(_settings).Build();

        await _service.SaveSettingsAsync(PluginId, new() { ["a"] = JsonSerializer.SerializeToElement(7) });

        Assert.Equal("7", configuration[$"{PluginSettingsConfiguration.SectionFor(PluginId)}:a"]);
    }

    /// <summary>A restart a plugin enable asked for is still owed after a settings save.</summary>
    [Fact]
    public async Task SaveSettingsAsync_AfterAnEnable_KeepsTheRestartOwed()
    {
        await _service.SetEnabledAsync(PluginId, true);

        await _service.SaveSettingsAsync(PluginId, new() { ["a"] = JsonSerializer.SerializeToElement(1) });

        Assert.True(_service.RestartRequired);
    }

    [Fact]
    public async Task SaveSettingsAsync_KeepsOtherPluginsSettings()
    {
        await _service.SaveSettingsAsync("khost.youtube", new() { ["a"] = JsonSerializer.SerializeToElement(1) });
        await _service.SaveSettingsAsync("khost.spotify", new() { ["b"] = JsonSerializer.SerializeToElement(2) });

        Assert.Equal(2, _savedState!.Settings.Count);
    }

    [Fact]
    public async Task ReadSettingsAsync_UnknownPlugin_ReturnsEmpty()
    {
        var settings = await _service.ReadSettingsAsync("khost.unknown");

        Assert.Empty(settings);
    }
}
