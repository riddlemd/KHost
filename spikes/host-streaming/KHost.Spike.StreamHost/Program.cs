using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using KHost.Spike.StreamHost;

// Spike: what the main KHost app would look like if it owned ffmpeg and every screen were just
// an HTTP consumer. Not wired into KHost.slnx; nothing here is meant to ship as-is.

var builder = WebApplication.CreateSlimBuilder(args);

// 0.0.0.0, not loopback: a Chromecast fetches the playlist over the LAN, so the address the
// device is handed has to be one it can route to. This is the single biggest deployment change
// versus today's per-screen 127.0.0.1 media server.
builder.WebHost.UseUrls("http://0.0.0.0:5480");

var app = builder.Build();

var sessionRoot = Path.Combine(Path.GetTempPath(), "khost-spike-hls");
Directory.CreateDirectory(sessionRoot);

var sessions = new Dictionary<string, TranscodeSession>();
var gate = new object();

string lanAddress = ResolveLanAddress();
string baseUrl = $"http://{lanAddress}:5480";

// UseDefaultFiles has to precede UseStaticFiles or "/" never rewrites to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/info", () => Results.Json(new
{
    baseUrl,
    lanAddress,
    sessionRoot,
    ffmpeg = ProbeFfmpeg(),
}));

// Start a transcode. In the real app this is what PlaybackService.LoadAsync would call instead of
// broadcasting a file path that every screen has to be able to open for itself.
app.MapPost("/api/session", (StartRequest request) =>
{
    if (!File.Exists(request.FilePath))
        return Results.BadRequest(new { error = $"No such file: {request.FilePath}" });

    var session = new TranscodeSession(
        request.FilePath,
        TimeSpan.FromSeconds(Math.Max(0, request.Offset)),
        request.Pitch,
        sessionRoot);

    lock (gate) sessions[session.Id] = session;

    return Results.Json(new
    {
        session.Id,
        // The URL handed to every consumer alike — screen, browser, or Chromecast.
        playlistUrl = $"{baseUrl}/media/{session.Id}/{session.PlaylistName}",
    });
});

app.MapGet("/api/session", () =>
{
    lock (gate)
        return Results.Json(sessions.Values.Select(s => new
        {
            s.Id,
            s.SourcePath,
            offset = s.Offset.TotalSeconds,
            s.PitchSemitones,
            s.IsComplete,
            s.FirstSegmentSeconds,
            segments = s.SegmentCount(),
            playlistUrl = $"{baseUrl}/media/{s.Id}/{s.PlaylistName}",
        }).ToList());
});

app.MapDelete("/api/session/{id}", (string id) =>
{
    TranscodeSession? session;
    lock (gate)
    {
        if (!sessions.Remove(id, out session)) return Results.NotFound();
    }

    session!.Dispose();
    return Results.Ok();
});

// Playlist and segments. Range support matters: Cast receivers issue ranged GETs for segments.
app.MapGet("/media/{id}/{file}", (string id, string file, HttpContext context) =>
{
    TranscodeSession? session;
    lock (gate)
    {
        if (!sessions.TryGetValue(id, out session)) return Results.NotFound();
    }

    // Reject anything that is not a bare file name before it reaches the filesystem.
    if (file.Contains('/') || file.Contains('\\') || file.Contains("..", StringComparison.Ordinal))
        return Results.BadRequest();

    var path = Path.Combine(session!.Directory, file);
    if (!File.Exists(path)) return Results.NotFound();

    var contentType = Path.GetExtension(path) switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".ts" => "video/mp2t",
        ".m4s" or ".mp4" => "video/mp4",
        _ => "application/octet-stream",
    };

    // A Chromecast will not play a cross-origin stream without this.
    context.Response.Headers.AccessControlAllowOrigin = "*";

    if (contentType.Contains("mpegurl"))
    {
        // Playlists must never be cached: an EVENT playlist grows while the song transcodes.
        context.Response.Headers.CacheControl = "no-cache, no-store";

        // Transcoding runs ~30x realtime, so by the time a player attaches the playlist already
        // holds a minute of segments and it joins at the live edge — the song starts part-way in.
        // EXT-X-START pins every player to the top of the song instead.
        var playlist = File.ReadAllText(path);
        if (!playlist.Contains("#EXT-X-START", StringComparison.Ordinal))
            playlist = playlist.Replace("#EXTM3U", "#EXTM3U\n#EXT-X-START:TIME-OFFSET=0,PRECISE=YES",
                StringComparison.Ordinal);

        return Results.Text(playlist, contentType);
    }

    return Results.File(path, contentType, enableRangeProcessing: true);
});

// macOS-only convenience so the spike can answer "is there anything to cast to?" without a
// dependency. Real discovery would use an mDNS library in-process.
app.MapGet("/api/cast/discover", async () =>
{
    var found = await DiscoverCastDevicesAsync(TimeSpan.FromSeconds(5));
    return Results.Json(new { count = found.Count, devices = found });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    lock (gate)
    {
        foreach (var session in sessions.Values) session.Dispose();
        sessions.Clear();
    }
});

Console.WriteLine($"Spike stream host listening on {baseUrl}");
Console.WriteLine($"HLS sessions under {sessionRoot}");
app.Run();

static string ResolveLanAddress()
{
    // The address a Chromecast can reach, which is never the loopback one the screens use today.
    var candidate = NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                             && !IPAddress.IsLoopback(a));

    return candidate?.ToString() ?? "127.0.0.1";
}

static object ProbeFfmpeg()
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        var first = process.StandardOutput.ReadLine();
        process.WaitForExit();
        return new { available = true, version = first };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message };
    }
}

static async Task<List<string>> DiscoverCastDevicesAsync(TimeSpan timeout)
{
    var devices = new List<string>();

    if (!OperatingSystem.IsMacOS()) return devices;

    try
    {
        using var process = Process.Start(new ProcessStartInfo("dns-sd", "-B _googlecast._tcp")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cts.Token) is { } line)
                if (line.Contains("_googlecast._tcp", StringComparison.Ordinal) && line.Contains("Add", StringComparison.Ordinal))
                    devices.Add(line.Trim());
        }
        catch (OperationCanceledException) { /* browsing never ends on its own */ }

        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
    catch (Exception ex)
    {
        devices.Add($"discovery failed: {ex.Message}");
    }

    return devices;
}

internal sealed record StartRequest(string FilePath, double Offset = 0, int Pitch = 0);
