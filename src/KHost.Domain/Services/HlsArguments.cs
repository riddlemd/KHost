using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Common.Media;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.VideoEncoding;

namespace KHost.Domain.Services;

/// <summary>What one song's encode reads: the same for every run of it but where the run starts.</summary>
/// <param name="StartOffset">Song time the run starts at.</param>
internal sealed record EncodeInputs(string FilePath, TimeSpan StartOffset, int Pitch, int Tempo, int SegmentSeconds)
{
    /// <summary>The audio half of a graphics-only pair, read as a second input.</summary>
    public string? CompanionAudioPath { get; init; }

    public AudioMix? Mix { get; init; }

    /// <summary>Painted words laid over the picture, fed down a pipe.</summary>
    public BurnInOverlay? BurnIn { get; init; }

    public int GraphicsHeight { get; init; } = GraphicsScaling.Off;

    /// <summary>The display draws the song's timed words itself, so the encode carries only the
    /// source's own moving picture: never a cover, and nothing from an audio file
    /// (<see cref="SongBackdrops"/>).</summary>
    public bool DisplayDrawsWords { get; init; }
}

/// <summary>Where one ffmpeg run sits in its session's playlist.</summary>
/// <param name="After">Output the playlist already lists, which this run appends after; zero for
/// the first run.</param>
/// <param name="LeftOpen">Whether the run leaves off the end marker, for the host to write or to
/// carry the song on past.</param>
internal readonly record struct PlaylistPart(TimeSpan After, bool LeftOpen)
{
    public static readonly PlaylistPart Whole = default;

    public bool Appends => After > TimeSpan.Zero;
}

/// <summary>One stem as ffmpeg reads it: a local path or an http address, and its level.</summary>
internal sealed record StemInput(string Input, AudioTrackRole Role, int Volume);

/// <summary>Every codec, filter and muxer decision for a song's encode, as a command line; static so
/// a test asserts it without ffmpeg.</summary>
internal static class HlsArguments
{
    /// <summary>Repeats the last frame without end; <c>-shortest</c> is what stops it.</summary>
    private const string HoldLastFrame = "tpad=stop=-1:stop_mode=clone";

    /// <summary>The rate a graphics-only source is brought to.</summary>
    private const int GraphicsFramesPerSecond = 30;

