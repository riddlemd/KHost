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
#pragma warning disable CS0618 // the obsolete member must still be covered by ClearWarnings
        context.ReportWarning("old style");
#pragma warning restore CS0618
        Assert.Contains("old style", plugin.Warnings);

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
            Substitute.For<IQrCodeService>(), broker), plugin, broker);
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
            new PluginSecretStore(new InMemorySecretStore()), Substitute.For<IQrCodeService>(), new MessageBroker(NullLogger<MessageBroker>.Instance));
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
    public async Task UnregisterQrCodeAsync_WithdrawsUnderTheManifestsId()
    {
        var id = Guid.NewGuid();
        var qrCodes = Substitute.For<IQrCodeService>();
        var context = ContextFor(id, new PluginSecretStore(new InMemorySecretStore()), qrCodes);

        await context.UnregisterQrCodeAsync();

        await qrCodes.Received(1).UnregisterAsync(id.ToString());
    }

    private static PluginContext ContextFor(Guid id, IPluginSecretStore store, IQrCodeService? qrCodes = null)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };

        return new PluginContext(manifest, null,
            new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest }, store,
            qrCodes ?? Substitute.For<IQrCodeService>(), new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    private class TestSettings
    {
        public string Name { get; set; } = "initializer";
        public int PageSize { get; set; } = 10;
    }
}
