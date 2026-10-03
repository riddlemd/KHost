namespace KHost.UserInterface.Services;

/// <summary>Opens a window following this run's host log, standing in for the console window a
/// Windows build no longer opens.</summary>
public interface ILiveLogService
{
    /// <summary>False where the host has no window of its own to offer (anything but Windows).</summary>
    bool IsAvailable { get; }

    void Show();
}
