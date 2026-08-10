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

    public void Dispose()
    {
        foreach (var (screenId, process) in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    _logger.LogInformation("Killing local screen '{ScreenId}'", screenId);
                    process.Kill(entireProcessTree: true);
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill local screen '{ScreenId}'", screenId);
            }
        }

        _processes.Clear();
    }

    // The .NET apphost only carries a .exe extension on Windows; on macOS/Linux it is
    // extensionless, so a hardcoded name makes IsAvailable false everywhere but Windows.
    internal static string ResolveExePath(string? configuredExePath, string baseDirectory, bool isWindows)
        => string.IsNullOrWhiteSpace(configuredExePath)
            ? Path.Combine(baseDirectory, isWindows ? "KHost.Screen.exe" : "KHost.Screen")
            : configuredExePath;

    // Must stay one element per argument: screen ids are generated as "Screen 1", and a single
    // concatenated argument string would split that in two, leaving every screen named "Screen".
    internal static string[] BuildArguments(string serverUri, string screenId)
        => ["--server-uri", serverUri, "--screen-id", screenId];

    private string ResolvedExePath =>
        ResolveExePath(_options.ExePath, AppContext.BaseDirectory, OperatingSystem.IsWindows());
}
