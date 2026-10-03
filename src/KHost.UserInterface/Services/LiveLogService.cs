using System.Diagnostics;

namespace KHost.UserInterface.Services;

public sealed class LiveLogService : ILiveLogService
{
    private readonly string _logFilePath;

    public LiveLogService(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    // A separate PowerShell tailing the file, not AllocConsole: closing a console the host owns
    // terminates the host, and Windows Terminal ignores ShowWindow on it.
    public void Show()
    {
        if (!IsAvailable) return;

        Process.Start(new ProcessStartInfo("powershell.exe", BuildArguments(_logFilePath))
        {
            // Without the shell, a debug run started from a terminal tails into that terminal.
            UseShellExecute = true,
        });
    }

    internal static string BuildArguments(string logFilePath)
    {
        var quotedPath = "'" + logFilePath.Replace("'", "''") + "'";

        // Serilog writes UTF-8 without a BOM, which Windows PowerShell reads as ANSI unless told.
        return "-NoLogo -NoProfile -NoExit -Command \""
            + "$Host.UI.RawUI.WindowTitle = 'KHost log'; "
            + "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
            + $"Get-Content -LiteralPath {quotedPath} -Encoding UTF8 -Tail 200 -Wait\"";
    }
}
