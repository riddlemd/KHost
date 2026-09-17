namespace KHost.Abstractions.Services;

/// <summary>What the host hands a plugin: settings, and a way to warn it. Fixed per process.</summary>
public interface IPluginContext
{
    T? GetSetting<T>(string key);

    /// <summary>Binds settings to <typeparamref name="TSettings"/>; manifest fills any gaps.</summary>
    TSettings BindSettings<TSettings>() where TSettings : new();

    /// <summary>Shows a line against this plugin on the Plugins page; for setup, not failures.</summary>
    void ReportWarning(string message);

    /// <summary>Reads back a secret this plugin stored, filed under a name the host supplies.</summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Keeps a value that must not sit in settings, e.g. a credential. Empty forgets it.</summary>
    /// <remarks>For a value read back later; one needed once goes through IInteractionDispatcher.</remarks>
    Task SetSecretAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Offers the screens a QR code, replacing this plugin's prior one; the venue picks.</summary>
    /// <param name="payload">What the code says when scanned, usually a URL. The host draws it.</param>
    /// <param name="caption">A line under it. A QR with no words is a mystery.</param>
    Task RegisterQrCodeAsync(string payload, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>Withdraws this plugin's code; safe to call even if nothing was registered.</summary>
    Task UnregisterQrCodeAsync(CancellationToken cancellationToken = default);
}
