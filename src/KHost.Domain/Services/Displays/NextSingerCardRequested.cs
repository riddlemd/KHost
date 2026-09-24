using KHost.Abstractions.Services.IPC;

namespace KHost.Domain.Services.Displays;

/// <summary>A host asked for the next-singer card.</summary>
/// <remarks>Carries the card, unlike a change message: it is a one-shot request with nothing to
/// re-read afterwards, so a display that was not connected when it was asked never draws it.</remarks>
public sealed record NextSingerCardRequested(ShowNextSingerCommand Card);
