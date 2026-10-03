using System.Runtime.InteropServices;

namespace KHost.UserInterface.Startup;

/// <summary>
/// A Windows build is a GUI executable so no console window opens beside it, which also leaves it
/// deaf to the terminal it was started from. Attaching keeps <c>--reset-password</c> and
/// <c>--headless</c> printing there; the terminal no longer waits for the process, though.
/// </summary>
internal static class ParentConsole
{
    private const int AttachParentProcess = -1;

    // Must run before anything touches Console: .NET caches the standard handles on first use.
    internal static void TryAttach()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Fails harmlessly when there is no parent console or the process already has one (dotnet run).
        AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
