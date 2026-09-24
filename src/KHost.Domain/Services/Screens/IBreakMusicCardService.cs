using KHost.Abstractions.Services.IPC;

namespace KHost.Domain.Services.Screens;

/// <summary>What the break music card should say right now; the display decides when to draw it.</summary>
public interface IBreakMusicCardService
{
    /// <summary>The whole card, disabled when there is nothing playing worth naming.</summary>
    Task<SetBreakMusicCardCommand> BuildAsync(CancellationToken cancellationToken = default);
}
