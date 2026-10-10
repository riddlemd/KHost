using KHost.Domain.Services.BreakMusic;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.BreakMusic;

public class BreakMusicSettingsTests
{
    private readonly IOptionsMonitor<BreakMusicService.ServiceOptions> _options = Substitute.For<IOptionsMonitor<BreakMusicService.ServiceOptions>>();
    private BreakMusicService.ServiceOptions _current = new();

    public BreakMusicSettingsTests() => _options.CurrentValue.Returns(_ => _current);

    [Fact]
    public void FadeDuration_Unset_IsTheDefault()
    {
        Assert.Equal(BreakMusicService.ServiceOptions.DefaultFadeDuration, new BreakMusicSettings(_options).FadeDuration);
    }

    /// <summary>App Settings promises the change applies immediately, so a plugin holding the
    /// settings object must see a save without being rebuilt.</summary>
    [Fact]
    public void FadeDuration_FollowsASaveMadeAfterConstruction()
    {
        var settings = new BreakMusicSettings(_options);

        _current = new() { FadeDuration = TimeSpan.FromSeconds(4) };

        Assert.Equal(TimeSpan.FromSeconds(4), settings.FadeDuration);
    }

    [Fact]
    public void FadeDuration_Negative_ReadsAsZero()
    {
        _current = new() { FadeDuration = TimeSpan.FromSeconds(-1) };

        Assert.Equal(TimeSpan.Zero, new BreakMusicSettings(_options).FadeDuration);
    }
}
