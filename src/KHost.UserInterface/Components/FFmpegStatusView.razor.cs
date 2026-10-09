using KHost.Abstractions.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>Where ffmpeg and ffprobe were found and the install's progress; App Settings and the
/// setup wizard show the same thing.</summary>
public partial class FFmpegStatusView
{
    [Parameter, EditorRequired] public FFmpegStatus Status { get; set; } = default!;

    private IEnumerable<FFmpegToolStatus> Tools => [Status.FFmpeg, Status.FFprobe];

    private string StageText => Status.Install.State switch
    {
        FFmpegInstallState.Downloading => "Downloading FFmpeg…",
        FFmpegInstallState.Verifying => "Checking the download…",
        _ => "Installing…",
    };

    private static string NameOf(FFmpegToolStatus tool) => tool.Tool == FFmpegTool.FFmpeg ? "FFmpeg" : "FFprobe";

    // The path goes on its own line under this, so here it is only the version or what went wrong.
    private string Describe(FFmpegToolStatus tool)
    {
        if (!Status.HasChecked) return "Checking…";

        if (tool.IsUsable) return tool.Version ?? "Found";

        return tool.Path is not null ? $"Found, but {tool.Error}" : "Missing";
    }

    private string StateClass(FFmpegToolStatus tool)
        => !Status.HasChecked ? "kh-ffmpeg-status__tool--checking"
            : tool.IsUsable ? "kh-ffmpeg-status__tool--found" : "kh-ffmpeg-status__tool--missing";

    private string IconFor(FFmpegToolStatus tool)
        => !Status.HasChecked ? "hourglass-split" : tool.IsUsable ? "check-circle-fill" : "x-circle-fill";
}