    /// <summary>EVENT playlist type so a consumer can start on the first segment.</summary>
    /// <remarks>H.264 Main@4.1 + AAC-LC decodes in every browser and in WKWebView, which is what
    /// LocalScreen renders through; a display provider's device is a third consumer it also suits.
    /// A frame taller than 1080 lines is Main@5.1, the lowest level that allows one. A display with
    /// no H.264 (Oriel on stock Chromium) is handed <see cref="VideoEncoderProfile.Vp9"/>, which
    /// brings Opus and fMP4 with it, picture or not.</remarks>
    internal static string BuildArguments(
        EncodeInputs inputs, VideoEncoderProfile? encoder = null, PlaylistPart part = default)
    {
        var (filePath, startOffset, pitch, tempo, segmentSeconds) = inputs;
        var companionAudioPath = inputs.CompanionAudioPath;
        var burnIn = inputs.BurnIn;
        var arguments = "-hide_banner -loglevel error";

        // CDG decoding is stateful, so an input seek lands mid-packet and the graphics decode to
        // garbage. Such a source seeks on the output instead and eats the frames.
        //
        // Asked of the graphics, not of the companion audio: a .cdg with no audio beside it is still
        // a stateful decode, and keying this off the pairing seeked it on the input and drew
        // garbage for the one case that already had no sound. Reused below for the frame-rate
        // decision, which asks the same question of the same file.
        var isGraphicsOnly = IsGraphicsOnly(filePath);

        // A .cdg decodes a frame only when its graphics change, so its picture stops at the last
        // change, seconds before the audio. A player then stalls on the missing video rather than
        // ending, and the song never concludes. The picture is held until the audio ends it.
        // Never without audio: an endless picture with nothing to end it would never finish.
        var holdsPictureToTheAudio = isGraphicsOnly && companionAudioPath is not null;

        // Zero when unscaled: the native picture, as it always was.
        var scaledHeight = isGraphicsOnly && inputs.GraphicsHeight > GraphicsScaling.Off ? inputs.GraphicsHeight : 0;

        var picksOwnPicture = PicksOwnPicture(inputs);
        var hasNoPicture = !HasPicture(inputs);

        if (startOffset > TimeSpan.Zero && !isGraphicsOnly)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        arguments += $" -i \"{filePath}\"";

        if (companionAudioPath is not null)
        {
            arguments += $" -i \"{companionAudioPath}\"";

            // Without the mapping ffmpeg picks one stream per type from the first input that has
            // one, and a .cdg carries no audio at all. A burn-in and a held picture each map their
            // own, after their graph.
            if (burnIn is null && !holdsPictureToTheAudio) arguments += " -map 0:v:0 -map 1:a:0";
        }

        // Inputs come before the output seek below, or that -ss would bind to the next input.
        var pipeInput = companionAudioPath is null ? 1 : 2;
        if (burnIn is not null) arguments += BurnInInputs(burnIn);

        if (startOffset > TimeSpan.Zero && isGraphicsOnly)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        var segment = Math.Max(1, segmentSeconds);

        // A .cdg only emits a frame when the graphics change, so x264 is handed a wildly variable
        // rate and encodes far more than the picture needs. Measured on two songs: 110 and 154
        // CPU-seconds without this against 33 and 44 with it, for the same segments either way.
        if (isGraphicsOnly)
            arguments += $" -r {GraphicsFramesPerSecond}";

        // The filters above all run on the CPU, so whichever encoder is chosen sees the same frames.
        if (!hasNoPicture)
            arguments += (encoder ?? VideoEncoderProfile.Software).Arguments(burnIn?.Height ?? scaledHeight, segment);

        var audioFilter = BuildAudioFilter(pitch, tempo);
        var mixGraph = BuildMixGraph(inputs.Mix, audioFilter);

        // Capital V matches no attached picture, so a video's own picture survives and a cover does not.
        var pictureMap = hasNoPicture ? "" : picksOwnPicture ? " -map 0:V:0?" : " -map 0:v:0?";

        if (burnIn is not null)
        {
            arguments += BurnInMapping(
                burnIn, pipeInput, tempo, companionAudioPath is null ? 0 : 1, mixGraph, audioFilter,
                isGraphicsOnly, holdsPictureToTheAudio);
        }
        else if (holdsPictureToTheAudio)
        {
            var picture = GraphicsOnCanvas(
                tempo, GraphicsFramesPerSecond, GraphicsFill(scaledHeight), "v");

            arguments += mixGraph.Length > 0
                ? $" -filter_complex \"{picture};{mixGraph}\" -map \"[v]\" -map \"[a]\""
                : $" -filter_complex \"{picture}\" -map \"[v]\" -map 1:a:0"
                  + (audioFilter.Length > 0 ? $" -af \"{audioFilter}\"" : "");
        }
        else if (mixGraph.Length > 0)
        {
            // Explicit maps: the graph names the audio output, and without saying so ffmpeg would
            // also carry one of the raw tracks through beside it. The picture is optional ("?")
            // because a multi-track container can be audio alone — a mix is the one case where a
            // missing video stream would otherwise be a fatal unmatched mapping rather than a
            // silently dropped one.
            arguments += $" -filter_complex \"{mixGraph}\"{pictureMap} -map \"[a]\"";
        }
        else
        {
            // Any -map switches off ffmpeg's own pick for every stream type, so the audio is named too.
            if (picksOwnPicture) arguments += $"{pictureMap} -map 0:a:0?";
            if (audioFilter.Length > 0) arguments += $" -af \"{audioFilter}\"";
        }

        // -vf rather than a filter_complex: ffmpeg drops it silently on a source with no video
        // rather than failing on an unmatched label. A burn-in and a held picture retime inside
        // their own graphs instead.
        var videoFilter = scaledHeight > 0
            ? string.Join(',', new[]
            {
                BuildVideoFilter(tempo), $"fps={GraphicsFramesPerSecond}", GraphicsFill(scaledHeight),
            }.Where(f => f.Length > 0))
            : BuildVideoFilter(tempo);
        if (videoFilter.Length > 0 && burnIn is null && !holdsPictureToTheAudio && !hasNoPicture)
            arguments += $" -vf \"{videoFilter}\"";

        if (holdsPictureToTheAudio) arguments += " -shortest";

        return arguments + HlsOutput(segment, part, encoder?.IsOpenCodec == true);
    }

