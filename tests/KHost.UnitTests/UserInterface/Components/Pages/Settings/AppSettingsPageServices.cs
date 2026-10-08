using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>What the page's FFmpeg section injects, for fixtures testing something else on it.</summary>
internal static class AppSettingsPageServices
{
    /// <summary>What the page's Search section injects, for fixtures testing something else on it.
    /// Defaults to just Local, matching a host with no media provider plugins loaded.</summary>
    public static IMediaSearchService AddSearchSection(this IServiceCollection services, params IMediaProvider[] providers)
    {
        // Built ahead of Returns(): configuring FakeLocalProvider's own substitute inside that
        // call's argument list would consume the "last call" Returns() needs for Providers itself.
        IReadOnlyList<IMediaProvider> effective = providers.Length > 0 ? providers : [FakeLocalProvider()];

        var search = Substitute.For<IMediaSearchService>();
        search.Providers.Returns(effective);

        services.AddSingleton(search);

        return search;
    }

    /// <summary>What the page's Break music section injects, for fixtures testing something else on
    /// it. Defaults to the host's own playlists alone, active.</summary>
    public static IBreakMusicService AddBreakMusicSection(this IServiceCollection services, params IBreakMusicProvider[] providers)
    {
        // Built ahead of Returns(), for the same reason as the search section's providers.
        IReadOnlyList<IBreakMusicProvider> effective = providers.Length > 0 ? providers : [FakeBreakMusicProvider("LibraryBreakMusicProvider", "Local")];

        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(effective);
        breakMusic.LibraryProvider.Returns(effective[0]);
        breakMusic.ActiveProvider.Returns(effective[0]);

        services.AddSingleton(breakMusic);

        return breakMusic;
    }

    public static IBreakMusicProvider FakeBreakMusicProvider(string sourceName, string displayName)
    {
        var provider = Substitute.For<IBreakMusicProvider>();
        provider.SourceName.Returns(sourceName);
        provider.DisplayName.Returns(displayName);
        return provider;
    }

    public static IMediaProvider FakeProvider(string sourceName, string displayName)
    {
        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns(sourceName);
        provider.DisplayName.Returns(displayName);
        return provider;
    }

    public static IMediaProvider FakeLocalProvider() => FakeProvider(KHost.UserInterface.Services.AppSettings.LocalSearchMode, "Local");

    public static IFFmpegService AddFFmpegSection(this IServiceCollection services, FFmpegStatus? status = null)
    {
        var ffmpeg = Substitute.For<IFFmpegService>();
        ffmpeg.Status.Returns(status ?? Found());
        ffmpeg.CanInstall.Returns(true);

        var directories = Substitute.For<IHostDirectories>();
        directories.BinDirectory.Returns(Path.Combine("khost", "bin"));

        services.AddSingleton(ffmpeg);
        services.AddSingleton(directories);
        services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));

        return ffmpeg;
    }

    public static FFmpegStatus Found() => new()
    {
        FFmpeg = new FFmpegToolStatus { Tool = FFmpegTool.FFmpeg, Path = Path.Combine("khost", "bin", "ffmpeg"), Version = "9.0" },
        FFprobe = new FFmpegToolStatus { Tool = FFmpegTool.FFprobe, Path = Path.Combine("khost", "bin", "ffprobe"), Version = "9.0" },
        HasChecked = true,
    };

    public static FFmpegStatus Missing() => new()
    {
        FFmpeg = new FFmpegToolStatus { Tool = FFmpegTool.FFmpeg },
        FFprobe = new FFmpegToolStatus { Tool = FFmpegTool.FFprobe },
        HasChecked = true,
    };
}
