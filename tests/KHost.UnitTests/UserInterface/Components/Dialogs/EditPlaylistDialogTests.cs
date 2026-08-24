using System.Reflection;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
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
        Format = "MP3",
        Kind = MediaKind.BreakMusic,
        Status = MediaStatus.Ready,
    };

    private readonly Media _spot = new()
    {
        Id = Guid.NewGuid(),
        FilePath = "/media/spot.mp4",
        Title = "Happy Hour Spot",
        Format = "MP4",
        Kind = MediaKind.Ad,
        Status = MediaStatus.Ready,
    };

    public EditPlaylistDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // NSubstitute hands back a completed task wrapping null otherwise, and the dialog counts it.
        _media.ReadAllByKindsAsync(Arg.Any<MediaKind[]>())
            .Returns(call =>
            {
                var kinds = call.Arg<MediaKind[]>();
                return Task.FromResult<IReadOnlyList<Media>>(
                    [.. new[] { _bed, _spot }.Where(m => kinds.Contains(m.Kind))]);
            });

        _pools.ReadAllWithEntriesAsync(Arg.Any<MediaKind>(), Arg.Any<Guid?>())
            .Returns(Task.FromResult<IReadOnlyList<MediaPool>>([]));

        Services.AddSingleton(_pools);
        Services.AddSingleton(_media);
    }

    private IRenderedComponent<EditPlaylistDialog> RenderDialog(MediaPool? pool, Action<MediaPool>? onSave = null)
        => Render<EditPlaylistDialog>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Pool, pool)
            .Add(p => p.OnSave, EventCallback.Factory.Create<MediaPool>(this, saved => onSave?.Invoke(saved))));

    private static TimeSpan? ParseTime(string? text) => (TimeSpan?)typeof(EditPlaylistDialog)
        .GetMethod("ParseTime", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [text]);

    [Fact]
    public void AdFields_AreHidden_ForABreakMusicPlaylist()
    {
        var rendered = RenderDialog(new MediaPool { Name = "Beds", Kind = MediaKind.BreakMusic });

        Assert.DoesNotContain("When these ads play", rendered.Markup);
    }

    [Fact]
    public void AdFields_AreShown_ForAnAdPlaylist()
    {
        var rendered = RenderDialog(new MediaPool { Name = "Spots", Kind = MediaKind.Ad });

        Assert.Contains("When these ads play", rendered.Markup);
    }

    // Host-only takes no number, so offering one describes a schedule that is not running.
    [Fact]
    public void TheIntervalField_IsHidden_ForAHostOnlyTrigger()
    {
        var rendered = RenderDialog(new MediaPool
        {
            Name = "Spots",
            Kind = MediaKind.Ad,
            AdTrigger = AdTriggerMode.HostOnly,
        });

        Assert.Empty(rendered.FindAll("#playlist-interval"));
    }

    [Fact]
    public void TheIntervalField_IsShown_ForAnEveryNTrigger()
    {
        var rendered = RenderDialog(new MediaPool
        {
            Name = "Spots",
            Kind = MediaKind.Ad,
            AdTrigger = AdTriggerMode.EveryNPerformances,
        });

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
        }, pool => saved = pool);

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

        var rendered = RenderDialog(new MediaPool { Name = "" }, pool => saved = pool);
        rendered.Find(".kh-button--primary").Click();

        Assert.Null(saved);
    }

    // Both pickers are scoped to the kind, so switching to ads has to fetch again — otherwise the
    // dialog keeps offering bed tracks and none of the venue's ads.
    [Fact]
    public void ChangingTheKind_ReloadsTheMediaChoices()
    {
        var rendered = RenderDialog(new MediaPool { Name = "Beds", Kind = MediaKind.BreakMusic });

        Assert.Contains("Elevator Jazz", rendered.Markup);
        Assert.DoesNotContain("Happy Hour Spot", rendered.Markup);

        rendered.Find("#playlist-kind").Change(nameof(MediaKind.Ad));

        Assert.Contains("Happy Hour Spot", rendered.Markup);
        Assert.DoesNotContain("Elevator Jazz", rendered.Markup);
    }

    // Those entries came out of the other kind's library, so they are not this playlist's to keep.
    [Fact]
    public void ChangingTheKind_ClearsEntriesPickedFromTheOtherLibrary()
    {
        var rendered = RenderDialog(new MediaPool
        {
            Name = "Beds",
            Kind = MediaKind.BreakMusic,
            Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid() }],
        });

        Assert.NotEmpty(rendered.FindAll(".kh-playlist-dialog__entries tbody tr"));

        rendered.Find("#playlist-kind").Change(nameof(MediaKind.Ad));

        Assert.Empty(rendered.FindAll(".kh-playlist-dialog__entries tbody tr"));
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("0:05", 5)]
    [InlineData("1:00:00", 3600)]
    public void ParseTime_AcceptsTheFormsAHostWouldType(string text, int expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ParseTime(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    public void ParseTime_NothingUsable_IsNull(string text)
        => Assert.Null(ParseTime(text));
}