    /// <summary>Whether the encode cuts any video, and so has an encoder to choose: not for a display
    /// drawing its own words over an audio file.</summary>
    internal static bool HasPicture(EncodeInputs inputs)
        => !(PicksOwnPicture(inputs) && !SongBackdrops.MayShowPictureFrom(inputs.FilePath));

    /// <remarks>Left to itself ffmpeg encodes an MP3's cover art as a one-frame video, which a screen
    /// then shows under the words. A burn-in maps its own picture.</remarks>
    private static bool PicksOwnPicture(EncodeInputs inputs) => inputs.DisplayDrawsWords && inputs.BurnIn is null;


    /// <summary>The encode for a song that arrived as separate stems: every stem an input, mixed at
    /// its own level, then keyed and retimed as one.</summary>
    /// <remarks>Audio alone unless words are burned in, as a stems-only song has no picture of its own
    /// and a display drawing its own words wants none. Burned-in words go over black, a stem set
    /// having no picture of its own.
    /// </remarks>
    internal static string BuildStemArguments(
        IReadOnlyList<StemInput> stems,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        int segmentSeconds,
        BurnInOverlay? burnIn = null,
        VideoEncoderProfile? encoder = null,
        PlaylistPart part = default)
    {
        var arguments = "-hide_banner -loglevel error";

        // Each input seeks on its own: -ss binds to the next -i only.
        var seek = startOffset > TimeSpan.Zero
            ? string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds)
            : "";

        foreach (var stem in stems) arguments += $"{seek} -i \"{stem.Input}\"";

        if (burnIn is not null) arguments += BurnInInputs(burnIn);

        var segment = Math.Max(1, segmentSeconds);
        var audioFilter = BuildAudioFilter(pitch, tempo);
        var mixGraph = MixGraph(
            stems.Select((stem, index) => (Pad: $"{index}:a:0", stem.Role, Index: index, stem.Volume)), audioFilter);

        if (burnIn is not null)
        {
            arguments += (encoder ?? VideoEncoderProfile.Software).Arguments(burnIn.Height, segment)
                         + BurnInMapping(burnIn, stems.Count, tempo, 0, mixGraph, audioFilter, false, false);
        }
        else
        {
            arguments += $" -filter_complex \"{mixGraph}\" -map \"[a]\"";
        }

