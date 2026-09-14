namespace KHost.Abstractions.Services;

/// <summary>
/// What the host hands a plugin: its settings, and a way to tell the host something the person
/// running it should know. Everything else a plugin needs it takes in its constructor — the host's
/// container builds it, so any registered service is available the same way it is to the host. Setting values come from what the host collected for the manifest's
/// settings schema and are fixed for the lifetime of the process (restart applies changes).
/// </summary>
public interface IPluginContext
{

    T? GetSetting<T>(string key);

    /// <summary>
    /// Deserializes every stored setting into <typeparamref name="TSettings"/>. Manifest
    /// defaults fill missing keys; the class's property initializers cover the rest.
    /// </summary>
    TSettings BindSettings<TSettings>() where TSettings : new();

    /// <summary>
    /// Shows a line against this plugin on the Plugins page. For setup a host can act on — a
    /// slower path taken, a tool that would work better installed — not for failures, which
    /// belong in the exception that caused them.
    /// </summary>
    void ReportWarning(string message);

    /// <summary>
    /// Reads back a secret this plugin stored, or null if it never did. Keys are this plugin's own: two plugins using "session" do not collide, and
    /// neither can read the other's, because the name they are filed under comes from the host and
    /// not from the caller.
    /// </summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps a value that must not sit in plugin settings — a credential the plugin will trade for
    /// a session, the token behind a paid account. A null or empty value forgets it.
    /// </summary>
    /// <remarks>
    /// This is for what a plugin must be able to read back. Anything it merely needs *once* should
    /// be asked for through <c>IInteractionDispatcher</c> and never stored at all, and anything a
    /// host should see and edit belongs in the manifest's settings, which the Plugins page shows.
    /// Storing a secret is a decision to keep a credential on a venue's machine: make it because
    /// the alternative is worse, not because it is convenient.
    /// </remarks>
    Task SetSecretAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers the screens a QR code, replacing whatever this plugin offered before. The manifest's
    /// <c>qrCode</c> is the standing registration — "this plugin is a source a venue may pick" —
    /// and this is the live one: what that source is pointing at right now.
    /// </summary>
    /// <remarks>
    /// Registering is not showing. The venue names one source, and a code from any other is held
    /// and not drawn; a venue that has named none shows nothing at all. So call this whenever the
    /// payload changes and let the host decide — a plugin that checks first would be guessing at a
    /// setting that can change under it.
    /// <para>
    /// The owner is filled in from the manifest the host loaded, never passed by the caller, for
    /// the same reason a secret's key is: two plugins cannot collide, and neither can register
    /// over the other's code.
    /// </para>
    /// </remarks>
    /// <param name="payload">What the code says when scanned, usually a URL. The host draws it.</param>
    /// <param name="caption">A line under it. A QR with no words is a mystery.</param>
    Task RegisterQrCodeAsync(string payload, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws this plugin's code. Safe when nothing was registered, so it is the right thing to
    /// call on the way out whether or not a code ever went up.
    /// </summary>
    Task UnregisterQrCodeAsync(CancellationToken cancellationToken = default);
}
