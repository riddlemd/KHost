using System.Text.Json;
using Photino.NET;

namespace KHost.Spike.ScreenConsumer;

/// <summary>
/// What Screen2 collapses to once the host owns ffmpeg: a window, a &lt;video&gt; tag, and the
/// playlist URL. No media server, no MediaSource plumbing, no ffmpeg, no access to the media file.
/// Compare against KHost.Screen2, which needs all four.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var playlistUrl = GetArg(args, "--playlist")
            ?? throw new ArgumentException("Pass --playlist <url to stream.m3u8>");

        PhotinoWindow? window = null;
        window = new PhotinoWindow()
            .SetTitle("KHost Screen (host-streamed)")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 720)
            .SetLeft(120)
            .SetTop(120)
            .RegisterWebMessageReceivedHandler((_, message) => Console.WriteLine($"screen -> host: {message}"))
            .Load(new Uri(BuildPage(playlistUrl)));

        window.WaitForClose();
    }

    /// <summary>
    /// Written to a file rather than a data: URI — WKWebView treats data: as an opaque origin and
    /// then refuses the cross-origin playlist fetch.
    /// </summary>
    private static string BuildPage(string playlistUrl)
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-screen-consumer-{Guid.NewGuid():n}.html");

        File.WriteAllText(path, $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>KHost Screen</title>
            <style>
              html,body{margin:0;height:100%;background:#000;overflow:hidden;user-select:none}
              video{width:100%;height:100%;object-fit:contain;background:#000}
              #status{position:absolute;left:12px;top:12px;color:#6a6a76;
                      font:600 13px system-ui,sans-serif;pointer-events:none}
            </style></head>
            <body>
              <video id="v" playsinline autoplay></video>
              <div id="status">connecting…</div>
              <script>
                const v = document.getElementById('v');
                const status = document.getElementById('status');
                const url = {{JsonSerializer.Serialize(playlistUrl)}};

                function send(payload) {
                  if (window.external && window.external.sendMessage)
                    window.external.sendMessage(JSON.stringify(payload));
                }

                // The entire client. Safari/WKWebView plays HLS from a bare src, so there is no
                // MediaSource code here at all — that is the point being demonstrated.
                if (v.canPlayType('application/vnd.apple.mpegurl') === '') {
                  status.textContent = 'no native HLS in this engine — would need hls.js';
                  send({ type: 'error', message: 'no native HLS' });
                } else {
                  v.src = url;
                }

                v.addEventListener('loadedmetadata', () => {
                  status.textContent = '';
                  send({ type: 'ready', duration: v.duration });
                });
                v.addEventListener('error', () => send({ type: 'error', message: String(v.error && v.error.code) }));

                setInterval(() => send({
                  type: 'state',
                  position: v.currentTime,
                  duration: Number.isFinite(v.duration) ? v.duration : 0,
                  playing: !v.paused && !v.ended && v.readyState > 2,
                }), 1000);
              </script>
            </body></html>
            """);

        return path;
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
