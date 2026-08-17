using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.IPC.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Drives real Screen2 processes over the real IPC hub with the real HlsMediaStreamService, so the
// sync group can be measured without a seeded media library. Screens report their position once a
// second; comparing two reports taken at the same instant is the skew.
//
//   dotnet run -- --media /path/to/song.mp4

var mediaPath = GetArg("--media") ?? throw new ArgumentException("Pass --media <file>");
if (!File.Exists(mediaPath)) throw new FileNotFoundException(mediaPath);

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5490");
builder.Services.AddSignalRIPCServer();
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();

var streams = new HlsMediaStreamService(
    NullLogger<HlsMediaStreamService>.Instance,
    Options.Create(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://127.0.0.1:5490" }));

app.MapIPCServer();

app.MapGet("/media/{sessionId}/{fileName}", (string sessionId, string fileName, HttpContext context) =>
{
    var path = streams.ResolveArtifact(sessionId, fileName);
    if (path is null) return Results.NotFound();

    var contentType = Path.GetExtension(path) == ".m3u8" ? "application/vnd.apple.mpegurl" : "video/mp2t";
    context.Response.Headers.AccessControlAllowOrigin = "*";

    if (contentType.Contains("mpegurl"))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        var playlist = File.ReadAllText(path);
        if (!playlist.Contains("#EXT-X-START", StringComparison.Ordinal))
            playlist = playlist.Replace("#EXTM3U", "#EXTM3U\n#EXT-X-START:TIME-OFFSET=0,PRECISE=YES",
                StringComparison.Ordinal);
        return Results.Text(playlist, contentType);
    }

    return Results.File(path, contentType, enableRangeProcessing: true);
});

var server = app.Services.GetRequiredService<IScreenServer>();

// screenId -> (wall clock when the report landed, reported song position)
var reports = new Dictionary<string, (DateTime At, TimeSpan Position)>();
var reportLock = new object();

string? primaryId = null;

server.StateReceived += (_, e) =>
{
    if (e.State is not ScreenPlaybackState state) return;

    // Timestamped by the screen itself, so the skew figure is not polluted by delivery latency.
    lock (reportLock) reports[e.ScreenId] = (state.SampledAtUtc ?? DateTime.UtcNow, state.Position);

    // Mirrors PlaybackService: re-anchor the group onto what the primary actually plays, so the
    // followers converge on a reachable position and settle back to true speed.
    if (e.ScreenId != primaryId || !state.IsPlaying || state.SampledAtUtc is not { } sampledAt) return;

    foreach (var id in SyncCapableIdsAsync().GetAwaiter().GetResult())
        server.SendCommandAsync(id, new SetTimelineCommand
        {
            Position = state.Position,
            AnchorUtc = sampledAt,
            IsPlaying = true,
            IsPrimary = id == primaryId,
        }).GetAwaiter().GetResult();
};

server.ScreenConnected += (_, e) => Console.WriteLine(
    $"[harness] {e.Connection.ScreenId} connected (supportsSync={e.Connection.Capabilities.SupportsSync})");

await app.StartAsync();
Console.WriteLine("[harness] listening on http://127.0.0.1:5490/ipc/screen");

// Wait for both screens; they are started staggered by the runner so the skew is real.
while (await CountScreensAsync() < 2) await Task.Delay(250);
Console.WriteLine("[harness] two screens present, opening stream");

var session = await streams.OpenAsync(mediaPath);
await server.BroadcastCommandAsync(new LoadMediaCommand
{
    FilePath = mediaPath,
    StreamUrl = session.PlaylistUrl,
    StreamStartOffset = session.StartOffset,
});

await Task.Delay(TimeSpan.FromSeconds(3));
await server.BroadcastCommandAsync(new PlayCommand());

// The anchor is what makes both screens start on one instant rather than on arrival. From here
// the primary's own reports re-anchor the group, so the followers chase a reachable position.
var initialScreens = await SyncCapableIdsAsync();
primaryId = initialScreens.FirstOrDefault();

var anchor = DateTime.UtcNow.AddMilliseconds(3000);
foreach (var id in initialScreens)
    await server.SendCommandAsync(id, new SetTimelineCommand
    {
        Position = TimeSpan.Zero,
        AnchorUtc = anchor,
        IsPlaying = true,
        IsPrimary = id == primaryId,
    });

Console.WriteLine($"[harness] primary is {primaryId}");

Console.WriteLine("[harness] timeline published; sampling skew");

for (var i = 0; i < 100; i++)
{
    await Task.Delay(1000);

    lock (reportLock)
    {
        if (reports.Count < 2) continue;

        var ordered = reports.OrderBy(r => r.Key).ToArray();
        var a = ordered[0];
        var b = ordered[1];

        // Reports land at slightly different moments; advance each to a common instant before
        // comparing, or the sampling jitter would be read as skew.
        var now = DateTime.UtcNow;
        var pa = a.Value.Position + (now - a.Value.At);
        var pb = b.Value.Position + (now - b.Value.At);

        Console.WriteLine($"[skew] {a.Key}={pa.TotalSeconds,7:F3}  {b.Key}={pb.TotalSeconds,7:F3}  "
                          + $"skew={Math.Abs((pa - pb).TotalSeconds):F3}s");
    }
}

await streams.CloseAsync(session.Id);
await app.StopAsync();

async Task<int> CountScreensAsync()
{
    var count = 0;
    await foreach (var _ in server.GetConnectedScreensAsync()) count++;
    return count;
}

async Task<List<string>> SyncCapableIdsAsync()
{
    var ids = new List<string>();
    await foreach (var s in server.GetConnectedScreensAsync())
        if (s.Capabilities.SupportsSync) ids.Add(s.ScreenId);
    return ids;
}

string? GetArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
