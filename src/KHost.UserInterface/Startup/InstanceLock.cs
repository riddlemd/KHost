using Microsoft.Extensions.Logging.Abstractions;
using Photino.NET;

namespace KHost.UserInterface.Startup;

/// <summary>Keeps a second launch from running alongside a live host.</summary>
internal static class InstanceLock
{
    private const string InstanceLockFileName = ".instance.lock";

    internal const int AlreadyRunningExitCode = 1;

    /// <summary>Holds an exclusive handle on the lock file, or null when another instance has it.</summary>
    /// <remarks>Scoped to the install directory, so separate installs may coexist.</remarks>
    internal static FileStream? TryAcquire()
    {
        try
        {
            // FileShare.None, and the OS drops the handle even on a kill, so the lock cannot go stale.
            return new FileStream(
                Path.Combine(AppContext.BaseDirectory, InstanceLockFileName),
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

        // ShowMessage crashes on a window the native layer has not built yet, so the dialog has to
        // be raised from inside the created handler, which is why a throwaway window hosts it.
        PhotinoWindow? window = null;
        window = new PhotinoWindow()
            .SetTitle("KHost")
            .SetAppIcon(NullLogger.Instance)
            .SetUseOsDefaultSize(false)
            .SetSize(1, 1)
            .RegisterWindowCreatedHandler((_, _) =>
            {
                MacDockIcon.TrySet(NullLogger.Instance);
                window!.ShowMessage("KHost", Message, PhotinoDialogButtons.Ok, PhotinoDialogIcon.Warning);

                // Close() here does not break out of WaitForClose, which would leave the process
                // pumping an invisible window forever. Showing the dialog is all this process does.
                Environment.Exit(AlreadyRunningExitCode);
            })
            .LoadRawString("<html><body></body></html>");

        window.WaitForClose();
    }
}
