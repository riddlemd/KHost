using System.Text.Json;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.UserInterface.Services;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-settings-{Guid.NewGuid():n}");
    private readonly IUsersService _users = Substitute.For<IUsersService>();

    private AppSettingsService Service(params KeyValuePair<string, string?>[] config)
        => new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), _users, _directory);

    [Fact]
    public async Task SaveAsync_WritesAConfigShapedOverlay()
    {
        var service = Service();

        var result = await service.SaveAsync(new AppSettings { RequireLogin = false, SegmentSeconds = 4 });

        Assert.True(result.Saved);
        using var overlay = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
        Assert.False(overlay.RootElement.GetProperty("Auth").GetProperty("RequireLogin").GetBoolean());
        Assert.Equal(4, overlay.RootElement.GetProperty("MediaStream").GetProperty("SegmentSeconds").GetInt32());
        Assert.Equal("00:00:05", overlay.RootElement.GetProperty("Playback").GetProperty("StopFadeDuration").GetString());
    }

    [Fact]
    public async Task SaveAsync_RefusesRequiringLogin_WhileNoAdminHasAPassword()
    {
        _users.HasAdminWithPasswordAsync().Returns(false);
        var service = Service(new KeyValuePair<string, string?>("Auth:RequireLogin", "false"));

        var result = await service.SaveAsync(new AppSettings { RequireLogin = true });

        Assert.False(result.Saved);
        Assert.Contains("lock everyone out", result.Error);
        Assert.False(File.Exists(Path.Combine(_directory, AppSettingsService.OverlayFileName)));
    }

    [Fact]
    public async Task SaveAsync_AllowsRequiringLogin_OnceAnAdminHasAPassword()
    {
        _users.HasAdminWithPasswordAsync().Returns(true);
        var service = Service(new KeyValuePair<string, string?>("Auth:RequireLogin", "false"));

        var result = await service.SaveAsync(new AppSettings { RequireLogin = true });

        Assert.True(result.Saved);
    }

    [Fact]
    public async Task SaveAsync_FlagsARestart_OnlyForTheFfmpegPath()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { RequireLogin = false });
        Assert.False(service.RestartRequired);

        await service.SaveAsync(new AppSettings { FFmpegPath = "/opt/ffmpeg" });
        Assert.True(service.RestartRequired);
    }

    [Fact]
    public async Task SaveAsync_LeavesRestartAlone_ForLiveSettings()
    {
        var service = Service();

        await service.SaveAsync(new AppSettings { StopFadeSeconds = 3, SegmentSeconds = 4 });

        Assert.False(service.RestartRequired);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
