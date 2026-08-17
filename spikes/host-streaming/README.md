# host-streaming spike

Throwaway. Answers "what if the main app owned ffmpeg and the screen were just a consumer, and
would that get us Chromecast?" Findings: `plans/host-side-streaming-spike.md`.

Deliberately **not** in `KHost.slnx` and deliberately opted out of central package management, so
it cannot affect the real build.

```bash
# 1. the host that owns ffmpeg
cd KHost.Spike.StreamHost && dotnet run
#    -> prints http://<lan-ip>:5480

# 2. start a transcode (any media file ffmpeg can read)
curl -X POST http://127.0.0.1:5480/api/session \
     -H 'content-type: application/json' \
     -d '{"filePath":"/absolute/path/song.mp4","offset":0,"pitch":0}'

# 3. play that one URL from as many consumers as you like
cd KHost.Spike.ScreenConsumer && dotnet run -- --playlist <playlistUrl>
```

`http://<lan-ip>:5480/` is a browser consumer plus a session list. The same URL goes to a
Chromecast sender, a smart TV, or VLC — that equivalence is the point of the spike.

`GET /api/cast/discover` browses for `_googlecast._tcp` (macOS only, shells out to `dns-sd`).
It found nothing on the network this was written on, so the Cast control path is untested.
