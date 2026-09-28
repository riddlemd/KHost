using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>The header's shortcut to one App Setting: it shows what is saved and saves what it shows.</summary>
public class ColorBlindLyricsToggleTests : BunitContext
{
    private const string Toggle = ".kh-lyric-colours-toggle";

    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private AppSettings _saved = new();

    public ColorBlindLyricsToggleTests()
    {
        // A fresh snapshot per read, as the real service builds one, so a click cannot edit the saved one.
        _appSettings.Current.Returns(_ => _saved with { });
        _appSettings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Theory]
    [InlineData(true, "true", "bi-palette-fill", "on — click to turn off")]
    [InlineData(false, "false", "bi-palette", "off — click to turn on")]
    public void Render_ShowsTheSavedSetting(bool on, string pressed, string icon, string says)
    {
        _saved = new AppSettings { ColorBlindFriendlyLyrics = on };

        var button = Render<ColorBlindLyricsToggle>().Find(Toggle);

        Assert.Equal(pressed, button.GetAttribute("aria-pressed"));
        Assert.Equal(on, button.ClassList.Contains("kh-lyric-colours-toggle--on"));
        Assert.Contains(icon, button.QuerySelector("i")!.ClassList);
        Assert.EndsWith(says, button.GetAttribute("title"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Click_SavesTheFlippedValue_AndShowsIt(bool on)
    {
        _saved = new AppSettings { ColorBlindFriendlyLyrics = on, SegmentSeconds = 4 };
        var cut = Render<ColorBlindLyricsToggle>();

        await cut.Find(Toggle).ClickAsync(new());

        // The rest of the snapshot goes back as it was: this saves one setting, not a form.
        await _appSettings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.ColorBlindFriendlyLyrics == !on && s.SegmentSeconds == 4));
        Assert.Equal((!on).ToString().ToLowerInvariant(), cut.Find(Toggle).GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Click_TheSaveIsRefused_StaysAsSaved_AndSaysWhy()
    {
        _appSettings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(false, "No admin has a password."));
        var cut = Render<ColorBlindLyricsToggle>();

        await cut.Find(Toggle).ClickAsync(new());

        Assert.Equal("false", cut.Find(Toggle).GetAttribute("aria-pressed"));
        _flash.Received(1).Show("No admin has a password.", FlashType.Warning);
    }

    /// <summary>Another console, or the App Settings page, saved it: this one follows.</summary>
    [Fact]
    public void TheAdjustmentsChange_ReadsTheSettingAgain()
    {
        var cut = Render<ColorBlindLyricsToggle>();

        _saved = new AppSettings { ColorBlindFriendlyLyrics = true };
        _broker.Announce(new TimedLyricsSettingsChanged());

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(Toggle).GetAttribute("aria-pressed")));
    }
}
