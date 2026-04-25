using Avalonia;
using FFMpegCore;
using KHost.Screen;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureFFMpeg();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
