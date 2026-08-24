using System.Reflection;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class EditPlaylistDialogTests : BunitContext
{
    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();

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
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        var combo = dialog.FindComponent<ComboBox<Media>>();

        Assert.True(combo.Instance.MinimumSearchLength >= 3);
    }

    // The search covers the artist, so a row matched on its artist looks like a mistake when only
    // the title is shown.
    [Fact]
    public void TheMediaPicker_ShowsTheArtistBesideTheTitle()
    {
        var dialog = RenderDialog(new MediaPool { Name = "Beds" }, PoolPurpose.BreakMusic);
        var combo = dialog.FindComponent<ComboBox<Media>>();

        Assert.Equal("Elevator Jazz — Someone", combo.Instance.DisplayName(_bed));
        Assert.Equal("Happy Hour Spot", combo.Instance.DisplayName(
            new Media { FilePath = "/x.mp4", Title = "Happy Hour Spot", Artist = "" }));
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
