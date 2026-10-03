using System.Net.Sockets;

namespace KHost.Domain.Services;

/// <summary>Turns a failed call to something on the network into one line a host can act on.</summary>
public static class NetworkFailureText
{
    /// <summary>"Could not reach {thing}…" when the network is to blame, else "{thing} failed: …".</summary>
    /// <param name="thing">What was being reached, as the host knows it: "KaraFun", "the plugin catalog".</param>
    /// <param name="callerToken">The caller's own token, so its cancellation is not taken for a timeout.</param>
    public static string Describe(string thing, Exception exception, CancellationToken callerToken = default)
        => IsNetworkFailure(exception, callerToken)
            ? $"Could not reach {thing}. Check this computer is online, then try again."
            : $"{Capitalise(thing)} failed: {exception.Message}";

    public static bool IsNetworkFailure(Exception exception, CancellationToken callerToken = default) => exception switch
    {
        // A status code means the server answered; "could not reach" would send the host to check the wifi.
        HttpRequestException { StatusCode: not null } => false,
        HttpRequestException or SocketException => true,
        IOException { InnerException: HttpRequestException or SocketException } => true,
        // HttpClient reports its own Timeout as a cancellation.
        TaskCanceledException => !callerToken.IsCancellationRequested,
        _ => false,
    };

    private static string Capitalise(string text)
        => text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
}
