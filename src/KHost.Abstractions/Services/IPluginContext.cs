namespace KHost.Abstractions.Services;

/// <summary>What the host hands a plugin: settings, and a way to warn it. Fixed per process.</summary>
/// <remarks>A plugin TAKES it: as a constructor parameter of any extension class, and as the argument
/// to <see cref="IPlugin.InitializeAsync"/>. It carries the calls where the host, not the plugin,
/// supplies the identity, so one plugin can never name another's secrets or QR code. Settings are
/// those saved when the process started, over the manifest's defaults; a host's later edit applies
/// only after a restart. Callable from any thread.</remarks>
public interface IPluginContext
{
    /// <summary>One setting by key, the saved value else the manifest's default.</summary>
    /// <returns>The default of <typeparamref name="T"/> when neither exists, or when the value does
    /// not convert to <typeparamref name="T"/>.</returns>
    /// <remarks>Keys match without regard to case.</remarks>
    T? GetSetting<T>(string key);

    /// <summary>Binds settings to <typeparamref name="TSettings"/>; manifest fills any gaps.</summary>
    /// <returns>A new instance of <typeparamref name="TSettings"/>, left at its own defaults when a
    /// saved value is malformed; never null, never a throw.</returns>
    TSettings BindSettings<TSettings>() where TSettings : new();

    /// <summary>Shows a line against this plugin on the Plugins page; for setup, not failures.</summary>
    /// <remarks>Each distinct message shows once, however often it is reported, and stays until
    /// restart. Blank messages are ignored.</remarks>
    void ReportWarning(string message);

    /// <summary>Reads back a secret this plugin stored, filed under a name the host supplies.</summary>
    /// <returns>Null when nothing is stored under <paramref name="key"/>.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Keeps a value that must not sit in settings, e.g. a credential. Empty forgets it.</summary>
    /// <remarks>For a value read back later; one needed once goes through IInteractionDispatcher.
    /// Kept in the machine's own secure credential store, never in the plugin's settings. Null
    /// forgets it too. Never store a raw password: keep a hash from <see cref="IPasswordHasher"/>
    /// instead.</remarks>
    Task SetSecretAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Offers the screens a QR code, replacing this plugin's prior one; the venue picks.</summary>
    /// <param name="payload">What the code says when scanned, usually a URL. The host draws it.</param>
    /// <param name="caption">A line under it. A QR with no words is a mystery.</param>
    /// <param name="cancellationToken">Not observed: the registration is immediate.</param>
    /// <remarks>Drawn only while the venue names this plugin as its QR source; otherwise it is held,
    /// so a switch mid-show is immediate. Placement is the venue's. Not kept across a restart;
    /// register again at startup, or declare a standing code in the manifest.</remarks>
    Task RegisterQrCodeAsync(string payload, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>Withdraws this plugin's code; safe to call even if nothing was registered.</summary>
    Task UnregisterQrCodeAsync(CancellationToken cancellationToken = default);
}
