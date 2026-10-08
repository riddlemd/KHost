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

/// <summary>The header's shortcut to the selected venue's remote sign-ups: it shows what the venue
/// says and saves what it shows.</summary>
public class RemoteSignupsToggleTests : BunitContext
{
    private const string Toggle = ".kh-remote-signups-toggle";

    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private Venue? _saved = new() { Name = "The Lounge" };

    public RemoteSignupsToggleTests()
    {
        // A fresh copy per read, as the repository builds one, so a click cannot edit the saved venue.
        _venues.ReadSelectedVenueAsync().Returns(_ => _saved is null ? null : Copy(_saved));

        Services.AddSingleton(_venues);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Theory]
    [InlineData(true, "true", "bi-unlock", "open — click to close")]
    [InlineData(false, "false", "bi-lock-fill", "closed — click to open")]
    public void Render_ShowsTheVenuesSetting(bool open, string pressed, string icon, string says)
    {
        _saved!.Settings.AllowGuestRemote = open;

        var button = Render<RemoteSignupsToggle>().Find(Toggle);

        Assert.Equal(pressed, button.GetAttribute("aria-pressed"));
        Assert.Equal(open, button.ClassList.Contains("kh-remote-signups-toggle--on"));
        Assert.Contains(icon, button.QuerySelector("i")!.ClassList);
        Assert.EndsWith(says, button.GetAttribute("title"));
    }

    /// <summary>With no venue selected there is nothing to open or close.</summary>
    [Fact]
    public void Render_NoVenueSelected_ShowsNoButton()
    {
        _saved = null;

        Assert.Empty(Render<RemoteSignupsToggle>().FindAll(Toggle));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Click_SavesTheFlippedSettingOnTheVenue_AndShowsIt(bool open)
    {
        _saved!.Settings.AllowGuestRemote = open;
        _saved.Settings.AllowAliases = true;
        var cut = Render<RemoteSignupsToggle>();

        await cut.Find(Toggle).ClickAsync(new());

        // The rest of the venue goes back as it was read: this moves one setting, not a form.
        await _venues.Received(1).UpdateAsync(Arg.Is<Venue>(v =>
            v.Id == _saved.Id && v.Settings.AllowGuestRemote == !open && v.Settings.AllowAliases));
        Assert.Equal((!open).ToString().ToLowerInvariant(), cut.Find(Toggle).GetAttribute("aria-pressed"));
    }

    /// <summary>The Edit Venue dialog may have saved other settings since this last read the venue;
    /// the click must not put the old ones back.</summary>
    [Fact]
    public async Task Click_AfterAnotherEdit_SavesOnTheVenueAsItIsNow()
    {
        var cut = Render<RemoteSignupsToggle>();
        _saved!.Settings.AllowAliases = true;

        await cut.Find(Toggle).ClickAsync(new());

        await _venues.Received(1).UpdateAsync(Arg.Is<Venue>(v => v.Settings.AllowAliases));
    }

    [Fact]
    public async Task Click_TheSaveFails_StaysAsSaved_AndSaysSo()
    {
        _venues.UpdateAsync(Arg.Any<Venue>()).Returns(_ => throw new InvalidOperationException("disk full"));
        var cut = Render<RemoteSignupsToggle>();

        await cut.Find(Toggle).ClickAsync(new());

        Assert.Equal("true", cut.Find(Toggle).GetAttribute("aria-pressed"));
        _flash.Received(1).Show(Arg.Any<string>(), FlashType.Warning);
    }

    /// <summary>Another console, the Edit Venue dialog, or a different venue selected: this follows.</summary>
    [Fact]
    public void TheSelectedVenueChanges_ReadsItAgain()
    {
        var cut = Render<RemoteSignupsToggle>();

        _saved = new Venue { Name = "The Annex", Settings = { AllowGuestRemote = false } };
        _broker.Announce(new SelectedVenueChanged());

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(Toggle).GetAttribute("aria-pressed")));
    }

    private static Venue Copy(Venue venue) => new()
    {
        Id = venue.Id,
        Name = venue.Name,
        Settings = venue.Settings.Clone(),
    };
}
