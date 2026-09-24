namespace KHost.Abstractions.Services;

/// <summary>Brings the library database up to date before anything reads it.</summary>
/// <remarks>Host-only; a plugin has no business with it. The host runs it once, synchronously, at
/// startup, before the console serves a page.</remarks>
public interface IDatabaseInitializer
{
    /// <summary>Brings the schema up to date, settles what a previous run left unfinished (downloads
    /// that never completed, ephemeral foreign keys), and seeds the defaults a fresh install needs.
    /// </summary>
    Task InitializeAsync();
}
