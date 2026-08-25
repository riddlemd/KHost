using System.Reflection;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class EditPlaylistDialogTests : BunitContext
{
    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();

    private readonly Media _bed = new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/bed.mp3",
        Title = "Elevator Jazz",
        Artist = "Someone",
        Format = "MP3",
        Type = MediaType.Audio,
        Status = MediaStatus.Ready,
    };

    private readonly Media _spot = new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/spot.mp4",
        Title = "Happy Hour Spot",
        Format = "MP4",
        Type = MediaType.Video,
        Status = MediaStatus.Ready,
    };

    public EditPlaylistDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // NSubstitute hands back a completed task wrapping null otherwise, and the dialog counts it.
        _media.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<SortDescriptor?>(), Arg.Any<MediaSearchOptions?>())
            .Returns(call =>
            {
                var types = call.Arg<MediaSearchOptions?>()?.Types ?? [];
                var hits = new[] { _bed, _spot }.Where(m => types.Contains(m.Type)).ToList();

                return Task.FromResult(new PaginatedResult<Media>
                {
                    Items = hits, TotalCount = hits.Count, PageNumber = 1, PageSize = 50,
                });
            });

        _pools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(Task.FromResult<IReadOnlyList<MediaPool>>([]));

        Services.AddSingleton(_pools);
        Services.AddSingleton(_media);

        // The ad rows show the configured default as their placeholder, so the dialog reads it
        // even for a break music playlist that never renders the column.
        _appSettings.Current.Returns(new AppSettings { AdDefaultDurationSeconds = 10 });
        Services.AddSingleton(_appSettings);
    }

    // The Break Music and Ads managers each open this for their own purpose, so the dialog is
    // told which one rather than asking.
    private IRenderedComponent<EditPlaylistDialog> RenderDialog(
        MediaPool? pool, PoolPurpose purpose = PoolPurpose.BreakMusic, Action<MediaPool>? onSave = null)
        => Render<EditPlaylistDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Pool, pool)
            .Add(p => p.Purpose, purpose)
            .Add(p => p.OnSave, EventCallback.Factory.Create<MediaPool>(this, saved => onSave?.Invoke(saved))));

    private static TimeSpan? ParseTime(string? text) => (TimeSpan?)typeof(EditPlaylistDialog)
        .GetMethod("ParseTime", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [text]);

    [Fact]
    public void AdFields_AreHidden_ForABreakMusicPlaylist()
    {
        var rendered = RenderDialog(new MediaPool { Name = "Beds", Purpose = PoolPurpose.BreakMusic });

        Assert.DoesNotContain("When these ads play", rendered.Markup);
    }

    /// <summary>
    /// The entry already carried a length and nothing rendered it, so a host had no way to say how
    /// long a spot should run.
    /// </summary>
    [Fact]
    public void DurationColumn_IsShown_ForAnAdPlaylist()
    {
        var rendered = RenderDialog(WithOneEntry(PoolPurpose.Ads), PoolPurpose.Ads);

        // The unit is in the header: the field itself holds a bare number.
        Assert.Contains("Duration (sec)", rendered.FindAll("thead th").Select(th => th.TextContent.Trim()));
    }

    /// <summary>A bed runs for its own length; only an ad is cut to a slot.</summary>
    [Fact]
    public void DurationColumn_IsHidden_ForABreakMusicPlaylist()
    {
        var rendered = RenderDialog(WithOneEntry(PoolPurpose.BreakMusic));

        Assert.DoesNotContain("Duration (sec)", rendered.FindAll("thead th").Select(th => th.TextContent.Trim()));
    }

    /// <summary>
    /// The placeholder is what the row will run for if left blank. It said "video" before, which
    /// named the rule but not the number a host was deciding whether to override.
    /// </summary>
    [Fact]
    public void DurationPlaceholder_AVideo_ShowsItsOwnLength()
    {
        var video = Media("spot.mp4", "MP4", TimeSpan.FromSeconds(22));

        var rendered = RenderDialog(WithEntry(new MediaPoolEntry { MediaId = video.Id }), PoolPurpose.Ads);

        Assert.Equal("22", rendered.Find(".kh-playlist-dialog__duration").GetAttribute("placeholder"));
    }

    [Fact]
    public void DurationPlaceholder_AStillWithNothingToHear_ShowsTheConfiguredDefault()
    {
        var still = Media("card.png", "PNG", TimeSpan.FromSeconds(15));

        var rendered = RenderDialog(WithEntry(new MediaPoolEntry { MediaId = still.Id }), PoolPurpose.Ads);

        // Its stamped 15s is deliberately passed over, exactly as AdService passes it over.
        Assert.Equal("10", rendered.Find(".kh-playlist-dialog__duration").GetAttribute("placeholder"));
    }

    /// <summary>The picture and the words finish together, so that is the length to show.</summary>
    [Fact]
    public void DurationPlaceholder_AStillWithAVoiceover_ShowsWhatIsLeftOfTheVoiceover()
    {
        var still = Media("card.png", "PNG", TimeSpan.FromSeconds(15));
        var voice = Media("voice.mp3", "MP3", TimeSpan.FromSeconds(25));

        var entry = new MediaPoolEntry
        {
            MediaId = still.Id,
            AudioMediaId = voice.Id,
            AudioStart = TimeSpan.FromSeconds(5),
        };

        var rendered = RenderDialog(WithEntry(entry), PoolPurpose.Ads);

        Assert.Equal("20", rendered.Find(".kh-playlist-dialog__duration").GetAttribute("placeholder"));
    }

    [Fact]
    public void DurationValue_TheEntrySaysHowLong_ShowsThatRatherThanThePlaceholder()
    {
        var video = Media("spot.mp4", "MP4", TimeSpan.FromSeconds(22));
        var entry = new MediaPoolEntry { MediaId = video.Id, Duration = TimeSpan.FromSeconds(7) };

        var rendered = RenderDialog(WithEntry(entry), PoolPurpose.Ads);

        Assert.Equal("7", rendered.Find(".kh-playlist-dialog__duration").GetAttribute("value"));
    }

    /// <summary>Registers the row so the dialog can read its format and length back by id.</summary>
    private Media Media(string title, string format, TimeSpan? duration)
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = $"/media/{title}",
            Title = title,
            Format = format,
            Duration = duration,
            Status = MediaStatus.Ready,
        };

        _media.ReadAsync(media.Id).Returns(media);

        return media;
    }

    /// <summary>
    /// Choosing fills the field and nothing more. A host who picks the wrong row types again,
    /// rather than deleting a line they never meant to make.
    /// </summary>
    [Fact]
    public async Task ChoosingARow_DoesNotAddIt()
    {
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        var combo = OpenPicker(dialog);

        var row = (await combo.Instance.Search!("jazz")).First();
        await combo.InvokeAsync(() => combo.Instance.ValueChanged.InvokeAsync(row));

        Assert.Empty(dialog.FindAll("tbody tr"));
    }

    [Fact]
    public async Task PressingAdd_AddsTheChosenRowAndCollapsesAgain()
    {
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        var combo = OpenPicker(dialog);

        var row = (await combo.Instance.Search!("jazz")).First();
        await combo.InvokeAsync(() => combo.Instance.ValueChanged.InvokeAsync(row));

        dialog.FindAll("button").First(button => button.TextContent.Contains("Add")).Click();

        Assert.Single(dialog.FindAll("tbody tr"));

        // Back to the button: the row it made is the confirmation.
        Assert.Empty(dialog.FindComponents<ComboBox<EditPlaylistDialog.AddChoice>>());
    }

    [Fact]
    public void TheAddControl_RestsAsASingleButton()
    {
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);

        Assert.Empty(dialog.FindComponents<ComboBox<EditPlaylistDialog.AddChoice>>());
        Assert.Contains(dialog.FindAll("button"), button => button.TextContent.Contains("Add entry"));
    }

    /// <summary>The picker is revealed, not resident — nothing to find until Add entry is pressed.</summary>
    private static IRenderedComponent<ComboBox<EditPlaylistDialog.AddChoice>> OpenPicker(
        IRenderedComponent<EditPlaylistDialog> dialog)
    {
        dialog.FindAll("button").First(button => button.TextContent.Contains("Add entry")).Click();

        return dialog.FindComponent<ComboBox<EditPlaylistDialog.AddChoice>>();
    }

    private static MediaPool WithEntry(MediaPoolEntry entry)
    {
        entry.Id = Guid.NewGuid();

        return new MediaPool { Name = "Spots", Purpose = PoolPurpose.Ads, Entries = [entry] };
    }

    private static MediaPool WithOneEntry(PoolPurpose purpose) => new()
    {
        Name = purpose == PoolPurpose.Ads ? "Spots" : "Beds",
        Purpose = purpose,
        Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Position = 0 }],
    };

    [Fact]
    public void AdFields_AreShown_ForAnAdPlaylist()
    {
        var rendered = RenderDialog(new MediaPool { Name = "Spots" }, PoolPurpose.Ads);

        Assert.Contains("When these ads play", rendered.Markup);
    }

    // Host-only takes no number, so offering one describes a schedule that is not running.
    [Fact]
    public void TheIntervalField_IsHidden_ForAHostOnlyTrigger()
    {
        var rendered = RenderDialog(new MediaPool
        {
            Name = "Spots",
            AdTrigger = AdTriggerMode.HostOnly,
        }, PoolPurpose.Ads);

        Assert.Empty(rendered.FindAll("#playlist-interval"));
    }

    [Fact]
    public void TheIntervalField_IsShown_ForAnEveryNTrigger()
    {
        var rendered = RenderDialog(new MediaPool
        {
            Name = "Spots",
            AdTrigger = AdTriggerMode.EveryNPerformances,
        }, PoolPurpose.Ads);

        Assert.NotEmpty(rendered.FindAll("#playlist-interval"));
    }

    [Fact]
    public void WeightColumn_IsShown_OnlyForAWeightedPlaylist()
    {
        var weighted = RenderDialog(new MediaPool
        {
            Name = "Beds",
            SelectionMode = PoolSelectionMode.Weighted,
            Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid() }],
        });

        Assert.Contains("Weight", weighted.Markup);

        var shuffled = RenderDialog(new MediaPool
        {
            Name = "Beds",
            SelectionMode = PoolSelectionMode.Shuffle,
            Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid() }],
        });

        Assert.DoesNotContain(">Weight<", shuffled.Markup);
    }

    [Fact]
    public void Save_ReportsThePlaylistWithItsEntriesRenumbered()
    {
        MediaPool? saved = null;

        var rendered = RenderDialog(new MediaPool
        {
            Name = "Beds",
            Entries =
            [
                new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Position = 7 },
                new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Position = 3 },
            ],
        }, PoolPurpose.BreakMusic, pool => saved = pool);

        rendered.Find(".kh-button--primary").Click();

        Assert.NotNull(saved);
        Assert.Equal([0, 1], saved!.Entries.Select(e => e.Position));
    }

    // Cancel has to leave the stored playlist alone, so the dialog edits a copy.
    [Fact]
    public void Editing_DoesNotMutateTheStoredPlaylist()
    {
        var stored = new MediaPool
        {
            Name = "Beds",
            Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Position = 5 }],
        };

        var rendered = RenderDialog(stored);
        rendered.Find(".kh-button--primary").Click();

        Assert.Equal(5, stored.Entries[0].Position);
    }

    [Fact]
    public void Save_WithNoName_ReportsNothing()
    {
        MediaPool? saved = null;

        var rendered = RenderDialog(new MediaPool { Name = "" }, PoolPurpose.BreakMusic, pool => saved = pool);
        rendered.Find(".kh-button--primary").Click();

        Assert.Null(saved);
    }

    // The page owns the purpose, so the dialog saves whatever it was opened for.
    [Fact]
    public void Save_ReportsThePurposeThePageOpenedItFor()
    {
        MediaPool? saved = null;

        var rendered = RenderDialog(new MediaPool { Name = "Spots" }, PoolPurpose.Ads, pool => saved = pool);
        rendered.Find(".kh-button--primary").Click();

        Assert.Equal(PoolPurpose.Ads, saved?.Purpose);
    }

    private static async Task<IReadOnlyList<Media>> SearchThrough(IRenderedComponent<EditPlaylistDialog> dialog, string term)
        => await (Task<IReadOnlyList<Media>>)typeof(EditPlaylistDialog)
            .GetMethod("SearchMediaAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(dialog.Instance, [term])!;

    // An ad is a video, a sound or a still; break music is a record. Never the karaoke library —
    // those are backing tracks with no singer on them.
    [Fact]
    public async Task TheMediaPicker_SearchesOnlyWhatThePurposeCanUse()
    {
        var beds = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        await SearchThrough(beds, "any");

        await _media.Received().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<SortDescriptor?>(),
            Arg.Is<MediaSearchOptions>(o => o.Types!.SequenceEqual(new[] { MediaType.Audio })));

        _media.ClearReceivedCalls();

        var spots = RenderDialog(new MediaPool { Name = "Spots" }, PoolPurpose.Ads);
        await SearchThrough(spots, "any");

        await _media.Received().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<SortDescriptor?>(),
            Arg.Is<MediaSearchOptions>(o =>
                o.Types!.SequenceEqual(new[] { MediaType.Video, MediaType.Audio, MediaType.Image })));
    }

    // Two characters cannot reach the trigram index, so they fall to a substring match that hits
    // the artist as well — typing "ti" once returned every row, because every artist is "…artist".
    [Fact]
    public void TheMediaPicker_DoesNotSearchBelowTheTrigramMinimum()
    {
        var combo = OpenPicker(RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic));

        Assert.True(combo.Instance.MinimumSearchLength >= 3);
    }

    // The search covers the artist, so a row matched on its artist looks like a mistake when only
    // the title is shown.
    [Fact]
    public async Task TheMediaPicker_ShowsTheArtistBesideTheTitle()
    {
        var combo = OpenPicker(RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic));

        // Driven through the search, so this is the label the picker actually offers rather than
        // one the test handed to DisplayName itself.
        var rows = await combo.Instance.Search!("jazz");

        Assert.Contains(rows, row => row.Label == "Elevator Jazz — Someone");
    }

    [Fact]
    public async Task TheMediaPicker_WithNoArtistToShow_ShowsJustTheTitle()
    {
        var combo = OpenPicker(RenderDialog(new MediaPool { Name = "Spots" }, PoolPurpose.Ads));

        var rows = await combo.Instance.Search!("happy");

        Assert.Contains(rows, row => row.Label == "Happy Hour Spot");
    }

    /// <summary>
    /// One field over two lists, so the menu has to say which is which — and the box draws a
    /// heading wherever the group changes, never reordering, so media has to come first.
    /// </summary>
    [Fact]
    public async Task ThePicker_OffersMediaAndPlaylistsUnderTheirOwnHeadings()
    {
        // Stubbed before rendering: the dialog reads the playlists once, as it opens.
        _pools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(Task.FromResult<IReadOnlyList<MediaPool>>(
                [new MediaPool { Id = Guid.NewGuid(), Name = "House Spots" }]));

        var combo = OpenPicker(RenderDialog(new MediaPool { Name = "Spots" }, PoolPurpose.Ads));

        var groups = (await combo.Instance.Search!("")).Select(row => row.Group).ToList();

        Assert.Contains("Media", groups);
        Assert.Contains("Playlists", groups);
        Assert.True(groups.IndexOf("Media") < groups.IndexOf("Playlists"), "media has to be listed first");
    }

    // Capped: the box is for finding one row, and a thousand of them help nobody.
    [Fact]
    public async Task TheMediaPicker_CapsWhatItAsksFor()
    {
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        await SearchThrough(dialog, "any");

        await _media.Received().SearchAsync(Arg.Any<string>(), 1, 50,
            Arg.Any<SortDescriptor?>(), Arg.Any<MediaSearchOptions>());
    }

    // Never the whole library: preloading every row was fine at four and is not at a few thousand.
    [Fact]
    public void TheDialog_NeverBulkReadsTheLibrary()
    {
        RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);

        _media.DidNotReceive().ReadAllByTypesAsync(Arg.Any<MediaType[]>());
    }

}
