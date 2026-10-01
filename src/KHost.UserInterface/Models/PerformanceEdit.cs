using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Models;

/// <summary>What the Edit Performance dialog changed. A part left alone is absent, so saving writes
/// only what moved.</summary>
public sealed record PerformanceEdit
{
    /// <summary>Set when the name the turn is announced under was edited.</summary>
    public bool SungAsChanged { get; init; }

    /// <summary>The new name; null falls back to the singer's own.</summary>
    public string? SungAs { get; init; }

    /// <summary>Key, tempo and levels; null when none of them moved.</summary>
    public PerformanceSettings? Settings { get; init; }

    public bool IsEmpty => !SungAsChanged && Settings is null;

    /// <summary>Writes each changed part through the service that owns it, one announcement each.</summary>
    public async Task SaveAsync(IPerformanceService performances, Guid performanceId)
    {
        // Re-read rather than the caller's copy: a whole-row update from a stale copy would put back
        // a queue position or levels that moved since the dialog opened.
        if (SungAsChanged && await performances.ReadAsync(performanceId) is { } fresh)
        {
            fresh.SungAs = SungAs;
            await performances.UpdateAsync(fresh);
        }

        if (Settings is { } settings)
            await performances.UpdateSettingsAsync(performanceId, settings);
    }
}
