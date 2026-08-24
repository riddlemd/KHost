using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk.Messaging.Messages;
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
    private readonly PluginsService _service;

    private PluginsState? _savedState;

    public PluginsServiceTests()
    {
        _cache.LoadAsync<PluginsState>(PluginsState.CacheKey).Returns(_ => _savedState);
        _cache.SaveAsync(PluginsState.CacheKey, Arg.Do<PluginsState>(s => _savedState = s))
            .Returns(Task.CompletedTask);

        _service = new PluginsService(_logger, _cache, _registry, _broker);
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
    public async Task SetEnabledAsync_AnyChange_SetsRestartRequiredAndRaisesStateChanged()
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

    [Fact]
    public async Task SaveSettingsAsync_KeepsOtherPluginsSettings()
    {
        await _service.SaveSettingsAsync("khost.youtube", new() { ["a"] = JsonSerializer.SerializeToElement(1) });
        await _service.SaveSettingsAsync("khost.karafun", new() { ["b"] = JsonSerializer.SerializeToElement(2) });

        Assert.Equal(2, _savedState!.Settings.Count);
    }

    [Fact]
    public async Task ReadSettingsAsync_UnknownPlugin_ReturnsEmpty()
    {
        var settings = await _service.ReadSettingsAsync("khost.unknown");

        Assert.Empty(settings);
    }
}
