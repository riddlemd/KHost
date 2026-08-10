using KHost.Screen;
using KHost.Screen.OpenAl;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Screen;

// Play/Pause/Seek all run CancelFade() first, so the fade baseline must never be written back
// unless a fade ran. Volume round-trips without OpenAL, so these pass with no audio device.
public class DefaultMediaPlayerVolumeTests
{
    private static DefaultMediaPlayer MakePlayer() => new(
        new OpenAlAudioPlayer(NullLogger<OpenAlAudioPlayer>.Instance),
        NullLogger<DefaultMediaPlayer>.Instance);

    // With no media loaded these throw, but only after CancelFade() has already run.
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
