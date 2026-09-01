using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.UnitTests.Common.Media;

/// <summary>
/// Roles come from the track's name, never its position. The file this was built against orders
/// them Instrumental, Backing Vocal, Lead Vocal — so reading position would put the singer's own
/// part on the control marked backing, and a host would mute the wrong voice.
/// </summary>
public class AudioTrackRolesTests
{
    [Theory]
    [InlineData("Instrumental")]
    [InlineData("instrumental")]
    [InlineData("Music")]
    [InlineData("Karaoke")]
    [InlineData("Backing Track")]
    public void RoleFor_ReadsTheMusic(string name)
        => Assert.Equal(AudioTrackRole.Music, AudioTrackRoles.FromTrackName(name));

    [Theory]
    [InlineData("Lead Vocal")]
    [InlineData("LEAD")]
    [InlineData("Vocal")]
    [InlineData("Vocals")]
    public void RoleFor_ReadsTheLead(string name)
        => Assert.Equal(AudioTrackRole.Lead, AudioTrackRoles.FromTrackName(name));

    [Theory]
    [InlineData("Backing Vocal")]
    [InlineData("Backup Vocals")]
    [InlineData("Harmony")]
    [InlineData("Choir")]
    public void RoleFor_ReadsTheBacking(string name)
        => Assert.Equal(AudioTrackRole.Backing, AudioTrackRoles.FromTrackName(name));

    [Fact]
    public void RoleFor_TellsABackingTrackFromABackingVocal()
    {
        // One word apart and opposite meanings: the track is the music the singer sings over, the
        // vocal is a voice riding on it.
        Assert.Equal(AudioTrackRole.Music, AudioTrackRoles.FromTrackName("Backing Track"));
        Assert.Equal(AudioTrackRole.Backing, AudioTrackRoles.FromTrackName("Backing Vocal"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Stereo")]
    [InlineData("Audio Track 2")]
    public void RoleFor_SaysNothing_WhenTheNameDoesNot(string? name)
        => Assert.Null(AudioTrackRoles.FromTrackName(name));

    [Fact]
    public void IsMixable_NeedsMusicAndAtLeastOneVoice()
    {
        var music = new AudioTrack(0, AudioTrackRole.Music, "Instrumental");
        var lead = new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal");

        // Nothing to set a voice against, and nothing to set against the music.
        Assert.False(new AudioMix([music], 0, 100).IsMixable);
        Assert.False(new AudioMix([lead], 0, 100).IsMixable);
        Assert.True(new AudioMix([music, lead], 0, 100).IsMixable);
    }
}
