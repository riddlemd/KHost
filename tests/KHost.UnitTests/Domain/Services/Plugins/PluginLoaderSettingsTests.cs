using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.Plugins;

public sealed class SamplePluginSettings
{
    public int PageSize { get; set; } = 3;
    public bool Shuffle { get; set; }
    public string Name { get; set; } = "initializer";
}

public sealed class SampleOtherSettings
{
    public int Other { get; set; }
}

public sealed class SamplePlugin : IPlugin<SamplePluginSettings>;

public sealed class SamplePlainPlugin : IPlugin
{
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SampleTwoSettingsPlugin : IPlugin<SamplePluginSettings>, IPlugin<SampleOtherSettings>
{
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SampleForeignSettingsPlugin : IPlugin<BreakMusicService.ServiceOptions>;

public sealed class SampleExtensionWithoutContext(IOptionsMonitor<SamplePluginSettings> settings)
{
    public IOptionsMonitor<SamplePluginSettings> Settings { get; } = settings;
}

public sealed class SampleExtensionWithContext(IPluginContext context, IOptionsMonitor<SamplePluginSettings> settings)
{
    public IPluginContext Context { get; } = context;
    public IOptionsMonitor<SamplePluginSettings> Settings { get; } = settings;
}

/// <summary>A plugin naming its settings with IPlugin&lt;TSettings&gt; gets them as live options.</summary>
public class PluginLoaderSettingsTests
{
    private const string Id = "00700000-0000-4000-8000-000000000a01";

    private readonly PluginSettingsConfiguration _source = new();
    private readonly DiscoveredPlugin _plugin = new() { Directory = "/plugins/sample" };

    private ServiceProvider Register(params Type[] types)
    {
        _source.SetDefinitions(Id, [new PluginSettingDefinition { Key = "pageSize", Type = PluginSettingType.Int, Label = "Page size", Default = JsonSerializer.SerializeToElement(10) }]);
        var section = new ConfigurationBuilder().Add(_source).Build().GetSection(PluginSettingsConfiguration.SectionFor(Id));
        var services = new ServiceCollection();

        PluginLoader.RegisterSettings(services, _plugin, types, typeof(SamplePlugin).Assembly, section);

        services.AddOptions();
        return services.BuildServiceProvider();
    }

    /// <summary>A plugin that reads its settings through options may take no context; handing it one
    /// anyway is an argument nothing accepts, and the host cannot start.</summary>
    [Fact]
    public void CreateExtension_AConstructorWithoutAContext_IsBuiltWithoutOne()
    {
        using var provider = Register(typeof(SamplePlugin));
        var asked = false;

        var built = (SampleExtensionWithoutContext)PluginLoader.CreateExtension(provider, typeof(SampleExtensionWithoutContext), () =>
        {
            asked = true;
            return Substitute.For<IPluginContext>();
        });

        Assert.Equal(10, built.Settings.CurrentValue.PageSize);
        Assert.False(asked);
    }

    [Fact]
    public void CreateExtension_AConstructorTakingAContext_IsHandedThisPluginsOwn()
    {
        using var provider = Register(typeof(SamplePlugin));
        var context = Substitute.For<IPluginContext>();

        var built = (SampleExtensionWithContext)PluginLoader.CreateExtension(provider, typeof(SampleExtensionWithContext), () => context);

        Assert.Same(context, built.Context);
    }

    private void Save(object values) => _source.SetSaved(Id,
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value));

    [Fact]
    public void ASettingsClass_IsBoundToTheManifestDefaults_AndKeepsItsOwnInitializersForTheRest()
    {
        using var provider = Register(typeof(SamplePlugin));

        var settings = provider.GetRequiredService<IOptions<SamplePluginSettings>>().Value;

        Assert.Equal(10, settings.PageSize);
        Assert.Equal("initializer", settings.Name);
    }

    [Fact]
    public void ASave_ReachesTheMonitorsCurrentValue_AndItsListeners()
    {
        using var provider = Register(typeof(SamplePlugin));
        var monitor = provider.GetRequiredService<IOptionsMonitor<SamplePluginSettings>>();
        SamplePluginSettings? heard = null;
        using var _ = monitor.OnChange(s => heard = s);

        Save(new { pageSize = 25, shuffle = true, name = "Toto" });

        Assert.Equal(25, monitor.CurrentValue.PageSize);
        Assert.True(monitor.CurrentValue.Shuffle);
        Assert.Equal("Toto", monitor.CurrentValue.Name);
        Assert.Equal(25, heard?.PageSize);
    }

    [Fact]
    public void APlainEntryPoint_BindsNothing_AndWarnsOfNothing()
    {
        using var provider = Register(typeof(SamplePlainPlugin));

        Save(new { pageSize = 25 });

        Assert.Equal(3, provider.GetRequiredService<IOptions<SamplePluginSettings>>().Value.PageSize);
        Assert.Empty(_plugin.Warnings);
    }

    /// <summary>Two settings classes leave no way to say which a key belongs to, so neither is bound.</summary>
    [Fact]
    public void TwoSettingsClasses_BindNeither_AndSaySo()
    {
        using var provider = Register(typeof(SampleTwoSettingsPlugin));

        Save(new { pageSize = 25 });

        Assert.Equal(3, provider.GetRequiredService<IOptions<SamplePluginSettings>>().Value.PageSize);
        Assert.Contains(_plugin.Warnings, w => w.Contains("more than one settings class"));
    }

    /// <summary>Binding a host type would put this plugin's settings over the host's own options.</summary>
    [Fact]
    public void ASettingsClassFromAnotherAssembly_IsNotBound_AndSaysSo()
    {
        using var provider = Register(typeof(SampleForeignSettingsPlugin));

        Save(new { provider = "Mine" });

        Assert.Null(provider.GetRequiredService<IOptions<BreakMusicService.ServiceOptions>>().Value.Provider);
        Assert.Contains(_plugin.Warnings, w => w.Contains("is not its own"));
    }
}
