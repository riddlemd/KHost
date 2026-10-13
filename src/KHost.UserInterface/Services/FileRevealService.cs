using System.Diagnostics;

namespace KHost.UserInterface.Services;

public sealed class FileRevealService(ILogger<FileRevealService> logger) : IFileRevealService
{
    private static readonly FileManager Current =
        OperatingSystem.IsMacOS() ? FileManager.Finder
        : OperatingSystem.IsWindows() ? FileManager.Explorer
        : FileManager.FreeDesktop;

    public string Label => LabelFor(Current);

    // A file implies its folder, so the folder alone answers both cases.
    public bool CanReveal(string? path) => Directory.Exists(Path.GetDirectoryName(path));

    public void Reveal(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var startInfo = BuildStartInfo(Current, path, File.Exists(path), folder is not null && Directory.Exists(folder));

        if (startInfo is null)
        {
            logger.LogWarning("Nothing to show for {Path}: neither the file nor its folder exists", path);
            return;
        }

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open the file manager at {Path}", path);
        }
    }

    internal static string LabelFor(FileManager manager) => manager switch
    {
        FileManager.Finder => "Show in Finder",
        FileManager.Explorer => "Show in File Explorer",
        _ => "Open containing folder",
    };

    /// <returns>Null when there is nothing on disk to show.</returns>
    internal static ProcessStartInfo? BuildStartInfo(FileManager manager, string path, bool fileExists, bool folderExists)
    {
        var folder = Path.GetDirectoryName(path);

        if (!fileExists && (!folderExists || folder is null)) return null;

        switch (manager)
        {
            case FileManager.Finder:
                var finder = new ProcessStartInfo("open") { UseShellExecute = false };
                if (fileExists) finder.ArgumentList.Add("-R");
                finder.ArgumentList.Add(fileExists ? path : folder!);
                return finder;

            // Explorer parses its own command line, and wants /select and the quoted path as one argument.
            case FileManager.Explorer:
                return new ProcessStartInfo("explorer.exe", fileExists ? $"/select,\"{path}\"" : $"\"{folder}\"")
                {
                    UseShellExecute = false,
                };

            // No common way to select a file across Linux file managers; the folder is what all of them open.
            default:
                var desktop = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                desktop.ArgumentList.Add(folder!);
                return desktop;
        }
    }

    public enum FileManager { Finder, Explorer, FreeDesktop }
}
