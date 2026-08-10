using KHost.Screen;
using KHost.Screen.OpenAl;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Screen;

// Play/Pause/Seek all call CancelFade() before they touch player state, and CancelFade used to
// restore _preFadeVolume unconditionally. Since _preFadeVolume was only ever assigned inside
// Stop(), any transport command reset the host's chosen volume back to the 1.0 default.
//
// OpenAlAudioPlayer.Volume stores its value whether or not an OpenAL device is present, so these
// assertions hold on a machine with no audio device.
public class DefaultMediaPlayerVolumeTests
{
    private static DefaultMediaPlayer MakePlayer() => new(
        new OpenAlAudioPlayer(NullLogger<OpenAlAudioPlayer>.Instance),
        NullLogger<DefaultMediaPlayer>.Instance);

    // No media is loaded, so these throw after CancelFade() has already run — which is the
    // moment the volume used to be clobbered.
    private static void InvokeIgnoringNoMedia(Action transportCommand)
    {
        try
        {
            transportCommand();
        }
        catch (InvalidOperationException)
        {
        }
    }

    [Fact]
    public void Volume_RoundTrips()
    {
        var player = MakePlayer();

        player.Volume = 0.3f;

        Assert.Equal(0.3f, player.Volume);
    }

    [Fact]
    public void Play_PreservesVolume_WhenNoFadeIsRunning()
    {
        var player = MakePlayer();
        player.Volume = 0.3f;

        InvokeIgnoringNoMedia(player.Play);

        Assert.Equal(0.3f, player.Volume);
    }

    [Fact]
    public void Pause_PreservesVolume_WhenNoFadeIsRunning()
    {
        var player = MakePlayer();
        player.Volume = 0.25f;

        InvokeIgnoringNoMedia(player.Pause);

        Assert.Equal(0.25f, player.Volume);
    }

    [Fact]
    public void Seek_PreservesVolume_WhenNoFadeIsRunning()
    {
        var player = MakePlayer();
        player.Volume = 0.4f;

        InvokeIgnoringNoMedia(() => player.Seek(TimeSpan.FromSeconds(10)));

        Assert.Equal(0.4f, player.Volume);
    }

    [Fact]
    public void RepeatedTransportCommands_DoNotDriftTheVolume()
    {
        var player = MakePlayer();
        player.Volume = 0.15f;

        for (var i = 0; i < 5; i++)
        {
            InvokeIgnoringNoMedia(player.Play);
            InvokeIgnoringNoMedia(player.Pause);
            InvokeIgnoringNoMedia(() => player.Seek(TimeSpan.Zero));
        }

        Assert.Equal(0.15f, player.Volume);
    }

    [Fact]
    public void Stop_WithNothingLoaded_PreservesVolume()
    {
        var player = MakePlayer();
        player.Volume = 0.5f;

        player.Stop(TimeSpan.FromMilliseconds(50));

        Assert.Equal(0.5f, player.Volume);
    }
}
