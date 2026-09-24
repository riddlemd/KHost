namespace KHost.Abstractions.Models;

/// <summary>Which performances a query includes, by whether they are still on the queue.</summary>
[Flags]
public enum PerformanceFilter
{
    /// <summary>Performances still waiting to happen.</summary>
    Queued = 1,

    /// <summary>Performances already sung, or otherwise off the queue.</summary>
    UnQueued = 2,

    /// <summary>Both queued and unqueued performances.</summary>
    All = Queued | UnQueued
}
