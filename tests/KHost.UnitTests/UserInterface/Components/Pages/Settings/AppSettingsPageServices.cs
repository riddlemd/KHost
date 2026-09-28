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