        return arguments + HlsOutput(segment, part, encoder?.IsOpenCodec == true);
    }

    /// <remarks>MPEG-TS segments rather than fMP4: TS plays everywhere, and CMAF needs a newer device
    /// than some display providers reach. Wanting CMAF means asking the provider first.
    ///
    /// <para>A run appending to a playlist numbers on from it and shifts its timestamps to follow
    /// what is listed; ffmpeg marks the join a discontinuity itself.</para>
    ///
    /// <para>An open-codec encode (VP9, for a display with no H.264) carries Opus, which needs 48 kHz,
    /// in fMP4 segments: neither VP9 nor Opus travels in MPEG-TS for hls.js.</para></remarks>
    private static string HlsOutput(int segment, PlaylistPart part, bool openCodecs)
        => (openCodecs ? " -c:a libopus -ar 48000 -ac 2 -b:a 128k" : " -c:a aac -ar 44100 -ac 2 -b:a 128k")
           + (part.Appends
               ? string.Format(CultureInfo.InvariantCulture, " -output_ts_offset {0:F6}", part.After.TotalSeconds)
               : "")
           + string.Format(
               CultureInfo.InvariantCulture,
               " -f hls -hls_time {0} -hls_playlist_type event -hls_flags independent_segments{1}{2}",
               segment,
               part.LeftOpen ? "+omit_endlist" : "",
               part.Appends ? "+append_list" : "")
           + (openCodecs
               ? $" -hls_segment_type fmp4 -hls_fmp4_init_filename {InitSegmentFileName} -hls_segment_filename seg_%05d.m4s"
               : " -hls_segment_filename seg_%05d.ts")
           + $" {HlsPlaylistFile.FileName}";

    /// <summary>An fMP4 stream's initialisation segment, which every media segment needs first.</summary>
    internal const string InitSegmentFileName = "init.mp4";

    /// <summary>A loose .cdg: this is asked of what ffmpeg opens, after a zipped pair is written out.</summary>
    internal static bool IsGraphicsOnly(string filePath) => MediaFormats.IsGraphicsOnlyKaraoke(filePath);

    /// <summary>A .cdg holds only graphics; its audio is the same-named audio file beside it.</summary>
    /// <remarks>Through <see cref="MediaFormats.FindKaraokeAudio"/>, so the importer, the probe and
    /// the renderer all decide a pair the same way.</remarks>
    internal static string? ResolveCompanionAudio(string filePath)
        => IsGraphicsOnly(filePath) ? MediaFormats.FindKaraokeAudio(filePath) : null;

    /// <summary>The painted words, read raw off the pipe, and the picture they go over when the
    /// source has none of its own.</summary>
    private static string BurnInInputs(BurnInOverlay burnIn)
    {
        var inputs = string.Format(
            CultureInfo.InvariantCulture,
            " -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -thread_queue_size 64 -i pipe:0",
            burnIn.Width, burnIn.Height, burnIn.FramesPerSecond);

        return burnIn.Base switch
        {
            BurnInBase.Fill => inputs + string.Format(
                CultureInfo.InvariantCulture,
                " -f lavfi -i color=c=black:s={0}x{1}:r={2}",
                burnIn.Width, burnIn.Height, burnIn.FramesPerSecond),
            _ => inputs,
        };
    }

    /// <summary>One graph laying the painted frames over the picture, carrying the audio graph with
    /// it when there is one, and the maps that pick both.</summary>
    /// <remarks>The base is always the picture and the words always the overlay: <c>overlay</c> takes
    /// its alpha from the base, so the other way round composites onto transparency and the words
    /// vanish from an encode that still runs cleanly.
    ///
    /// <para>The base is brought to the painted frames' rate, because a source that only emits a
    /// frame when its picture changes would hold the chase still between them. A source's own video
    /// is fitted inside the frame. An endless base ends with the words (<c>shortest=1</c>); a finite one ends the picture
    /// itself and carries on without them if the words run out first.</para></remarks>
    private static string BurnInMapping(
        BurnInOverlay burnIn, int pipeInput, int tempo, int audioInput, string mixGraph, string audioFilter,
        bool isGraphicsOnly, bool holdsPictureToTheAudio)
    {
        var size = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", burnIn.Width, burnIn.Height);
        var rate = burnIn.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var baseInput = pipeInput + 1;
        var retime = BuildVideoFilter(tempo) is { Length: > 0 } filter ? filter + "," : "";

        var picture = burnIn.Base switch
        {
            // Scaled exactly as it is without words over it, so turning the words on moves no block.
            BurnInBase.SourceVideo when holdsPictureToTheAudio
                => GraphicsOnCanvas(tempo, burnIn.FramesPerSecond, GraphicsFit(burnIn.Width, burnIn.Height), "base"),
            BurnInBase.SourceVideo when isGraphicsOnly
                => $"[0:v:0]{retime}fps={rate},{GraphicsFit(burnIn.Width, burnIn.Height)}[base]",
            BurnInBase.SourceVideo => $"[0:v:0]{retime}"
                + $"fps={rate},scale={size}:force_original_aspect_ratio=decrease,"
                + $"pad={size}:(ow-iw)/2:(oh-ih)/2,setsar=1[base]",
            _ => $"[{baseInput}:v]setsar=1[base]",
        };

        var ending = burnIn.Base == BurnInBase.SourceVideo ? "eof_action=pass" : "shortest=1";
        var graph = $"{picture};[base][{pipeInput}:v]overlay=0:0:{ending}[v]";

        if (mixGraph.Length > 0)
            return $" -filter_complex \"{graph};{mixGraph}\" -map \"[v]\" -map \"[a]\"";

        // "?" so a source with no sound still encodes its picture rather than failing the map.
        var audio = $" -filter_complex \"{graph}\" -map \"[v]\" -map {audioInput}:a:0?";
        return audioFilter.Length > 0 ? audio + $" -af \"{audioFilter}\"" : audio;
    }

    private static string BuildAudioFilter(int pitch, int tempo)
    {
        var rate = StreamRate.FromTempo(tempo);

        if (pitch == 0 && rate == 1.0) return string.Empty;

        var ratio = Math.Pow(2.0, pitch / 12.0);

        // asetrate reinterprets whatever rate reaches it, so the leading resample is what makes
        // its base true: a 48kHz source would otherwise carry an uncorrected 44100/48000 as well.
        var stages = new List<string> { "aresample=44100" };

        if (pitch != 0)
        {
            stages.Add(FormattableString.Invariant($"asetrate=44100*{ratio:F6}"));
            stages.Add("aresample=44100");
        }

        // One factor, not two: asetrate already moved the speed by the pitch ratio, so undoing
        // that and applying the wanted tempo is a single atempo.
        stages.AddRange(TempoStages(rate / ratio));

        return string.Join(',', stages);
    }

    /// <summary>atempo rejects below 0.5, but pitch-up with tempo-down can reach 0.354 together.</summary>
    /// <remarks>Two chained stages cover the whole envelope.</remarks>
    private static IEnumerable<string> TempoStages(double factor)
    {
        if (Math.Abs(factor - 1.0) < 1e-9) yield break;

        if (factor >= 0.5)
        {
            yield return FormattableString.Invariant($"atempo={factor:F6}");
            yield break;
        }

        var stage = Math.Sqrt(factor);
        yield return FormattableString.Invariant($"atempo={stage:F6}");
        yield return FormattableString.Invariant($"atempo={stage:F6}");
    }

    /// <summary>Balances the named voices over the music; empty when there is nothing to balance.</summary>
    /// <remarks>Leaves the simpler -af path in place.</remarks>
    private static string BuildMixGraph(AudioMix? mix, string audioFilter)
    {
        if (mix is not { IsMixable: true }) return string.Empty;

        return MixGraph(
            mix.Tracks.Select(track => (Pad: $"0:a:{track.Index}", track.Role, track.Index, Volume: mix.VolumeFor(track))),
            audioFilter);
    }

    /// <summary>Each source pad at its own level, summed, then carried through the pitch and tempo
    /// chain; ends in <c>[a]</c>.</summary>
    private static string MixGraph(
        IEnumerable<(string Pad, AudioTrackRole Role, int Index, int Volume)> sources, string audioFilter)
    {
        var stages = new List<string>();
        var labels = new List<string>();

        foreach (var (pad, role, index, volume) in sources)
        {
            // Suffixed by index: a duet carries two leads, and a repeated pad label is an ffmpeg error.
            var label = role switch
            {
                AudioTrackRole.Music => "m",
                AudioTrackRole.Lead => "l",
                _ => "b",
            } + index.ToString(CultureInfo.InvariantCulture);

            stages.Add(FormattableString.Invariant($"[{pad}]volume={volume / 100.0:F3}[{label}]"));
            labels.Add($"[{label}]");
        }

        // normalize=0 or amix divides by the number of inputs, quietly dropping the whole mix by
        // several decibels the moment a second track joins.
        stages.Add(FormattableString.Invariant(
            $"{string.Concat(labels)}amix=inputs={labels.Count}:normalize=0[x]"));

        // The pitch and tempo chain rides on the mixed result rather than any one track.
        stages.Add(audioFilter.Length > 0 ? $"[x]{audioFilter}[a]" : "[x]anull[a]");

        return string.Join(';', stages);
    }

    /// <summary>A .cdg held to its audio, laid on black, and fitted into its frame; ends in
    /// <c>[<paramref name="label"/>]</c>.</summary>
    /// <remarks>
    /// <para>The canvas is what a disc that never draws still shows: with no decoded frame the
    /// graph emits nothing, and the encode produces no segment at all. Native size, so it is cheap.</para>
    /// <para>The order is load-bearing. The hold precedes <c>fps</c>, which drops the last frames
    /// at the end of its input. <c>fps</c> precedes the scale: a .cdg decodes up to 300 frames a
    /// second while it draws, and scaling each doubles the CPU. The overlay is RGB, since YUV
    /// halves the colour before the scale.</para>
    /// </remarks>
    private static string GraphicsOnCanvas(int tempo, int fps, string fit, string label)
        => string.Format(
               CultureInfo.InvariantCulture,
               "color=c=black:s={0}x{1}:r={2}[canvas];",
               GraphicsScaling.SourceWidth, GraphicsScaling.SourceHeight, fps)
           + $"[0:v:0]{HoldLastFrame},"
           + (BuildVideoFilter(tempo) is { Length: > 0 } retime ? retime + "," : "")
           + string.Format(CultureInfo.InvariantCulture, "fps={0}[graphics];", fps)
           + $"[canvas][graphics]overlay=format=rgb,{fit}[{label}]";

    /// <summary>Scales the native picture up to <paramref name="height"/> at its own shape, with no
    /// bands; zero leaves it native.</summary>
    /// <remarks>Whole pixels on nearest neighbour as far as they go, then one short bicubic step to the
    /// exact height where the height is not a multiple of the native one. Bicubic over lanczos: at
    /// ~1.1x it is as sharp, and rings less around hard colour edges.</remarks>
    internal static string GraphicsFill(int height)
    {
        if (height <= 0) return "setsar=1";

        var (width, _) = GraphicsScaling.PictureOfHeight(height);
        var factor = GraphicsScaling.WholeScaleFor(width, height);
        var finish = factor * GraphicsScaling.SourceHeight == height
            ? ""
            : string.Format(CultureInfo.InvariantCulture, "scale={0}:{1}:flags=bicubic,", width, height);

        return string.Format(CultureInfo.InvariantCulture, "scale=iw*{0}:ih*{0}:flags=neighbor,", factor)
               + finish + "setsar=1";
    }

    /// <summary>Scales the native picture by the largest whole number that fits the frame, on
    /// nearest neighbour, and centres it; a zero-sized frame leaves it native.</summary>
    /// <remarks>The burn-in frame only. A .cdg never reaches it, since its renderer burns no words.
    /// </remarks>
    internal static string GraphicsFit(int width, int height)
    {
        if (width <= 0 || height <= 0) return "setsar=1";

        var factor = GraphicsScaling.WholeScaleFor(width, height);

        return string.Format(
            CultureInfo.InvariantCulture,
            "scale=iw*{0}:ih*{0}:flags=neighbor,pad={1}:{2}:(ow-iw)/2:(oh-ih)/2,setsar=1",
            factor, width, height);
    }

    /// <summary>Retimes the picture: output frame rate becomes the source times the rate.</summary>
    /// <remarks>The keyframe expression is immune, since it is written in output time.</remarks>
    private static string BuildVideoFilter(int tempo)
    {
        var rate = StreamRate.FromTempo(tempo);

        return rate == 1.0
            ? string.Empty
            : FormattableString.Invariant($"setpts=PTS/{rate:F6}");
    }

}
