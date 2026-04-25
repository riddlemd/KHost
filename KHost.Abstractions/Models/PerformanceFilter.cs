namespace KHost.Abstractions.Models;

[Flags]
public enum PerformanceFilter
{
    Queued = 1,
    UnQueued = 2,

    All = Queued | UnQueued
}
