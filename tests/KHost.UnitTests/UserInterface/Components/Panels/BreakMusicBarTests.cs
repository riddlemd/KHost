using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using KHost.UserInterface.Components.Panels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// Driven through real clicks. A handler that exists but is attached to nothing passes every test
/// that calls it directly, which is how the queue's arrow keys once sat dead behind tooltips
/// advertising them.
/// </summary>
public class BreakMusicBarTests : BunitContext
{
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IAdService _ads = Substitute.For<IAdService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly IBreakMusicProvider _provider = Substitute.For<IBreakMusicProvider>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public BreakMusicBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _breakMusic.ActiveProvider.Returns(_provider);
        _breakMusic.Providers.Returns([_provider]);
        _breakMusic.State.Returns(BreakMusicState.Stopped);
        _breakMusic.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _ads.PlayNowAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_ads);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    private IRenderedComponent<BreakMusicBar> Render() => Render<BreakMusicBar>();

    [Fact]
    public void WithNoProvider_TheBarIsNotRendered()
    {
        _breakMusic.ActiveProvider.Returns((IBreakMusicProvider?)null);

        Assert.Empty(Render().FindAll(".kh-break-music-bar"));
    }

    [Fact]
    public void PlayButton_WhenStopped_StartsBreakMusic()
    {
        Render().Find("[title='Play break music']").Click();

        _breakMusic.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    // Resume, not restart: a host who paused for an announcement should not lose their place in
    // the playlist.
    [Fact]
    public void PlayButton_WhenPaused_ResumesRatherThanRestarting()
    {
        _breakMusic.State.Returns(BreakMusicState.Paused);

        Render().Find("[title='Play break music']").Click();

        _breakMusic.Received(1).ResumeAsync(Arg.Any<CancellationToken>());
        _breakMusic.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PauseButton_WhilePlaying_PausesBreakMusic()
    {
        _breakMusic.State.Returns(BreakMusicState.Playing);

        Render().Find("[title='Pause break music']").Click();

        _breakMusic.Received(1).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SkipButton_WhilePlaying_SkipsTheTrack()
    {
        _breakMusic.State.Returns(BreakMusicState.Playing);

        Render().Find("[title='Skip to the next track']").Click();

        _breakMusic.Received(1).SkipAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SkipButton_WhenStopped_IsDisabled()
    {
        var skip = Render().Find("[title='Skip to the next track']");

        Assert.True(skip.HasAttribute("disabled"));
    }

    // One venue level covers every channel, so the bar carries no fader of its own.
    [Fact]
    public void TheBar_HasNoVolumeControl()
        => Assert.Empty(Render().FindAll("input[type=range]"));

    [Fact]
    public void CurrentTrack_IsShown()
    {
        _provider.CurrentTrack.Returns(new BreakMusicTrack { Title = "Elevator Jazz", Artist = "Someone" });
        _breakMusic.CurrentTrack.Returns(new BreakMusicTrack { Title = "Elevator Jazz", Artist = "Someone" });

        var markup = Render().Markup;

        Assert.Contains("Elevator Jazz", markup);
        Assert.Contains("Someone", markup);
    }

    [Fact]
    public void AdButton_IsHidden_WhenTheVenueRunsNoAds()
    {
        _ads.IsConfigured.Returns(false);

        Assert.Empty(Render().FindAll("[title='Play an ad now']"));
    }

    [Fact]
    public void AdButton_WhenConfigured_PlaysAnAd()
    {
        _ads.IsConfigured.Returns(true);

        Render().Find("[title='Play an ad now']").Click();

        _ads.Received(1).PlayNowAsync(Arg.Any<CancellationToken>());
    }

    // Otherwise the button looks broken and the fix is two pages away.
    [Fact]
    public void PlayButton_WithNothingToPlay_SaysSo()
    {
        _breakMusic.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        Render().Find("[title='Play break music']").Click();

        // Both causes named: a missing playlist and a missing screen are indistinguishable here.
        _flash.Received(1).Show(
            Arg.Is<string>(m => m.Contains("playlist") && m.Contains("screen")), FlashType.Warning);
    }

    [Fact]
    public void AdButton_WhenNothingPlays_SaysSo()
    {
        _ads.IsConfigured.Returns(true);
        _ads.PlayNowAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        Render().Find("[title='Play an ad now']").Click();

        _flash.Received(1).Show(Arg.Any<string>(), FlashType.Warning);
    }

    [Fact]
    public async Task StateChanged_RedrawsTheBar()
    {
        var rendered = Render();

        _breakMusic.State.Returns(BreakMusicState.Playing);
        await _broker.PublishAsync(new BreakMusicChanged());

        rendered.WaitForAssertion(() => Assert.NotEmpty(rendered.FindAll("[title='Pause break music']")));
    }
}
