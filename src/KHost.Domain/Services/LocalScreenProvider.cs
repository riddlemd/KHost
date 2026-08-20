using System.Collections.Concurrent;
using System.Diagnostics;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public sealed class LocalScreenProvider : IScreenProvider, IDisposable
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "LocalScreen";
        public string? ExePath { get; set; }
        public string ServerUri { get; set; } = "http://localhost:5000/ipc/screen";
    }

    private const int ExitGraceMilliseconds = 2000;

    private readonly ServiceOptions _options;
    private readonly ILogger<LocalScreenProvider> _logger;
    private readonly ConcurrentDictionary<string, Process> _processes = new();

    public LocalScreenProvider(IOptions<ServiceOptions> options, ILogger<LocalScreenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Local";

    public bool IsAvailable => File.Exists(ResolvedExePath);

    public Task LaunchAsync(string screenId, CancellationToken cancellationToken = default)
    {
        var exePath = ResolvedExePath;

        // Process.Start would otherwise surface a bare Win32Exception that names nothing.
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Screen executable not found at '{exePath}'.", exePath);

        _logger.LogInformation("Launching local screen '{ScreenId}' via {ExePath}", screenId, exePath);

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (var argument in BuildArguments(_options.ServerUri, screenId))
            psi.ArgumentList.Add(argument);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exePath}");

        _processes[screenId] = process;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _processes.TryRemove(screenId, out _);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills the screen processes this provider started. Killing the process rather than asking the
    /// screen to quit over IPC is deliberate: the request has to reach only our own children, and a
    /// message on the hub would also reach screens running on other machines.
    /// </summary>
    public void CloseSpawnedScreens()
    {
        // Take each screen out of the map as it is handled, so running this on the way down and
        // again on disposal does the work once.
        foreach (var screenId in _processes.Keys)
        {
            if (!_processes.TryRemove(screenId, out var process))
                continue;

            try
            {
                if (!process.HasExited)
                {
                    _logger.LogInformation("Closing local screen '{ScreenId}'", screenId);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(ExitGraceMilliseconds);
                }
            }
            catch (Exception ex)
            {
                // A screen that will not die must not hold the host's shutdown open.
                _logger.LogWarning(ex, "Could not close local screen '{ScreenId}'", screenId);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Dispose() => CloseSpawnedScreens();

    /// <summary>Tracks an already-started process as if this provider had launched it.</summary>
    internal void Track(string screenId, Process process) => _processes[screenId] = process;

    internal bool IsTracking(string screenId) => _processes.ContainsKey(screenId);

    // The apphost is only .exe on Windows, so a hardcoded name makes IsAvailable false elsewhere.
    // Path.Combine leaves a rooted second argument alone, so an absolute configured path still wins.
    internal static string ResolveExePath(string? configuredExePath, string baseDirectory, bool isWindows)
        => Path.Combine(baseDirectory, string.IsNullOrWhiteSpace(configuredExePath)
            ? isWindows ? "KHost.Screen2.exe" : "KHost.Screen2"
            : configuredExePath);

    // Must stay one element per argument: screen ids are generated as "Screen 1", and a single
    // concatenated argument string would split that in two, leaving every screen named "Screen".
    internal static string[] BuildArguments(string serverUri, string screenId)
        => ["--server-uri", serverUri, "--screen-id", screenId];

    private string ResolvedExePath =>
        ResolveExePath(_options.ExePath, AppContext.BaseDirectory, OperatingSystem.IsWindows());
}
