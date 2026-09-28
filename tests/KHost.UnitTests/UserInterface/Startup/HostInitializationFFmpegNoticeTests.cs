using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Startup;

/// <summary>The startup warning that FFmpeg is missing comes down once an install makes it untrue.
/// The setup wizard draws no flash banner to count it down, so otherwise it greets the host on the
/// console after the wizard has already installed FFmpeg.</summary>
public class HostInitializationFFmpegNoticeTests
{
    private const string Notice = "FFmpeg and FFprobe could not be found, so songs will not play. Install FFmpeg from App Settings.";

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IFFmpegService _ffmpeg = Substitute.For<IFFmpegService>();
    private readonly FlashService _flash;

    public HostInitializationFFmpegNoticeTests()
    {
        _flash = new FlashService(_broker);
        _ffmpeg.Status.Returns(Missing);
    }

    private static FFmpegStatus Missing => new()
    {
        FFmpeg = new FFmpegToolStatus { Tool = FFmpegTool.FFmpeg },
        FFprobe = new FFmpegToolStatus { Tool = FFmpegTool.FFprobe },
        HasChecked = true,
    };

    private static FFmpegStatus Ready => new()
    {
        FFmpeg = new FFmpegToolStatus { Tool = FFmpegTool.FFmpeg, Path = "ffmpeg", Version = "8.1" },
        FFprobe = new FFmpegToolStatus { Tool = FFmpegTool.FFprobe, Path = "ffprobe", Version = "8.1" },
        HasChecked = true,
    };

    [Fact]
    public async Task WithdrawWhenFFmpegIsReady_AnInstallSucceeds_TakesTheWarningDown()
    {
        _flash.Show(Notice, FlashType.Warning);
        using var subscription = HostInitialization.WithdrawWhenFFmpegIsReady(_broker, _ffmpeg, _flash, Notice);

        _ffmpeg.Status.Returns(Ready);
        await _broker.PublishAsync(new FFmpegChanged());

        Assert.Empty(_flash.Messages);
    }

    [Fact]
    public async Task WithdrawWhenFFmpegIsReady_StillMissing_LeavesTheWarningUp()
    {
        _flash.Show(Notice, FlashType.Warning);
        using var subscription = HostInitialization.WithdrawWhenFFmpegIsReady(_broker, _ffmpeg, _flash, Notice);

        await _broker.PublishAsync(new FFmpegChanged());

        Assert.Equal([Notice], _flash.Messages.Select(m => m.Text));
    }

    [Fact]
    public async Task WithdrawWhenFFmpegIsReady_OtherMessagesShowing_LeavesThemUp()
    {
        _flash.Show(Notice, FlashType.Warning);
        _flash.Show("FFmpeg installed. The next song uses it.");
        using var subscription = HostInitialization.WithdrawWhenFFmpegIsReady(_broker, _ffmpeg, _flash, Notice);

        _ffmpeg.Status.Returns(Ready);
        await _broker.PublishAsync(new FFmpegChanged());

        Assert.Equal(["FFmpeg installed. The next song uses it."], _flash.Messages.Select(m => m.Text));
    }

    // Once down, a later reinstall must not reach for a warning a new check may have raised afresh.
    [Fact]
    public async Task WithdrawWhenFFmpegIsReady_AfterWithdrawing_StopsListening()
    {
        _flash.Show(Notice, FlashType.Warning);
        using var subscription = HostInitialization.WithdrawWhenFFmpegIsReady(_broker, _ffmpeg, _flash, Notice);

        _ffmpeg.Status.Returns(Ready);
        await _broker.PublishAsync(new FFmpegChanged());

        _flash.Show(Notice, FlashType.Warning);
        await _broker.PublishAsync(new FFmpegChanged());

        Assert.Equal([Notice], _flash.Messages.Select(m => m.Text));
    }
}
