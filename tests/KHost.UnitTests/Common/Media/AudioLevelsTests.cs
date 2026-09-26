using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.UnitTests.Common.Media;

public class AudioLevelsTests
{
    private static AudioMix Mix(IReadOnlyDictionary<string, int>? voices = null)
        => new([], LeadVolume: 30, BackingVolume: 70) { VoiceVolumes = voices };

    [Fact]
    public void VolumeFor_TheMusic_IsAlwaysFull()
        => Assert.Equal(AudioMix.MaxVolume, Mix().VolumeFor(AudioTrackRole.Music));

    [Fact]
    public void VolumeFor_TheBacking_IsTheBackingLevel()
        => Assert.Equal(70, Mix().VolumeFor(AudioTrackRole.Backing));

    [Fact]
    public void VolumeFor_ALeadWithNoVoice_IsTheLeadLevel()
        => Assert.Equal(30, Mix(new Dictionary<string, int> { ["♂"] = 90 }).VolumeFor(AudioTrackRole.Lead));

    [Fact]
    public void VolumeFor_ALeadWhoseVoiceHasALevel_RidesAtIt()
        => Assert.Equal(90, Mix(new Dictionary<string, int> { ["♂"] = 90 })
            .VolumeFor(new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal (♂)") { Voice = "♂" }));

    [Fact]
    public void VolumeFor_ALeadWhoseVoiceHasNoLevel_FallsBackToTheLeadLevel()
        => Assert.Equal(30, Mix(new Dictionary<string, int> { ["♀"] = 90 }).VolumeFor(AudioTrackRole.Lead, "♂"));

    [Fact]
    public void VolumeFor_AMixWithNoVoiceLevelsAtAll_FallsBackToTheLeadLevel()
        => Assert.Equal(30, Mix().VolumeFor(AudioTrackRole.Lead, "♂"));

    [Fact]
    public void VolumeFor_AVoiceOnABackingTrack_IsIgnored()
    {
        // Only a lead belongs to a singer; a voice on anything else must not borrow a lead level.
        Assert.Equal(70, Mix(new Dictionary<string, int> { ["♂"] = 90 }).VolumeFor(AudioTrackRole.Backing, "♂"));
    }

    [Theory]
    [InlineData(250, 100)]
    [InlineData(-5, 0)]
    public void VolumeFor_AVoiceLevelOutsideTheRange_IsClamped(int stored, int expected)
        => Assert.Equal(expected, Mix(new Dictionary<string, int> { ["♂"] = stored }).VolumeFor(AudioTrackRole.Lead, "♂"));

    [Fact]
    public void DefaultLevels_MatchTheFamiliarConvention_BackingFullAndLeadOut()
    {
        Assert.Equal(100, AudioMix.DefaultBackingVolume);
        Assert.Equal(0, AudioMix.DefaultLeadVolume);
    }
}
