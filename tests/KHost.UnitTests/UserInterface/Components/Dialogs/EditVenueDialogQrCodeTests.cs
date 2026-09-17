using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Abstractions.Models.Plugins;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Nothing else sets these JSON fields, so this dialog is the whole reachable surface.</summary>
public class EditVenueDialogQrCodeTests : BunitContext
{
    private const string SourceSelector = "#venue-qr-source";
    private const string CornerSelector = "#venue-qr-corner";
    private const string SizeSelector = "#venue-qr-size";
    private const string HideSelector = "#venue-qr-hide-during-song";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IPluginRegistry _plugins = Substitute.For<IPluginRegistry>();

    /// <summary>A plugin that declared itself a source, whether or not it has a code right now.</summary>
    private static DiscoveredPlugin Source(Guid id, string name, string? label)
        => new()
        {
            Directory = $"/plugins/{name}",
            Manifest = new PluginManifest
            {
                Id = id,
                Name = name,
                Version = "1.0.0",
                EntryAssembly = $"{name}.dll",
                ApiVersion = PluginApi.CurrentVersion,
                QrCode = new PluginQrCodeDefinition { Label = label },
            },
        };

    public EditVenueDialogQrCodeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_plugins);

        _plugins.Plugins.Returns([]);
    }

    /// <summary>A code invites a room to scan it, so it goes up only because someone chose it.</summary>
    [Fact]
    public void AVenueThatHasNeverBeenAsked_ShowsNoCode()
    {
        _plugins.Plugins.Returns([Source(Guid.NewGuid(), "Example", "Guest sign-up")]);

        var cut = Render(new Venue.VenueSettings());

        // A venue that has never chosen stores null, which the select renders as its None option.
        Assert.True(string.IsNullOrEmpty(cut.Find(SourceSelector).GetAttribute("value")));
        Assert.Empty(cut.FindAll(CornerSelector));
        Assert.Empty(cut.FindAll(SizeSelector));
    }

    /// <summary>Read from the manifest, not what registered: Example has no code until sign-in.</summary>
    [Fact]
    public void ADeclaredSource_IsOfferedBeforeItHasAnyCodeToGive()
    {
        _plugins.Plugins.Returns([Source(Guid.NewGuid(), "Example", "Guest sign-up")]);

        var options = Render(new Venue.VenueSettings()).FindAll($"{SourceSelector} option");

        Assert.Contains(options, option => option.TextContent.Contains("Guest sign-up"));
    }

    /// <summary>A plugin that declared a source without naming it still has to be pickable.</summary>
    [Fact]
    public void ASourceWithNoLabel_IsNamedAfterItsPlugin()
    {
        _plugins.Plugins.Returns([Source(Guid.NewGuid(), "Tip Jar", label: null)]);

        var options = Render(new Venue.VenueSettings()).FindAll($"{SourceSelector} option");

        Assert.Contains(options, option => option.TextContent.Contains("Tip Jar"));
    }

    [Fact]
    public void ChoosingASource_ReachesTheSavedVenue()
    {
        var id = Guid.NewGuid();
        _plugins.Plugins.Returns([Source(id, "Example", "Guest sign-up")]);
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(SourceSelector).Change(id.ToString());
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(id.ToString(), saved!.Settings.QrCodeSource);
    }

    /// <summary>Choosing None is how a venue turns the feature off, and it has to persist.</summary>
    [Fact]
    public void ChoosingNone_ReachesTheSavedVenue()
    {
        var id = Guid.NewGuid();
        _plugins.Plugins.Returns([Source(id, "Example", "Guest sign-up")]);
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { QrCodeSource = id.ToString() }, venue => saved = venue);

        cut.Find(SourceSelector).Change(string.Empty);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.True(string.IsNullOrEmpty(saved!.Settings.QrCodeSource));
    }

    /// <summary>A value matching no option renders blank, hiding what the next pick would replace.</summary>
    [Fact]
    public void ASourceWhosePluginIsGone_StaysSelectedAndSaysSo()
    {
        _plugins.Plugins.Returns([]);

        var cut = Render(new Venue.VenueSettings { QrCodeSource = "6f1d1f6e-0000-0000-0000-000000000000" });

        Assert.Equal("6f1d1f6e-0000-0000-0000-000000000000", cut.Find(SourceSelector).GetAttribute("value"));
        Assert.Contains(cut.FindAll(".kh-note--warning"),
            note => note.TextContent.Contains("No installed plugin provides this code"));
    }

    /// <summary>A select can't show null "no preference", so it offers what a code would take.</summary>
    [Fact]
    public void AVenueThatHasNeverBeenAsked_ShowsWhatACodeWouldTakeAnyway()
    {
        var cut = Render(new Venue.VenueSettings { QrCodeSource = "any" });

        Assert.Equal(nameof(ScreenCorner.BottomRight), cut.Find(CornerSelector).GetAttribute("value"));
        Assert.Equal(nameof(ScreenQrSize.Medium), cut.Find(SizeSelector).GetAttribute("value"));
        Assert.False(cut.Find(HideSelector).HasAttribute("checked"));
    }

    [Fact]
    public void AVenuesOwnChoices_AreWhatItSeesAgain()
    {
        var cut = Render(new Venue.VenueSettings
        {
            QrCodeSource = "any",
            QrCodeCorner = ScreenCorner.TopLeft,
            QrCodeSize = ScreenQrSize.Large,
            QrCodeHideDuringSong = true,
        });

        Assert.Equal(nameof(ScreenCorner.TopLeft), cut.Find(CornerSelector).GetAttribute("value"));
        Assert.Equal(nameof(ScreenQrSize.Large), cut.Find(SizeSelector).GetAttribute("value"));
        Assert.True(cut.Find(HideSelector).HasAttribute("checked"));
    }

    [Fact]
    public void WhatTheHostPicks_ReachesTheSavedVenue()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { QrCodeSource = "any" }, venue => saved = venue);

        cut.Find(CornerSelector).Change(nameof(ScreenCorner.TopRight));
        cut.Find(SizeSelector).Change(nameof(ScreenQrSize.Small));
        cut.Find(HideSelector).Change(true);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(ScreenCorner.TopRight, saved!.Settings.QrCodeCorner);
        Assert.Equal(ScreenQrSize.Small, saved.Settings.QrCodeSize);
        Assert.True(saved.Settings.QrCodeHideDuringSong);
    }

    /// <summary>Saving without a choice must write the values shown, not a placement it ignores.</summary>
    [Fact]
    public void SavingWithoutTouchingThem_WritesWhatWasOffered()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(ScreenCorner.BottomRight, saved!.Settings.QrCodeCorner);
        Assert.Equal(ScreenQrSize.Medium, saved.Settings.QrCodeSize);
    }

    private IRenderedComponent<EditVenueDialog> Render(
        Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings })
            .Add(p => p.OnSave, venue => onSave?.Invoke(venue)));
}
