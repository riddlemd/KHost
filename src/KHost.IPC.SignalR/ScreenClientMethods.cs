namespace KHost.IPC.SignalR;

/// <summary>The methods the host calls on a screen, by the name SignalR matches them on.</summary>
/// <remarks>The screen-to-host direction names <see cref="ScreenHub"/>'s methods with nameof; this
/// direction has no C# method to name, so both sides take these.</remarks>
internal static class ScreenClientMethods
{
    /// <summary>The nonce every message on the connection is signed against.</summary>
    public const string Session = "Session";

    /// <summary>A signed command envelope for the screen to verify and run.</summary>
    public const string ReceiveCommand = "ReceiveCommand";

    /// <summary>Why the hub refused the registration, sent just before it drops the connection.</summary>
    public const string RegistrationRefused = "RegistrationRefused";
}
