namespace KHost.Domain.Services;

/// <summary>The host's live listening address, which is not known until Kestrel is up — after the
/// options that need it have already been bound.</summary>
/// <remarks>Post-configure reapplies these on every bind, so a settings change that rebinds the
/// options cannot drop the resolved address back to the compile-time default. A value stays null
/// when configuration named one, because an explicit setting always wins.</remarks>
public sealed class ResolvedHostAddress
{
    public string? ScreenIpcUri { get; set; }

    public string? MediaStreamBaseAddress { get; set; }
}
