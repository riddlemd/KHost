using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>The page both AdsManagerPage and BreakMusicManagerPage wrap. Purpose-agnostic here;
/// the "In use" badge and the trigger column are what those two pages differ on.</summary>
public class PlaylistManagerTests : BunitContext
{
    private const string BadgeSelector = ".kh-badge--secondary";
    private const string TriggerHeaderSelector = "thead th:nth-child(4)";
    private const string DeleteSelector = ".kh-table__cell--actions button:last-child";

    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private readonly Guid _venueId = Guid.NewGuid();

    public PlaylistManagerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_pools);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);

        // The page always renders its EditPlaylistDialog, closed or not, which resolves these too.
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IAppSettingsService>());
    }

    private MediaPool Pool(string name, Guid id) => new()
    {
        Id = id,
        Name = name,
        Purpose = PoolPurpose.Ads,
        Entries = [],
    };

    private void WithVenue(Guid? adPoolId)
        => _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Id = _venueId,
            Name = "The Room",
            Settings = new Venue.VenueSettings { AdPoolId = adPoolId },
        });

    private IRenderedComponent<PlaylistManager> RenderAds()
        => Render<PlaylistManager>(parameters => parameters
            .Add(p => p.Purpose, PoolPurpose.Ads)
            .Add(p => p.Title, "Ads Manager")
            .Add(p => p.Icon, "megaphone")
            .Add(p => p.EmptyText, "No ad playlists yet.")
            .Add(p => p.DeleteWarning, "Any venue using it stops running ads.")
            .Add(p => p.ShowTrigger, true)
            .Add(p => p.ActivePoolOf, (Func<Venue.VenueSettings, Guid?>)(s => s.AdPoolId)));

    [Fact]
    public void ActivePool_ShowsTheInUseBadge()
    {
        var poolId = Guid.NewGuid();
        WithVenue(poolId);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("House Specials", poolId) });

        var cut = RenderAds();

        Assert.Single(cut.FindAll(BadgeSelector));
    }

    [Fact]
    public void NoActivePool_ShowsNoInUseBadge()
    {
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("House Specials", Guid.NewGuid()) });

        var cut = RenderAds();

        Assert.Empty(cut.FindAll(BadgeSelector));
    }

    /// <summary>The bug W7 fixes: the badge used to read whatever the venue was on first render and
    /// never again, so editing the venue elsewhere left it pointing at the wrong pool.</summary>
    [Fact]
    public async Task VenuesChanged_RefreshesTheActivePool()
    {
        var firstPool = Guid.NewGuid();
        var secondPool = Guid.NewGuid();
        WithVenue(firstPool);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("First", firstPool), Pool("Second", secondPool) });

        var cut = RenderAds();
        Assert.Contains("First", cut.Find(BadgeSelector).ParentElement!.TextContent);

        WithVenue(secondPool);
        await _broker.PublishAsync(new VenuesChanged());

        cut.WaitForAssertion(() => Assert.Contains("Second", cut.Find(BadgeSelector).ParentElement!.TextContent));
    }

    [Fact]
    public async Task SelectedVenueChanged_RefreshesTheActivePool()
    {
        var poolId = Guid.NewGuid();
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, Arg.Any<Guid?>())
            .Returns(new List<MediaPool> { Pool("Only Playlist", poolId) });

        var cut = RenderAds();
        Assert.Empty(cut.FindAll(BadgeSelector));

        WithVenue(poolId);
        await _broker.PublishAsync(new SelectedVenueChanged());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(BadgeSelector)));
    }

    [Fact]
    public void ShowTriggerTrue_RendersTheTriggerColumn()
    {
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("House Specials", Guid.NewGuid()) });

        var cut = RenderAds();

        Assert.Contains("They play", cut.Find("thead").TextContent);
    }

    [Fact]
    public void ShowTriggerFalse_OmitsTheTriggerColumn()
    {
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, _venueId)
            .Returns(new List<MediaPool> { Pool("Lounge Mix", Guid.NewGuid()) });

        var cut = Render<PlaylistManager>(parameters => parameters
            .Add(p => p.Purpose, PoolPurpose.BreakMusic)
            .Add(p => p.Title, "Break Music Playlists")
            .Add(p => p.Icon, "music-note-beamed")
            .Add(p => p.EmptyText, "No break music playlists yet.")
            .Add(p => p.DeleteWarning, "Any venue using it falls back to silence.")
            .Add(p => p.ShowTrigger, false)
            .Add(p => p.ActivePoolOf, (Func<Venue.VenueSettings, Guid?>)(s => s.BreakMusicPoolId)));

        Assert.DoesNotContain("They play", cut.Find("thead").TextContent);
    }

    [Fact]
    public void ClickingDelete_AsksBeforeDeleting()
    {
        var poolId = Guid.NewGuid();
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("House Specials", poolId) });

        var cut = RenderAds();
        cut.Find(DeleteSelector).Click();

        _dialogs.Received(1).ShowConfirmationAsync(
            Arg.Is<string>(m => m.Contains("House Specials") && m.Contains("stops running ads")),
            Arg.Any<Func<Task>>(),
            "Delete Playlist",
            "Delete",
            Arg.Any<Action?>(),
            Arg.Any<Action?>());

        _pools.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ConfirmingDelete_DeletesThePool()
    {
        var poolId = Guid.NewGuid();
        WithVenue(null);
        _pools.ReadAllWithEntriesAsync(PoolPurpose.Ads, _venueId)
            .Returns(new List<MediaPool> { Pool("House Specials", poolId) });

        Func<Task>? onConfirm = null;
        _dialogs.ShowConfirmationAsync(
                Arg.Any<string>(), Arg.Do<Func<Task>>(f => onConfirm = f),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(Task.FromResult(true));

        var cut = RenderAds();
        cut.Find(DeleteSelector).Click();

        await cut.InvokeAsync(() => onConfirm!());

        await _pools.Received(1).DeleteAsync(poolId);
    }
}
