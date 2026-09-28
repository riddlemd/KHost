namespace KHost.Abstractions.Services;

/// <summary>Folders the host keeps beside its own data that a plugin may use too.</summary>
/// <remarks>A host singleton; the paths are fixed for the life of the process.</remarks>
public interface IHostDirectories
{
    /// <summary>A shared folder of executables, looked in before the system PATH.</summary>
    /// <remarks>Exists once the host is up. The host installs ffmpeg and ffprobe here, so a plugin
    /// must never write, replace or delete files by those names; a plugin that needs a program of
    /// its own puts it here under a name distinctly its own. Kept across restarts and updates.
    /// </remarks>
    string BinDirectory { get; }
}
