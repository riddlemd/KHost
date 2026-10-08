using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Photino.NET;

// Linked into both executables. The namespace differs so the test assembly, which sees both, can
// name each copy without an ambiguity error.
#if KHOST_LOCALSCREEN
namespace KHost.LocalScreen;
#else
namespace KHost.UserInterface;
#endif

/// <summary>Where the app icon sits beside the binary, and how a window picks it up.</summary>
internal static class AppIcon
{
    internal const string DirectoryName = "icons";
    // The console copies the screen's whole output into its own, so the two share this directory and
    // each needs file names the other does not use.
#if KHOST_LOCALSCREEN
    private const string BaseName = "khost-screen";
#else
    private const string BaseName = "khost";
#endif
    internal const string WindowsIconFileName = BaseName + ".ico";
    internal const string LinuxIconFileName = BaseName + "-256.png";
    internal const string MacIconFileName = BaseName + ".icns";

    internal static OSPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OSPlatform.Linux;

    /// <summary>The window icon for <paramref name="platform"/>, or null when there is none to set.</summary>
    /// <remarks>Null on macOS: Photino puts it only on the title bar's document button there, and the
    /// Dock is <see cref="MacDockIcon"/>'s job.</remarks>
    internal static string? ResolveWindowIcon(string baseDirectory, OSPlatform platform)
    {
        string? name = platform == OSPlatform.Windows ? WindowsIconFileName
            : platform == OSPlatform.OSX ? null
            : LinuxIconFileName;

        if (name is null) return null;

        var path = Path.Combine(baseDirectory, DirectoryName, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Sets the window icon when one is there to set.</summary>
    /// <remarks>Photino throws on a path that does not exist, so a lost icon file would otherwise
    /// stop the window opening at all.</remarks>
    internal static PhotinoWindow SetAppIcon(this PhotinoWindow window, ILogger logger)
    {
        var platform = CurrentPlatform;
        if (platform == OSPlatform.OSX) return window;

        var path = ResolveWindowIcon(AppContext.BaseDirectory, platform);
        if (path is null)
        {
            logger.LogWarning("No window icon under {Directory}; the window opens without one",
                Path.Combine(AppContext.BaseDirectory, DirectoryName));
            return window;
        }

        try
        {
            return window.SetIconFile(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not set the window icon from {Path}", path);
            return window;
        }
    }
}

/// <summary>Puts the KHost icon in the macOS Dock.</summary>
/// <remarks>A process run through the <c>dotnet</c> host or as a bare apphost has no bundle to take
/// an icon from, so the Dock shows a generic one unless NSApp is told at runtime.</remarks>
internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>Sets the Dock icon from the .icns beside the binary; false when nothing was set.</summary>
    /// <remarks>Main thread only, after Photino has created NSApp: its window-created handler is both.</remarks>
    internal static bool TrySet(ILogger logger)
        => TrySet(
            Path.Combine(AppContext.BaseDirectory, AppIcon.DirectoryName, AppIcon.MacIconFileName),
            OperatingSystem.IsMacOS(),
            logger,
            Apply);

    internal static bool TrySet(string iconPath, bool isMacOS, ILogger logger, Func<string, bool> apply)
    {
        if (!isMacOS) return false;

        try
        {
            if (!File.Exists(iconPath))
            {
                logger.LogWarning("No Dock icon at {Path}; the Dock shows the generic one", iconPath);
                return false;
            }

            var applied = apply(iconPath);
            if (!applied) logger.LogWarning("AppKit declined the Dock icon at {Path}", iconPath);
            return applied;
        }
        catch (Exception ex)
        {
            // An Objective-C exception is not a .NET one and would not land here, which is why every
            // AppKit handle is checked for nil before the next message is sent.
            logger.LogWarning(ex, "Could not set the Dock icon from {Path}", iconPath);
            return false;
        }
    }

    private static bool Apply(string iconPath)
    {
        var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var imageClass = objc_getClass("NSImage");
        var stringClass = objc_getClass("NSString");
        if (app == 0 || imageClass == 0 || stringClass == 0) return false;

        var utf8 = Marshal.StringToCoTaskMemUTF8(iconPath);
        try
        {
            var path = Send(stringClass, sel_registerName("stringWithUTF8String:"), utf8);
            if (path == 0) return false;

            var image = Send(Send(imageClass, sel_registerName("alloc")), sel_registerName("initWithContentsOfFile:"), path);
            if (image == 0) return false;

            Send(app, sel_registerName("setApplicationIconImage:"), image);
            // NSApp keeps its own reference.
            Send(image, sel_registerName("release"));
            return true;
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [DllImport(ObjC)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);
}
