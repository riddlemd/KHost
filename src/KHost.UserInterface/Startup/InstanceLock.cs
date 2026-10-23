using Microsoft.Extensions.Logging.Abstractions;
using Oriel;
using KHost.Common.Storage;

namespace KHost.UserInterface.Startup;

/// <summary>Keeps a second launch from running alongside a live host.</summary>
internal static class InstanceLock
{
    private const string InstanceLockFileName = ".instance.lock";

    internal const int AlreadyRunningExitCode = 1;

    /// <summary>Holds an exclusive handle on the lock file, or null when another instance has it.</summary>
    /// <remarks>Scoped to the data folder: two hosts on one library would corrupt it.</remarks>
    internal static FileStream? TryAcquire()
    {
        try
        {
            // FileShare.None, and the OS drops the handle even on a kill, so the lock cannot go stale.
            return new FileStream(
                Path.Combine(AppPaths.DataRoot, InstanceLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Tells the user why this launch is stopping.</summary>
    /// <remarks>A shell launch has no console to read, so it gets a native dialog instead.</remarks>
    internal static void ReportAlreadyRunning(bool headless)
    {
        const string Message = "Only one instance of KHost can run at a time.";

        if (headless)
        {
            Console.Error.WriteLine(Message);
            return;
        }

        // A window-less app: the dialog is all this process shows before it exits.
        var app = OrielApp.Create("KHost");
        // Not the running window's profile, which that window's Chromium holds locked.
        app.SetPath("userData", Path.Combine(Path.GetTempPath(), "khost-instance-dialog"));
        app.Ready += (_, _) =>
        {
            MacDockIcon.TrySet(NullLogger.Instance);
            Dialog.ShowMessageBoxSync(new MessageBoxOptions
            {
                Type = MessageBoxType.Warning,
                Title = "KHost",
                Message = Message,
                Buttons = ["OK"],
            });
            app.Quit();
        };
        app.Run();
    }
}
