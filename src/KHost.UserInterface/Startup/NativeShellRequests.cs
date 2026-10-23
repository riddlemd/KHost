using Oriel;

namespace KHost.UserInterface.Startup;

/// <summary>What the console window asks its own <c>app://</c> origin for that only the host's web server can
/// answer, and the one script the window's build decides.</summary>
internal static class NativeShellRequests
{
    /// <summary>The script that tells the page it is the native window, and locks a Release build's window down.</summary>
    internal const string ShellScriptPath = "/_khost/shell.js";

    // The host's own endpoints the console page loads: media (images, streams, presets, levels, backdrops), plugin
    // icons and custom themes. Everything else is a static file or a Blazor route.
    private static readonly string[] HostPrefixes = ["/media/", "/plugins/", "/css/themes/custom/"];

    // Hop-by-hop, or the proxy's own to set.
    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "TE", "Trailer",
    };

    /// <summary>Whether <paramref name="path"/> is one of the host's endpoints rather than the console's own files.</summary>
    internal static bool IsHostEndpoint(string path)
        => HostPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The page's surface flags, and the appliance lockdown when <paramref name="lockDown"/>.</summary>
    /// <remarks>A developer keeps their tools; only a Release build's window is locked down.</remarks>
    internal static string ShellScript(bool lockDown)
    {
        var script = $"""
            document.documentElement.dataset.khSurface = 'native';
            document.documentElement.dataset.khDevtools = '{(lockDown ? "block" : "allow")}';
            """;
        return lockDown
            ? script + "\ndocument.head.appendChild(Object.assign(document.createElement('script'), { src: 'js/shell-lockdown.js' }));\n"
            : script + "\n";
    }

    /// <summary>Answers the shell script and the host's endpoints; null leaves the rest to the console's files.</summary>
    internal static Task<ProtocolResponse?> HandleAsync(
        ProtocolRequest request, HttpClient host, bool lockDown, CancellationToken cancellationToken)
        => HandleAsync(request.Url, request.Method, request.Headers, request.Body, host, lockDown, cancellationToken);

    /// <inheritdoc cref="HandleAsync(ProtocolRequest, HttpClient, bool, CancellationToken)"/>
    internal static async Task<ProtocolResponse?> HandleAsync(
        string url, string method, IReadOnlyDictionary<string, string> requestHeaders, byte[]? body,
        HttpClient host, bool lockDown, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        if (uri.AbsolutePath == ShellScriptPath)
            return ProtocolResponse.Text(ShellScript(lockDown), "text/javascript; charset=utf-8");
        if (!IsHostEndpoint(uri.AbsolutePath)) return null;

        using var message = new HttpRequestMessage(new HttpMethod(method), uri.PathAndQuery);
        if (body is { Length: > 0 }) message.Content = new ByteArrayContent(body);
        foreach (var (name, value) in requestHeaders)
        {
            if (SkippedHeaders.Contains(name)) continue;
            if (!message.Headers.TryAddWithoutValidation(name, value))
                message.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        // Not disposed: its body is the response's, read by Chromium after this returns.
        var response = await host.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && !SkippedHeaders.Contains(h.Key))
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);

        return new ProtocolResponse
        {
            StatusCode = (int)response.StatusCode,
            StatusText = response.ReasonPhrase,
            ContentType = response.Content.Headers.ContentType?.ToString(),
            Headers = headers,
            Body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            Length = response.Content.Headers.ContentLength,
        };
    }
}
