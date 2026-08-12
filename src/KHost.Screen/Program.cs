using Avalonia;
using FFMpegCore;
using KHost.Screen;
using Microsoft.Extensions.Logging;
using Serilog;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureFFMpeg();

        var logPath = $"logs/{DateTime.Now:yyyyMMddHHmmss}.Screen.log";
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        App.LoggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Information)
            .AddSerilog(serilog, dispose: true));

        App.IpcServerUri = GetArg(args, "--server-uri") ?? "http://localhost:5000/ipc/screen";
        App.IpcScreenId = GetArg(args, "--screen-id") ?? Environment.MachineName;
        App.ShowControls = !HasFlag(args, "--no-controls");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static bool HasFlag(string[] args, string name)
        => Array.IndexOf(args, name) >= 0;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Honor the <c>FFMPEG_PATH</c> environment variable by pointing FFMpegCore at that
    /// directory. When unset, FFMpegCore resolves ffmpeg/ffprobe from the system PATH.
    /// </summary>
    private static void ConfigureFFMpeg()
    {
        string? raw = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (string.IsNullOrWhiteSpace(raw)) return;

        string? folder = File.Exists(raw) ? Path.GetDirectoryName(raw) : raw;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        GlobalFFOptions.Configure(opts => opts.BinaryFolder = folder);
    }
}
