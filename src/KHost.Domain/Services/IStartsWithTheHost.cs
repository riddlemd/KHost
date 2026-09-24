namespace KHost.Domain.Services;

/// <summary>Marks a singleton the host must build at startup, not lazily on first use.</summary>
/// <remarks>Built lazily, it has wired nothing when the first screen connects, silently unanswered.</remarks>
public interface IStartsWithTheHost;
