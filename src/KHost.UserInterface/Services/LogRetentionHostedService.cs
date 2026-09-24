using KHost.Telemetry;
using Microsoft.Extensions.Hosting;

namespace KHost.UserInterface.Services;

/// <summary>
/// Sweeps <c>logs/</c> once a day. Program.cs already sweeps once at launch, but that alone never
/// fires again for a host left running for weeks.
/// </summary>
internal sealed class LogRetentionHostedService(string logDirectory) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            KHostLogFiles.SweepStaleLogs(logDirectory);
    }
}
