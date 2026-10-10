using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The dialog could only add a library row as an entry's picture: a voiceover over a still
/// could not be built, and a sound added on its own blanked the screen for its whole run.</summary>
public class EditPlaylistDialogVoiceoverTests : BunitContext
{
    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();

    private readonly Media _voice = new()
    {
        Id = Guid.NewGuid(), FilePath = "/media/voice.mp3", Title = "Happy Hour Voiceover",
        Format = "MP3", Type = MediaType.Audio, Status = MediaStatus.Ready, Duration = TimeSpan.FromSeconds(20),
    };

    private readonly Media _still = new()
    {
        Id = Guid.NewGuid(), FilePath = "/media/still.png", Title = "Happy Hour Still",
        Format = "PNG", Type = MediaType.Image, Status = MediaStatus.Ready,
    };

    private readonly Media _video = new()
    {
        Id = Guid.NewGuid(), FilePath = "/media/spot.mp4", Title = "Happy Hour Spot",
        Format = "MP4", Type = MediaType.Video, Status = MediaStatus.Ready,
    };

    public EditPlaylistDialogVoiceoverTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Media[] library = [_voice, _still, _video];

        _media.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<SortDescriptor?>(), Arg.Any<MediaSearchOptions?>())
            .Returns(call =>
            {
                var types = call.Arg<MediaSearchOptions?>()?.Types ?? [];
                var hits = library.Where(m => types.Contains(m.Type)).ToList();

                return Task.FromResult(new PaginatedResult<Media> { Items = hits, TotalCount = hits.Count, PageNumber = 1, PageSize = 50 });
            });

        foreach (var media in library)
            _media.ReadAsync(media.Id).Returns(Task.FromResult<Media?>(media));

        _pools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(Task.FromResult<IReadOnlyList<MediaPool>>([]));

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings { AdDefaultDurationSeconds = 10 });

        Services.AddSingleton(_pools);
        Services.AddSingleton(_media);
        Services.AddSingleton(appSettings);
    }

    private IRenderedComponent<EditPlaylistDialog> RenderDialog(
        PoolPurpose purpose, Action<MediaPool> onSave, params MediaPoolEntry[] entries)
        => Render<EditPlaylistDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Pool, new MediaPool { Name = "Spots", Purpose = purpose, Entries = [.. entries] })
            .Add(p => p.Purpose, purpose)
            .Add(p => p.OnSave, EventCallback.Factory.Create<MediaPool>(this, onSave)));

    private static MediaPoolEntry Entry(Guid? mediaId = null, Guid? audioMediaId = null, TimeSpan? audioStart = null)
        => new() { Id = Guid.NewGuid(), MediaId = mediaId, AudioMediaId = audioMediaId, AudioStart = audioStart };

    private static async Task AddThroughThePickerAsync(IRenderedComponent<EditPlaylistDialog> dialog, Media media)
    {
        dialog.FindAll("button").First(button => button.TextContent.Contains("Add entry")).Click();

        var picker = dialog.FindComponent<ComboBox<EditPlaylistDialog.AddChoice>>();
        var choice = (await picker.Instance.Search!("")).First(c => c.Media?.Id == media.Id);
        await picker.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(choice));

        dialog.FindAll("button").First(button => button.TextContent.Trim() == "Add").Click();
    }

    private static void Save(IRenderedComponent<EditPlaylistDialog> dialog)
        => dialog.Find(".kh-button--primary").Click();

    [Fact]
    public async Task AddingASound_ToAnAdPlaylist_MakesASoundOnlyEntry()
    {
        MediaPool? saved = null;
        var dialog = RenderDialog(PoolPurpose.Ads, pool => saved = pool);

        await AddThroughThePickerAsync(dialog, _voice);
        Save(dialog);

        var entry = Assert.Single(saved!.Entries);
        Assert.Equal(_voice.Id, entry.AudioMediaId);
        Assert.Null(entry.MediaId);
    }

    // A bed is played as the entry's own media; nothing about break music changes.
    [Fact]
    public async Task AddingASound_ToABreakMusicPlaylist_StaysTheEntrysMedia()
    {
        MediaPool? saved = null;
        var dialog = RenderDialog(PoolPurpose.BreakMusic, pool => saved = pool);

        await AddThroughThePickerAsync(dialog, _voice);
        Save(dialog);

        var entry = Assert.Single(saved!.Entries);
        Assert.Equal(_voice.Id, entry.MediaId);
        Assert.Null(entry.AudioMediaId);
    }

    [Fact]
    public void AStillEntry_OffersAVoiceover()
    {
        var dialog = RenderDialog(PoolPurpose.Ads, _ => { }, Entry(mediaId: _still.Id));

        Assert.Single(dialog.FindComponents<ComboBox<Media>>());
    }

    // A video carries its own sound; a second track over it would be two at once.
    [Fact]
    public void AVideoEntry_OffersNoVoiceover()
    {
        var dialog = RenderDialog(PoolPurpose.Ads, _ => { }, Entry(mediaId: _video.Id));

        Assert.Empty(dialog.FindComponents<ComboBox<Media>>());
    }

    [Fact]
    public async Task ChoosingAVoiceover_SavesItWithTheStill()
    {
        MediaPool? saved = null;
        var dialog = RenderDialog(PoolPurpose.Ads, pool => saved = pool, Entry(mediaId: _still.Id));

        var picker = dialog.FindComponent<ComboBox<Media>>();
        await picker.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(_voice));
        Save(dialog);

        var entry = Assert.Single(saved!.Entries);
        Assert.Equal(_still.Id, entry.MediaId);
        Assert.Equal(_voice.Id, entry.AudioMediaId);
    }

    // A start point belongs to the clip it was set against.
    [Fact]
    public async Task ChangingTheVoiceover_DropsTheOldStartPoint()
    {
        MediaPool? saved = null;
        var dialog = RenderDialog(PoolPurpose.Ads, pool => saved = pool,
            Entry(mediaId: _still.Id, audioMediaId: _voice.Id, audioStart: TimeSpan.FromSeconds(4)));

        var picker = dialog.FindComponent<ComboBox<Media>>();
        await picker.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(null));
        Save(dialog);

        var entry = Assert.Single(saved!.Entries);
        Assert.Null(entry.AudioMediaId);
        Assert.Null(entry.AudioStart);
    }

    [Fact]
    public void AnEntryWithNoSound_OffersNoStartPoint()
    {
        var dialog = RenderDialog(PoolPurpose.Ads, _ => { }, Entry(mediaId: _still.Id));

        Assert.Empty(dialog.FindAll(".kh-playlist-dialog__start-input"));
    }

    [Theory]
    [InlineData("5", 5.0)]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData("25", 19.0)]
    public void SoundFrom_IsSavedAndHeldInsideTheClip(string typed, double? expectedSeconds)
    {
        MediaPool? saved = null;
        var dialog = RenderDialog(PoolPurpose.Ads, pool => saved = pool, Entry(audioMediaId: _voice.Id));

        dialog.Find(".kh-playlist-dialog__start-input").Change(typed);
        Save(dialog);

        var entry = Assert.Single(saved!.Entries);
        Assert.Equal(expectedSeconds is { } s ? TimeSpan.FromSeconds(s) : null, entry.AudioStart);
    }
}
