using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>One turn's name and how it is sung, edited together and handed back only on Save, with
/// only the parts that moved.</summary>
public class EditPerformanceDialogTests : BunitContext
{
    private const string Sliders = ".kh-song-control--slider .kh-song-control__track";
    private const string Dials = ".kh-song-control--dial .kh-song-control__grip";
    private const string Labels = ".kh-song-control__label";
    private const string Values = ".kh-song-control__value";
    private const string Alias = "#edit-performance-sung-as";
    private const string Save = ".kh-edit-performance-dialog__save-btn";
    private const string Cancel = ".kh-edit-performance-dialog__cancel-btn";

    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly IAudioTrackService _audioTracks = Substitute.For<IAudioTrackService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly AppSettings _settings = new() { BackingVocalVolume = 80 };
    private readonly Venue _venue = new() { Name = "Bar", Settings = new() { AllowAliases = true } };

    private readonly Performance _performance = new()
    {
        Id = Guid.NewGuid(),
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
        Pitch = -2,
        Tempo = 15,
        SungAs = "flo",
    };

    private readonly Media _media = new() { Id = Guid.NewGuid(), FilePath = "/music/duet.ext", Title = "Duet" };

    private PerformanceEdit? _saved;
    private int _cancelled;

    public EditPerformanceDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _appSettings.Current.Returns(_settings);
        _audioTracks.ReadTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AudioTrack>>([]));
        _venues.ReadSelectedVenueAsync().Returns(_ => _venue);

        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(_audioTracks);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_playback);
        Services.AddSingleton(_venues);

        _presets.ReadAll().Returns(
        [
            new VisualiserPreset { Name = "Rovastar - Oozing Resistance", Source = VisualiserPresetSource.Bundled },
            new VisualiserPreset { Name = "_Mig_049", Source = VisualiserPresetSource.Bundled },
        ]);
        Services.AddSingleton(_presets);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IVideoBackdropService>());
    }

    [Fact]
    public async Task Opening_ShowsTheKeyAndTempoTheTurnCarries()
    {
        var host = await OpenAsync();

        var values = host.FindAll(Values);
        Assert.Equal("−2", values[0].TextContent.Trim());
        Assert.Equal("+15%", values[1].TextContent.Trim());
    }

    [Fact]
    public async Task Opening_PrefillsTheNameTheTurnIsQueuedUnder()
    {
        var host = await OpenAsync();

        Assert.Equal("flo", host.Find(Alias).GetAttribute("value"));
        Assert.Equal("Ann", host.Find(Alias).GetAttribute("placeholder"));
    }

    /// <summary>The room would not hear an alias, so the field is not offered; the controls still are.</summary>
    [Fact]
    public async Task Opening_AtAVenueWithoutAliases_OffersNoAliasField()
    {
        _venue.Settings.AllowAliases = false;

        var host = await OpenAsync();

        Assert.Empty(host.FindAll(Alias));
        Assert.Equal(2, host.FindAll(Sliders).Count);
    }

    [Fact]
    public async Task Opening_ADuet_OffersAFaderPerVoicePrefilledFromTheTurn()
    {
        GiveADuet();
        _performance.BackingVolume = 40;
        _performance.VoiceVolumes = new() { ["♀"] = 55 };

        var host = await OpenAsync();

        Assert.Equal(["Key", "Tempo", "Backing Vocals", "♂", "♀"],
            host.FindAll(Labels).Select(l => l.TextContent.Trim()));
        var values = host.FindAll(Values).Select(v => v.TextContent.Trim()).ToList();
        Assert.Equal("40%", values[2]);
        Assert.Equal("0%", values[3]);
        Assert.Equal("55%", values[4]);
    }

    /// <summary>A turn nobody mixed opens at the house setting, the level LoadAsync would play.</summary>
    [Fact]
    public async Task Opening_AnUnmixedTurn_ShowsTheMachineBackingLevel()
    {
        GiveADuet();

        var host = await OpenAsync();

        Assert.Equal("80%", host.FindAll(Values)[2].TextContent.Trim());
    }

    [Fact]
    public async Task Opening_ShowsTheSingersNotesToRead()
    {
        _performance.SingerNotes = "For Sam's birthday";

        var host = await OpenAsync();

        Assert.Equal("For Sam's birthday", host.Find(".kh-edit-performance-dialog__singer-notes-text").TextContent.Trim());
    }

    /// <summary>The singer wrote it; the host reads it and has nothing to change it with.</summary>
    [Fact]
    public async Task Opening_OffersNoWayToEditTheSingersNotes()
    {
        _performance.SingerNotes = "For Sam's birthday";

        var host = await OpenAsync();

        Assert.Empty(host.FindAll(".kh-edit-performance-dialog__singer-notes input, .kh-edit-performance-dialog__singer-notes textarea"));
        Assert.DoesNotContain(host.FindAll("input, textarea"), field => field.GetAttribute("value") == "For Sam's birthday");
    }

    [Fact]
    public async Task Opening_ATurnWithNoNotes_ShowsNoNotes()
    {
        var host = await OpenAsync();

        Assert.Empty(host.FindAll(".kh-edit-performance-dialog__singer-notes"));
    }

    [Fact]
    public async Task Saving_TheAliasAlone_HandsBackTheNameAndNoSettings()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change("  DJ P ");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.True(_saved!.SungAsChanged);
        Assert.Equal("DJ P", _saved.SungAs);
        Assert.Null(_saved.Settings);
    }

    /// <summary>Blank is stored as no name, which every reader takes as the singer's own.</summary>
    [Fact]
    public async Task Saving_ABlankedAlias_HandsBackNoName()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change("   ");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.True(_saved!.SungAsChanged);
        Assert.Null(_saved.SungAs);
    }

    [Fact]
    public async Task Saving_TheSameNameRetyped_IsNoChange()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change(" flo ");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.True(_saved!.IsEmpty);
    }

    [Fact]
    public async Task Saving_AnOverlongAlias_IsRefused()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change(new string('a', 256));
        host.Find(Save).Click();

        Assert.Null(_saved);
        Assert.Contains("too long", host.Markup);
    }

    [Fact]
    public async Task Saving_TheControlsAlone_LeavesTheAliasAlone()
    {
        var host = await OpenAsync();

        host.FindAll(Sliders)[0].Change("3");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.False(_saved!.SungAsChanged);
        Assert.Equal(3, _saved.Settings!.Pitch);
        Assert.Equal(15, _saved.Settings.Tempo);
    }

    [Fact]
    public async Task Saving_BothMoved_HandsBackBoth()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change("DJ P");
        host.FindAll(Sliders)[1].Change("-20");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Equal("DJ P", _saved!.SungAs);
        Assert.Equal(-20, _saved.Settings!.Tempo);
    }

    [Fact]
    public async Task Saving_OnlyAVoiceMoved_HandsBackTheSettings()
    {
        GiveADuet();
        var host = await OpenAsync();

        host.FindAll(Sliders)[4].Change("65");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Equal(65, _saved!.Settings!.VoiceVolumes!["♀"]);
        Assert.Equal(0, _saved.Settings.VoiceVolumes["♂"]);
        Assert.Equal(0, _cancelled);
    }

    /// <summary>Untouched backing stays null, so the turn goes on following the machine setting.</summary>
    [Fact]
    public async Task Saving_WithBackingUntouched_LeavesItToTheMachineSetting()
    {
        GiveADuet();
        var host = await OpenAsync();

        host.FindAll(Sliders)[0].Change("1");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Null(_saved!.Settings!.BackingVolume);
    }

    [Fact]
    public async Task Saving_WithBackingMoved_RecordsTheLevel()
    {
        GiveADuet();
        var host = await OpenAsync();

        host.FindAll(Sliders)[2].Change("30");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Equal(30, _saved!.Settings!.BackingVolume);
    }

    [Fact]
    public async Task Cancelling_HandsNothingBack()
    {
        var host = await OpenAsync();

        host.Find(Alias).Change("DJ P");
        host.FindAll(Sliders)[0].Change("4");
        host.Find(Cancel).Click();

        host.WaitForAssertion(() => Assert.Equal(1, _cancelled));
        Assert.Null(_saved);
        Assert.Empty(host.FindAll(Save));
    }

    /// <summary>The loaded song opens read-only: its name was announced at load and its values are
    /// the song controls' to move, and the dialog says both.</summary>
    [Fact]
    public async Task Opening_TheLoadedTurn_ShowsEverythingReadOnly()
    {
        _playback.CurrentPerformance.Returns(_performance);

        var host = await OpenAsync();

        Assert.True(host.Find(Alias).HasAttribute("disabled"));
        Assert.All(host.FindAll(Sliders), slider => Assert.True(slider.HasAttribute("disabled")));
        Assert.True(host.Find(Save).HasAttribute("disabled"));
        Assert.Contains("microphone", host.Find(".kh-edit-performance-dialog__alias-locked").TextContent);
        Assert.Contains("song controls", host.Find(".kh-edit-performance-dialog__locked").TextContent);
    }

    [Fact]
    public async Task Opening_AWaitingTurn_LeavesEverythingEditable()
    {
        var host = await OpenAsync();

        Assert.False(host.Find(Alias).HasAttribute("disabled"));
        Assert.All(host.FindAll(Sliders), slider => Assert.False(slider.HasAttribute("disabled")));
        Assert.False(host.Find(Save).HasAttribute("disabled"));
        Assert.Empty(host.FindAll(".kh-edit-performance-dialog__locked"));
    }

    /// <summary>Loaded while the editor was open: the save is refused and the editor says where to
    /// go instead.</summary>
    [Fact]
    public async Task Saving_ATurnLoadedMeanwhile_RefusesAndPointsAtTheSongControls()
    {
        var host = await OpenAsync();
        _playback.CurrentPerformance.Returns(_performance);

        host.Find(Alias).Change("DJ P");
        host.FindAll(Sliders)[0].Change("4");
        host.Find(Save).Click();

        Assert.Null(_saved);
        Assert.True(host.Find(Save).HasAttribute("disabled"));
        Assert.Contains("song controls", host.Find(".kh-edit-performance-dialog__locked").TextContent);
    }

    [Fact]
    public async Task Opening_WithDialsChosen_DrawsDials()
    {
        _settings.SongControlStyle = SongControlStyle.Dials;

        var host = await OpenAsync();

        Assert.Equal(2, host.FindAll(".kh-song-control--dial").Count);
        Assert.Empty(host.FindAll(Sliders));
        Assert.All(host.FindAll(Dials), dial => Assert.False(dial.HasAttribute("disabled")));
    }

    [Fact]
    public async Task Opening_TheLoadedTurnWithDials_DisablesTheDials()
    {
        _settings.SongControlStyle = SongControlStyle.Dials;
        _playback.CurrentPerformance.Returns(_performance);

        var host = await OpenAsync();

        Assert.Equal(2, host.FindAll(Dials).Count);
        Assert.All(host.FindAll(Dials), dial => Assert.True(dial.HasAttribute("disabled")));
    }

    // --- background ---

    private const string Background = "#edit-performance-background";

    /// <summary>A sung turn is history: nothing will draw it again, so there is nothing to choose.</summary>
    [Fact]
    public async Task Opening_ATurnAlreadySung_OffersNoBackground()
    {
        var host = await OpenAsync();

        Assert.Empty(host.FindAll(Background));
    }

    [Fact]
    public async Task Opening_AQueuedTurnWithNone_StartsOnTheVenuesPlaylistWithNoEditor()
    {
        _performance.QueuePosition = 2;

        var host = await OpenAsync();

        Assert.Equal("Venue", host.Find(Background).GetAttribute("value"));
        Assert.Empty(host.FindAll(".kh-visualisation-look"));
    }

    [Fact]
    public async Task Opening_AQueuedTurnWithALook_ShowsItInTheEditor()
    {
        _performance.QueuePosition = 2;
        _performance.Background = new PerformanceBackground { Type = PerformanceBackgroundType.Look, PresetName = "_Mig_049", Brightness = 140 };

        var host = await OpenAsync();

        Assert.Equal("Look", host.Find(Background).GetAttribute("value"));
        Assert.Equal("0:_Mig_049", host.Find("#visualisation-preset").GetAttribute("value"));
        Assert.Equal("140", host.Find("#visualisation-brightness").GetAttribute("value"));
    }

    [Fact]
    public async Task Saving_Black_SavesBlackThroughTheService()
    {
        _performance.QueuePosition = 2;
        var host = await OpenAsync();

        host.Find(Background).Change("Black");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.True(_saved!.BackgroundChanged);
        await _saved.SaveAsync(_performances, _performance.Id);
        await _performances.Received(1).UpdateBackgroundAsync(_performance.Id,
            Arg.Is<PerformanceBackground?>(b => b != null && b.Type == PerformanceBackgroundType.Black));
    }

    [Fact]
    public async Task Saving_ALook_SavesItAsEditedThroughTheService()
    {
        _performance.QueuePosition = 2;
        var host = await OpenAsync();

        host.Find(Background).Change("Look");
        host.Find("#visualisation-preset").Change("0:_Mig_049");
        host.Find("#visualisation-brightness").Change("150");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        await _saved!.SaveAsync(_performances, _performance.Id);
        await _performances.Received(1).UpdateBackgroundAsync(_performance.Id,
            Arg.Is<PerformanceBackground?>(b => b != null && b.Type == PerformanceBackgroundType.Look
                && b.PresetName == "_Mig_049" && b.Brightness == 150));
    }

    [Fact]
    public async Task Saving_TheVenuesPlaylistOverBlack_SavesNullThroughTheService()
    {
        _performance.QueuePosition = 2;
        _performance.Background = new PerformanceBackground { Type = PerformanceBackgroundType.Black };
        var host = await OpenAsync();

        host.Find(Background).Change("Venue");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.True(_saved!.BackgroundChanged);
        await _saved.SaveAsync(_performances, _performance.Id);
        await _performances.Received(1).UpdateBackgroundAsync(_performance.Id, null);
    }

    /// <summary>Reopening a look and saving writes nothing about it, so the queue is not told it moved.</summary>
    [Fact]
    public async Task Saving_TheLookAsOpened_ChangesNoBackground()
    {
        _performance.QueuePosition = 2;
        _performance.Background = new PerformanceBackground { Type = PerformanceBackgroundType.Look, PresetName = "_Mig_049" };
        var host = await OpenAsync();

        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.False(_saved!.BackgroundChanged);
    }

    [Fact]
    public async Task Opening_TheLoadedTurn_DisablesTheBackground()
    {
        _performance.QueuePosition = 2;
        _performance.Background = new PerformanceBackground { Type = PerformanceBackgroundType.Look, PresetName = "_Mig_049" };
        _playback.CurrentPerformance.Returns(_performance);

        var host = await OpenAsync();

        Assert.True(host.Find(Background).HasAttribute("disabled"));
        Assert.True(host.Find("fieldset.kh-visualisation-look").HasAttribute("disabled"));
    }

    private async Task<IRenderedComponent<DialogHost>> OpenAsync()
    {
        var host = Render<DialogHost>();

        await _dialogs.RequestEditPerformanceAsync(_performance, _media, "Ann",
            edit => { _saved = edit; return Task.CompletedTask; },
            onCancel: () => _cancelled++);

        host.WaitForElement(Save);
        host.WaitForElement(".kh-song-control");
        return host;
    }

    private void GiveADuet() =>
        _audioTracks.ReadTracksAsync(_media.FilePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AudioTrack>>(
        [
            new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
            new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
            new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal (♂)") { Voice = "♂" },
            new AudioTrack(3, AudioTrackRole.Lead, "Lead Vocal (♀)") { Voice = "♀" },
        ]));
}
