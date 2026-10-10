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

/// <summary>The header's shortcut to the lyrics offset: it shows what is saved and saves what it shows.</summary>
public class LyricsOffsetStepperTests : BunitContext
{
    private const string Stepper = ".kh-lyrics-offset";
    private const string Earlier = ".kh-lyrics-offset__earlier";
    private const string Later = ".kh-lyrics-offset__later";
    private const string Value = ".kh-lyrics-offset__value";

    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private AppSettings _saved = new() { ShowLyricsOffsetControl = true };

    public LyricsOffsetStepperTests()
    {
        // A fresh snapshot per read, as the real service builds one, so a click cannot edit the saved one.
        _appSettings.Current.Returns(_ => _saved with { });
        _appSettings.SaveAsync(Arg.Any<AppSettings>()).Returns(call =>
        {
            _saved = call.Arg<AppSettings>() with { };
            return new AppSettingsSaveResult(true);
        });

        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    private IRenderedComponent<LyricsOffsetStepper> RenderStepper(bool songHasTimedLyrics = true)
        => Render<LyricsOffsetStepper>(p => p.Add(s => s.SongHasTimedLyrics, songHasTimedLyrics));

    [Fact]
    public void Render_TurnedOff_DrawsNothing()
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = false, LyricsOffsetMilliseconds = -120 };

        Assert.Empty(RenderStepper().FindAll(Stepper));
    }

    [Fact]
    public void Render_ASongWithNoTimedWords_DrawsNothing()
    {
        Assert.Empty(RenderStepper(songHasTimedLyrics: false).FindAll(Stepper));
    }

    [Theory]
    [InlineData(-120, "−120 ms", true)]
    [InlineData(250, "+250 ms", true)]
    [InlineData(0, "0 ms", false)]
    public void Render_ShowsTheSavedOffset(int offset, string label, bool set)
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = offset };

        var value = RenderStepper().Find(Value);

        Assert.Equal(label, value.TextContent.Trim());
        Assert.Equal(set, value.ClassList.Contains("kh-lyrics-offset__value--set"));
    }

    [Theory]
    [InlineData(Earlier, -130, "−130 ms")]
    [InlineData(Later, -110, "−110 ms")]
    public async Task Click_AStep_SavesTheOffsetMovedTenMilliseconds_AndShowsIt(string button, int expected, string label)
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = -120, SegmentSeconds = 4 };
        var cut = RenderStepper();

        await cut.Find(button).ClickAsync(new());

        // The rest of the snapshot goes back as it was: this saves one setting, not a form.
        await _appSettings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.LyricsOffsetMilliseconds == expected && s.SegmentSeconds == 4));
        Assert.Equal(label, cut.Find(Value).TextContent.Trim());
    }

    [Fact]
    public async Task Click_SeveralSteps_EndsOnTheLastOne()
    {
        var cut = RenderStepper();

        await cut.Find(Later).ClickAsync(new());
        await cut.Find(Later).ClickAsync(new());
        await cut.Find(Later).ClickAsync(new());

        Assert.Equal(30, _saved.LyricsOffsetMilliseconds);
        Assert.Equal("+30 ms", cut.Find(Value).TextContent.Trim());
    }

    [Theory]
    [InlineData(-2000, Earlier, Later)]
    [InlineData(2000, Later, Earlier)]
    public void Render_AtTheLimit_DisablesTheStepPastIt(int offset, string disabled, string enabled)
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = offset };

        var cut = RenderStepper();

        Assert.True(cut.Find(disabled).HasAttribute("disabled"));
        Assert.False(cut.Find(enabled).HasAttribute("disabled"));
    }

    /// <summary>A hand-edited offset off the 10 ms grid still stops at the limit rather than passing it.</summary>
    [Fact]
    public async Task Click_AStepThatWouldPassTheLimit_StopsAtIt()
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = -1995 };
        var cut = RenderStepper();

        await cut.Find(Earlier).ClickAsync(new());

        Assert.Equal(-2000, _saved.LyricsOffsetMilliseconds);
    }

    [Fact]
    public async Task Click_TheValue_SetsTheOffsetBackToZero()
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = -120 };
        var cut = RenderStepper();

        await cut.Find(Value).ClickAsync(new());

        await _appSettings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.LyricsOffsetMilliseconds == 0));
        Assert.Equal("0 ms", cut.Find(Value).TextContent.Trim());
    }

    [Fact]
    public async Task Click_TheValueAtZero_SavesNothing()
    {
        var cut = RenderStepper();

        await cut.Find(Value).ClickAsync(new());

        await _appSettings.DidNotReceive().SaveAsync(Arg.Any<AppSettings>());
    }

    [Fact]
    public async Task Click_TheSaveIsRefused_GoesBackToTheSavedOffset_AndSaysWhy()
    {
        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = -120 };
        _appSettings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(false, "No admin has a password."));
        var cut = RenderStepper();

        await cut.Find(Later).ClickAsync(new());

        Assert.Equal("−120 ms", cut.Find(Value).TextContent.Trim());
        _flash.Received(1).Show("No admin has a password.", FlashType.Warning);
    }

    /// <summary>Another console, or the App Settings page, saved it: this one follows.</summary>
    [Fact]
    public void TheAdjustmentsChange_ReadsTheOffsetAgain()
    {
        var cut = RenderStepper();

        _saved = new AppSettings { ShowLyricsOffsetControl = true, LyricsOffsetMilliseconds = 90 };
        _broker.Announce(new TimedLyricsSettingsChanged());

        cut.WaitForAssertion(() => Assert.Equal("+90 ms", cut.Find(Value).TextContent.Trim()));
    }
}
