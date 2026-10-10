namespace KHost.Abstractions.Services;

/// <summary>The host's break music settings that every provider honours, set once in App Settings
/// rather than per plugin.</summary>
/// <remarks>A host singleton a provider may take in its constructor. Values change while the host
/// runs, so read them each time they are needed rather than keeping a copy.</remarks>
public interface IBreakMusicSettings
{
    /// <summary>How long a fade takes, both down (a pause, or a singer taking the room) and back up
    /// (a start or resume). Zero means cut at once. Never negative.</summary>
    TimeSpan FadeDuration { get; }
}
