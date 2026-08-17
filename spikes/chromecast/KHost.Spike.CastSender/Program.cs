using System.Reflection;
using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

// Answers one question: can Sharpcaster drive the emulator end to end, given its TLS certificate
// is self-signed and cannot chain to Google's Eureka CA the way a real Chromecast's does?
//
//   dotnet run -- --api                       dump the library surface
//   dotnet run -- [--host 127.0.0.1] [--url <media url>]

if (args.Contains("--api")) return DumpApi();

var host = Arg("--host") ?? "127.0.0.1";
var mediaUrl = Arg("--url") ?? "http://example.com/video.mp4";

Console.WriteLine("== discovery ==");
var locator = new ChromecastLocator();
var found = (await locator.FindReceiversAsync(TimeSpan.FromSeconds(5))).ToList();

foreach (var device in found)
    Console.WriteLine($"   {device.Name}  {device.DeviceUri}  model={device.Model}");

// Fall back to the literal host: mDNS can be blocked, and the emulator supports --no-advertise.
var receiver = found.FirstOrDefault(r => r.DeviceUri?.Host == host)
    ?? found.FirstOrDefault()
    ?? new ChromecastReceiver { Name = "manual", DeviceUri = new Uri($"https://{host}:8009") };

Console.WriteLine($"\n== connect to {receiver.Name} @ {receiver.DeviceUri} ==");

var client = new ChromecastClient();
var status = await client.ConnectChromecast(receiver);
Console.WriteLine($"   connected; volume={status?.Volume?.Level}");

Console.WriteLine("\n== launch default media receiver ==");
await client.LaunchApplicationAsync("CC1AD845", false);
Console.WriteLine("   launched");

client.MediaChannel.StatusChanged += (_, s) =>
    Console.WriteLine($"   [status] state={s?.PlayerState} t={s?.CurrentTime:F2}");

Console.WriteLine("\n== load ==");
var loaded = await client.MediaChannel.LoadAsync(new Media { ContentUrl = mediaUrl, Duration = 30 }, true);
Console.WriteLine($"   state={loaded?.PlayerState} sessionId={loaded?.MediaSessionId}");

await Task.Delay(1500);

Console.WriteLine("\n== transport ==");
Console.WriteLine($"   pause -> {(await client.MediaChannel.PauseAsync())?.PlayerState}");
Console.WriteLine($"   seek  -> {(await client.MediaChannel.SeekAsync(10))?.CurrentTime:F2}");
Console.WriteLine($"   play  -> {(await client.MediaChannel.PlayAsync())?.PlayerState}");

Console.WriteLine("\n== volume (the auto-mute path) ==");

// Stream volume would be the polite choice — it leaves the TV's own level alone — but
// Sharpcaster requires a media session id it does not always have.
try
{
    var muted = await client.MediaChannel.SetVolumeAsync(0);
    Console.WriteLine($"   media-stream volume 0 -> level={muted?.Volume?.Level}");
}
catch (Exception ex)
{
    Console.WriteLine($"   media-stream volume UNAVAILABLE: {ex.Message}");
}

var receiverMuted = await client.ReceiverChannel.SetMute(true);
Console.WriteLine($"   receiver mute -> muted={receiverMuted?.Volume?.Muted} level={receiverMuted?.Volume?.Level}");

var receiverVolume = await client.ReceiverChannel.SetVolume(0.25);
Console.WriteLine($"   receiver volume 0.25 -> level={receiverVolume?.Volume?.Level}");

await client.ReceiverChannel.SetMute(false);

await Task.Delay(1000);
await client.DisconnectAsync();

Console.WriteLine("\nOK — Sharpcaster drives the emulator");
return 0;

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int DumpApi()
{
    var assembly = typeof(ChromecastClient).Assembly;

    foreach (var type in assembly.GetExportedTypes()
                 .Where(t => t.Name is "ChromecastClient" or "ChromecastReceiver" or "Media"
                     || t.Name.EndsWith("Channel") || t.Name.EndsWith("Locator") || t.Name == "MediaStatus")
                 .OrderBy(t => t.FullName))
    {
        Console.WriteLine($"\n== {type.FullName}");

        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance
                     | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (member is MethodInfo m && (m.IsSpecialName || m.DeclaringType == typeof(object))) continue;
            Console.WriteLine("   " + member);
        }
    }

    return 0;
}
