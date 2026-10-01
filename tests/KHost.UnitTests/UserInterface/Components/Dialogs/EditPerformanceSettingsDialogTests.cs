using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A waiting turn's key, tempo and levels, set with the controls the playing song uses and
/// saved only on Save.</summary>
public class EditPerformanceSettingsDialogTests : BunitContext
{
    private const string Sliders = ".kh-song-control--slider .kh-song-control__track";
    private const string Labels = ".kh-song-control__label";
    private const string Values = ".kh-song-control__value";
    private const string Save = ".kh-edit-performance-settings-dialog__save-btn";
    private const string Cancel = ".kh-edit-performance-settings-dialog__cancel-btn";

    private readonly DialogService _dialogs = new(NullLogger<DialogService>.Instance, []);
    private readonly IAudioTrackService _audioTracks = Substitute.For<IAudioTrackService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly AppSettings _settings = new() { BackingVocalVolume = 80 };

    private readonly Performance _performance = new()
    {
        Id = Guid.NewGuid(),
        SingerId = Guid.NewGuid(),
        MediaId = Guid.NewGuid(),
        Pitch = -2,
        Tempo = 15,
    };

    private readonly Media _media = new() { Id = Guid.NewGuid(), FilePath = "/music/duet.kit", Title = "Duet" };

    private PerformanceSettings? _saved;
    private int _cancelled;

    public EditPerformanceSettingsDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _appSettings.Current.Returns(_settings);
        _audioTracks.ReadTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AudioTrack>>([]));

        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton(_audioTracks);
        Services.AddSingleton(_appSettings);
        Services.AddSingleton(_playback);
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
    public async Task Saving_HandsBackTheMovedValues()
    {
        GiveADuet();
        var host = await OpenAsync();

        host.FindAll(Sliders)[0].Change("3");
        host.FindAll(Sliders)[1].Change("-20");
        host.FindAll(Sliders)[4].Change("65");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Equal(3, _saved!.Pitch);
        Assert.Equal(-20, _saved.Tempo);
        Assert.Equal(65, _saved.VoiceVolumes!["♀"]);
        Assert.Equal(0, _saved.VoiceVolumes["♂"]);
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
        Assert.Null(_saved!.BackingVolume);
    }

    [Fact]
    public async Task Saving_WithBackingMoved_RecordsTheLevel()
    {
        GiveADuet();
        var host = await OpenAsync();

        host.FindAll(Sliders)[2].Change("30");
        host.Find(Save).Click();

        host.WaitForAssertion(() => Assert.NotNull(_saved));
        Assert.Equal(30, _saved!.BackingVolume);
    }

    [Fact]
    public async Task Cancelling_HandsNothingBack()
    {
        var host = await OpenAsync();

        host.FindAll(Sliders)[0].Change("4");
        host.Find(Cancel).Click();

        host.WaitForAssertion(() => Assert.Equal(1, _cancelled));
        Assert.Null(_saved);
        Assert.Empty(host.FindAll(Save));
    }

    /// <summary>Loaded while the editor was open: playback owns the values now and would write its
    /// own back over the row, so the save is refused and the editor says where to go instead.</summary>
    [Fact]
    public async Task Saving_ATurnLoadedMeanwhile_RefusesAndPointsAtTheSongControls()
    {
        var host = await OpenAsync();
        _playback.CurrentPerformance.Returns(_performance);

        host.FindAll(Sliders)[0].Change("4");
        host.Find(Save).Click();

        Assert.Null(_saved);
        Assert.True(host.Find(Save).HasAttribute("disabled"));
        Assert.Contains("song controls", host.Find(".kh-edit-performance-settings-dialog__locked").TextContent);
    }

    [Fact]
    public async Task Opening_WithDialsChosen_DrawsDials()
    {
        _settings.SongControlStyle = SongControlStyle.Dials;

        var host = await OpenAsync();

        Assert.Equal(2, host.FindAll(".kh-song-control--dial").Count);
        Assert.Empty(host.FindAll(Sliders));
    }

    private async Task<IRenderedComponent<DialogHost>> OpenAsync()
    {
        var host = Render<DialogHost>();

        await _dialogs.RequestPerformanceSettingsAsync(_performance, _media,
            settings => { _saved = settings; return Task.CompletedTask; },
            onCancel: () => _cancelled++);

        host.WaitForElement(Save);
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
