using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using System.Text.Json;
using KHost.Domain.Services.Plugins;
using KHost.Domain.Services.Plugins.Secrets;
using KHost.Secrets;
using KHost.UnitTests.Secrets;
using KHost.Domain.Services.QrCodes;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginContextTests
{
    [Fact]
    public void GetSetting_StoredValue_ReturnsIt()
    {
        var plugin = CreatePlugin(stored: new() { ["apiKey"] = JsonSerializer.SerializeToElement("abc123") });

        Assert.Equal("abc123", plugin.GetSetting<string>("apiKey"));
    }

    [Fact]
    public void GetSetting_KeyDiffersOnlyByCase_ReturnsStoredValue()
    {
        var plugin = CreatePlugin(stored: new() { ["apiKey"] = JsonSerializer.SerializeToElement("abc123") });

        Assert.Equal("abc123", plugin.GetSetting<string>("ApiKey"));
    }

    [Fact]
    public void GetSetting_MissingValueWithManifestDefault_ReturnsDefault()
    {
        var plugin = CreatePlugin(defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(25, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void GetSetting_StoredValueOverridesManifestDefault_ReturnsStored()
    {
        var plugin = CreatePlugin(
            stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement(50) },
            defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(50, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void GetSetting_MissingValueNoDefault_ReturnsTypeDefault()
    {
        var plugin = CreatePlugin();

        Assert.Null(plugin.GetSetting<string>("Unset"));
        Assert.Equal(0, plugin.GetSetting<int>("Unset"));
    }

    [Fact]
    public void GetSetting_StoredValueOfWrongType_ReturnsTypeDefault()
    {
        var plugin = CreatePlugin(stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement("not a number") });

        Assert.Equal(0, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void BindSettings_StoredValues_MapToProperties()
    {
        var plugin = CreatePlugin(stored: new()
        {
            ["name"] = JsonSerializer.SerializeToElement("stored-name"),
            ["pageSize"] = JsonSerializer.SerializeToElement(50),
        });

        var settings = plugin.BindSettings<TestSettings>();

        Assert.Equal("stored-name", settings.Name);
        Assert.Equal(50, settings.PageSize);
    }

    [Fact]
    public void BindSettings_MissingKey_ManifestDefaultWins()
    {
        var plugin = CreatePlugin(defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(25, plugin.BindSettings<TestSettings>().PageSize);
    }

    [Fact]
    public void BindSettings_NoStoredOrManifestValue_KeepsPropertyInitializer()
    {
        var plugin = CreatePlugin();

        var settings = plugin.BindSettings<TestSettings>();

        Assert.Equal("initializer", settings.Name);
        Assert.Equal(10, settings.PageSize);
    }

    [Fact]
    public void BindSettings_MalformedStoredValue_FallsBackToTypeDefaults()
    {
        var plugin = CreatePlugin(stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement("not a number") });

        Assert.Equal(10, plugin.BindSettings<TestSettings>().PageSize);
    }


    [Fact]
    public void AddWarning_Message_ShowsItAndReturnsAnId()
    {
        var (context, plugin, _) = WarningFixture();

        var id = context.AddWarning("a");

        Assert.NotEqual(0, id);
        Assert.Equal(["a"], plugin.Warnings);
    }

    [Fact]
    public void AddWarning_AfterAnotherIsCleared_IdsStayStableAndAreNotReused()
    {
        var (context, plugin, _) = WarningFixture();
        var first = context.AddWarning("a");
        var second = context.AddWarning("b");

        context.ClearWarning(first);
        var third = context.AddWarning("c");
        context.ClearWarning(second);

        Assert.Equal(["c"], plugin.Warnings);
        Assert.NotEqual(first, third);
        Assert.NotEqual(second, third);
        context.ClearWarning(third);
        Assert.Empty(plugin.Warnings);
    }

    [Fact]
    public void AddWarning_IdenticalText_ReturnsSameIdWithOneLine()
    {
        var (context, plugin, _) = WarningFixture();

        var first = context.AddWarning("a");
        var again = context.AddWarning("a");

        Assert.Equal(first, again);
        Assert.Equal(["a"], plugin.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddWarning_Blank_ReturnsZeroAndAddsNothing(string blank)
    {
        var (context, plugin, broker) = WarningFixture();
        var announced = 0;
        using var subscription = broker.Subscribe<PluginsChanged>(_ => announced++);

        var id = context.AddWarning(blank);

        Assert.Equal(0, id);
        Assert.Empty(plugin.Warnings);
        Assert.Equal(0, announced);
    }

    [Fact]
    public void ClearWarning_Id_RemovesOnlyThatLine()
    {
        var (context, plugin, _) = WarningFixture();
        var a = context.AddWarning("a");
        context.AddWarning("b");

        context.ClearWarning(a);

        Assert.Equal(["b"], plugin.Warnings);
    }

    [Fact]
    public void ClearWarning_UnknownZeroOrClearedId_IsNoOpAndSilent()
    {
        var (context, plugin, broker) = WarningFixture();
        var a = context.AddWarning("a");
        var b = context.AddWarning("b");
        context.ClearWarning(b);
        var announced = 0;
        using var subscription = broker.Subscribe<PluginsChanged>(_ => announced++);

        context.ClearWarning(0);
        context.ClearWarning(999);
        context.ClearWarning(b);
        context.ClearWarning(-1);

        Assert.Equal(["a"], plugin.Warnings);
        Assert.Equal(0, announced);
        Assert.NotEqual(0, a);
    }

    [Fact]
    public void ClearWarning_HostWarningWithSameText_IsNotRemoved()
    {
        var (context, plugin, _) = WarningFixture();
        plugin.Warnings.Add("shared");
        var id = context.AddWarning("shared");

        context.ClearWarning(id);

        Assert.Equal(["shared"], plugin.Warnings);
    }

    [Fact]
    public void ClearWarnings_RemovesEveryPluginLineAndNoHostLine()
    {
        var (context, plugin, _) = WarningFixture();
        plugin.Warnings.Add("Icon could not be read.");
        plugin.Warnings.Add("shared");
        context.AddWarning("a");
        context.AddWarning("shared");

        context.ClearWarnings();

        Assert.Equal(["Icon could not be read.", "shared"], plugin.Warnings);
    }

    [Fact]
    public void AddWarning_AfterClear_ShowsItAgain()
    {
        var (context, plugin, _) = WarningFixture();
        var first = context.AddWarning("a");
        context.ClearWarning(first);

        var second = context.AddWarning("a");

        Assert.Equal(["a"], plugin.Warnings);
        Assert.NotEqual(0, second);
        context.ClearWarnings();
        context.AddWarning("a");
        Assert.Equal(["a"], plugin.Warnings);
    }

    [Fact]
    public void Warnings_ActualChanges_AnnouncePluginsChangedEachTime()
    {
        var (context, _, broker) = WarningFixture();
        var announced = 0;
        using var subscription = broker.Subscribe<PluginsChanged>(_ => announced++);

        var id = context.AddWarning("a");
        Assert.Equal(1, announced);
        context.ClearWarning(id);
        Assert.Equal(2, announced);
        context.AddWarning("a");
        Assert.Equal(3, announced);
        context.ClearWarnings();
        Assert.Equal(4, announced);
    }

    [Fact]
    public void Warnings_NoChange_AnnounceNothing()
    {
        var (context, plugin, broker) = WarningFixture();
        plugin.Warnings.Add("shared");
        context.AddWarning("a");
        var announced = 0;
        using var subscription = broker.Subscribe<PluginsChanged>(_ => announced++);

        context.AddWarning("a");
        context.AddWarning("shared");
        context.ClearWarning(12345);
        context.ClearWarnings();
        Assert.Equal(1, announced);

        context.ClearWarnings();
        Assert.Equal(1, announced);
    }

    private static (PluginContext Context, DiscoveredPlugin Plugin, IMessageBroker Broker) WarningFixture()
    {
        var manifest = new PluginManifest
        {
            Id = Guid.Parse("00700000-0000-4000-8000-000000000702"),
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };
        var plugin = new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest };
        var broker = new MessageBroker(NullLogger<MessageBroker>.Instance);

        return (new PluginContext(manifest, null, plugin, new PluginSecretStore(new InMemorySecretStore()),
            Substitute.For<IQrCodeService>(), broker, Substitute.For<IFlashService>(), NullLogger<PluginContext>.Instance),
            plugin, broker);
    }

    private static PluginContext CreatePlugin(Dictionary<string, JsonElement>? stored = null, JsonElement? defaultValue = null)
    {
        var manifest = new PluginManifest
        {
            Id = Guid.Parse("00700000-0000-4000-8000-000000000701"),
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
            Settings =
            [
                new PluginSettingDefinition
                {
                    Key = "PageSize",
                    Type = PluginSettingType.Int,
                    Label = "Page Size",
                    Default = defaultValue,
                },
            ],
        };

        return new PluginContext(manifest, stored, new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest },
            new PluginSecretStore(new InMemorySecretStore()), Substitute.For<IQrCodeService>(),
            new MessageBroker(NullLogger<MessageBroker>.Instance), Substitute.For<IFlashService>(),
            NullLogger<PluginContext>.Instance);
    }

    /// <summary>A secret's name comes from the manifest, so two plugins can both use "session".</summary>
    [Fact]
    public async Task Secrets_AreFiledUnderThePluginsOwnId()
    {
        var store = new PluginSecretStore(new InMemorySecretStore());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var one = ContextFor(first, store);
        var two = ContextFor(second, store);

        await one.SetSecretAsync("session", "one");
        await two.SetSecretAsync("session", "two");

        // Read back through the contexts, not the store: reading under the wrong name is as much a
        // leak as writing under one, and a test that queries the store directly sees neither.
        Assert.Equal("one", await one.GetSecretAsync("session"));
        Assert.Equal("two", await two.GetSecretAsync("session"));
    }

    [Fact]
    public async Task Secrets_ReadBackWhatWasWritten()
    {
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()));

        Assert.Null(await context.GetSecretAsync("session"));

        await context.SetSecretAsync("session", "sk-1");

        Assert.Equal("sk-1", await context.GetSecretAsync("session"));
    }

    [Fact]
    public async Task Secrets_ClearedByAnEmptyValue()
    {
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()));
        await context.SetSecretAsync("session", "sk-1");

        await context.SetSecretAsync("session", null);

        Assert.Null(await context.GetSecretAsync("session"));
    }

    /// <summary>The owner is the manifest's id: a plugin able to name one could register over another's code.</summary>
    [Fact]
    public async Task RegisterQrCodeAsync_StampsTheManifestsIdAsTheOwner()
    {
        var id = Guid.NewGuid();
        var qrCodes = Substitute.For<IQrCodeService>();
        var context = ContextFor(id, new PluginSecretStore(new InMemorySecretStore()), qrCodes);

        await context.RegisterQrCodeAsync("https://example.test/join", "Scan to join");

        await qrCodes.Received(1).RegisterAsync(Arg.Is<QrCodeRegistration>(code =>
            code.OwnerId == id.ToString()
            && code.Payload == "https://example.test/join"
            && code.Caption == "Scan to join"));
    }

    [Fact]
    public async Task RegisterQrCodeAsync_WithFeatures_PassesThemOn()
    {
        var qrCodes = Substitute.For<IQrCodeService>();
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()), qrCodes);

        await context.RegisterQrCodeAsync("https://example.test/join", "Scan to join", QrCodeFeatures.SongEnqueue | QrCodeFeatures.Tipping);

        await qrCodes.Received(1).RegisterAsync(Arg.Is<QrCodeRegistration>(code =>
            code.Payload == "https://example.test/join"
            && code.Caption == "Scan to join"
            && code.Features == (QrCodeFeatures.SongEnqueue | QrCodeFeatures.Tipping)));
    }

    /// <summary>The overload without features declares none, so the code is drawn as it always was.</summary>
    [Fact]
    public async Task RegisterQrCodeAsync_WithoutFeatures_DeclaresNone()
    {
        var qrCodes = Substitute.For<IQrCodeService>();
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()), qrCodes);

        await context.RegisterQrCodeAsync("https://example.test/join", "Scan to join");

        await qrCodes.Received(1).RegisterAsync(Arg.Is<QrCodeRegistration>(code => code.Features == QrCodeFeatures.None));
    }

    [Fact]
    public async Task UnregisterQrCodeAsync_WithdrawsUnderTheManifestsId()
    {
        var id = Guid.NewGuid();
        var qrCodes = Substitute.For<IQrCodeService>();
        var context = ContextFor(id, new PluginSecretStore(new InMemorySecretStore()), qrCodes);

        await context.UnregisterQrCodeAsync();

        await qrCodes.Received(1).UnregisterAsync(id.ToString());
    }

    /// <summary>A warning raised after startup reaches no one through the list alone: the startup
    /// dump has run, and the Plugins page is not open.</summary>
    [Fact]
    public void AddWarning_FlashesItOnTheConsole_NamingThePlugin()
    {
        var flash = Substitute.For<IFlashService>();
        var discovered = DiscoveredFor(Guid.NewGuid());
        var context = ContextFor(discovered, flash, new RecordingLogger());

        context.AddWarning("Could not sign in: no keyring");

        flash.Received(1).Show("Test: Could not sign in: no keyring", FlashType.Warning);
        Assert.Contains("Could not sign in: no keyring", discovered.Warnings);
    }

    [Fact]
    public void AddWarning_LogsItAtWarning()
    {
        var logger = new RecordingLogger();
        var context = ContextFor(DiscoveredFor(Guid.NewGuid()), Substitute.For<IFlashService>(), logger);

        context.AddWarning("Could not sign in: no keyring");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Could not sign in: no keyring", entry.Message);
    }

    /// <summary>A plugin retrying in the background reports the same line each time; the console
    /// must not stack a banner per attempt.</summary>
    [Fact]
    public void AddWarning_SameWarningAgain_FlashesAndLogsOnce()
    {
        var flash = Substitute.For<IFlashService>();
        var logger = new RecordingLogger();
        var discovered = DiscoveredFor(Guid.NewGuid());
        var context = ContextFor(discovered, flash, logger);

        context.AddWarning("Could not sign in: no keyring");
        context.AddWarning("Could not sign in: no keyring");

        flash.ReceivedWithAnyArgs(1).Show(default!, default);
        Assert.Single(logger.Entries);
        Assert.Single(discovered.Warnings);
    }

    /// <summary>The host already shows that line, so nothing new reached the Plugins page.</summary>
    [Fact]
    public void AddWarning_HostAlreadyShowsTheLine_DoesNotFlash()
    {
        var flash = Substitute.For<IFlashService>();
        var discovered = DiscoveredFor(Guid.NewGuid());
        discovered.Warnings.Add("Could not sign in: no keyring");
        var context = ContextFor(discovered, flash, new RecordingLogger());

        context.AddWarning("Could not sign in: no keyring");

        flash.DidNotReceiveWithAnyArgs().Show(default!, default);
    }

    /// <summary>A cleared warning that comes back is a new failure the host has not seen.</summary>
    [Fact]
    public void AddWarning_AfterItWasCleared_FlashesAgain()
    {
        var flash = Substitute.For<IFlashService>();
        var context = ContextFor(DiscoveredFor(Guid.NewGuid()), flash, new RecordingLogger());

        context.ClearWarning(context.AddWarning("Could not sign in: no keyring"));
        context.AddWarning("Could not sign in: no keyring");

        flash.Received(2).Show("Test: Could not sign in: no keyring", FlashType.Warning);
    }

    private static DiscoveredPlugin DiscoveredFor(Guid id)
    {
        var manifest = ManifestFor(id);
        return new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest };
    }

    private static PluginManifest ManifestFor(Guid id) => new()
    {
        Id = id,
        Name = "Test",
        Version = "1.0.0",
        EntryAssembly = "Test.dll",
        ApiVersion = PluginApi.CurrentVersion,
    };

    private static PluginContext ContextFor(DiscoveredPlugin discovered, IFlashService flash, ILogger<PluginContext> logger)
        => new(discovered.Manifest!, null, discovered, new PluginSecretStore(new InMemorySecretStore()),
            Substitute.For<IQrCodeService>(), new MessageBroker(NullLogger<MessageBroker>.Instance), flash, logger);

    private static PluginContext ContextFor(Guid id, IPluginSecretStore store, IQrCodeService? qrCodes = null)
    {
        var manifest = ManifestFor(id);

        return new PluginContext(manifest, null,
            new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest }, store,
            qrCodes ?? Substitute.For<IQrCodeService>(), new MessageBroker(NullLogger<MessageBroker>.Instance),
            Substitute.For<IFlashService>(), NullLogger<PluginContext>.Instance);
    }

    private sealed class RecordingLogger : ILogger<PluginContext>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private class TestSettings
    {
        public string Name { get; set; } = "initializer";
        public int PageSize { get; set; } = 10;
    }
}
