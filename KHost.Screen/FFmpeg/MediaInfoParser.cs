using KHost.Abstractions.MediaPlayer;
using System.Globalization;
using System.Text.Json;

namespace KHost.Screen.FFmpeg;

/// <summary>Parses ffprobe JSON output into a <see cref="VideoInfo"/>.</summary>
internal static class MediaInfoParser
{
    public static IMediaPlayer.MediaInfo Parse(string filePath, string ffprobeJson)
    {
        using var doc = JsonDocument.Parse(ffprobeJson);
        var root = doc.RootElement;

        double durationSecs = 0;
        int width = 0, height = 0;
        double fps = 25;
        int sampleRate = 44100, channels = 2;
        bool hasVideo = false, hasAudio = false;

        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durProp))
            TryParseDouble(durProp.GetString(), out durationSecs);

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                string codecType = stream.TryGetProperty("codec_type", out var ct)
                    ? ct.GetString() ?? "" : "";

                if (codecType == "video" && !hasVideo)
                {
                    hasVideo = true;

                    if (stream.TryGetProperty("width", out var w)) width = w.GetInt32();
                    if (stream.TryGetProperty("height", out var h)) height = h.GetInt32();

                    if (stream.TryGetProperty("r_frame_rate", out var fpsP))
                    {
                        var s = fpsP.GetString() ?? "";
                        var slash = s.IndexOf('/');
                        if (slash > 0 &&
                            TryParseDouble(s[..slash], out double num) &&
                            TryParseDouble(s[(slash + 1)..], out double den) &&
                            den > 0)
                            fps = num / den;
                    }

                    if (durationSecs == 0 && stream.TryGetProperty("duration", out var sd))
                        TryParseDouble(sd.GetString(), out durationSecs);
                }
                else if (codecType == "audio" && !hasAudio)
                {
                    hasAudio = true;

                    if (stream.TryGetProperty("sample_rate", out var sr))
                        int.TryParse(sr.GetString(), out sampleRate);
                    if (stream.TryGetProperty("channels", out var ch))
                        channels = ch.GetInt32();
                }
            }
        }

        var auxiliaryFilePaths = new List<string>();

        var fileExtension = Path.GetExtension(filePath);
        var filePathWithoutExtension = filePath[..^fileExtension.Length];
        var statefulFormat = false;

        if (fileExtension.Equals(".cdg", StringComparison.InvariantCultureIgnoreCase))
        {
            var mp3FilePath = $"{filePathWithoutExtension}.mp3";

            if (File.Exists(mp3FilePath))
            {
                auxiliaryFilePaths.Add(mp3FilePath);
                hasAudio = true;
                statefulFormat = true;
            }
        }

        var output = new IMediaPlayer.MediaInfo()
        {
            FilePath = filePath,
            AuxiliaryFilePaths = [.. auxiliaryFilePaths],
            Duration = TimeSpan.FromSeconds(durationSecs),
            Width = width,
            Height = height,
            Fps = fps > 0 ? fps : 25,
            AudioSampleRate = sampleRate,
            AudioChannels = channels,
            HasVideo = hasVideo,
            HasAudio = hasAudio,
            StatefulFormat = statefulFormat
        };

        return output;
    }

    private static bool TryParseDouble(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
