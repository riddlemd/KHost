using System.Globalization;

namespace KHost.Domain.Services.VideoEncoding;

/// <summary>Which H.264 encoder a host wants: the first working hardware one, only hardware, or
/// always software.</summary>
public enum VideoEncoderPreference
{
    Auto,
    Hardware,
    Software,
}

/// <summary>One H.264 encoder and the arguments that drive it: rate control, profile, level, pixel
/// format and keyframes.</summary>
/// <remarks>Keyframes on time, not a frame count: -g is in frames, so it matches the segment length
/// at exactly one source frame rate, and the muxer can only cut where a keyframe already is. Every
/// profile forces them the same way.</remarks>
public sealed record VideoEncoderProfile(string Codec, bool IsHardware)
{
    public static readonly VideoEncoderProfile Software = new("libx264", false);
    public static readonly VideoEncoderProfile VideoToolbox = new("h264_videotoolbox", true);
    public static readonly VideoEncoderProfile QuickSync = new("h264_qsv", true);
    public static readonly VideoEncoderProfile Nvenc = new("h264_nvenc", true);
    public static readonly VideoEncoderProfile Amf = new("h264_amf", true);
    public static readonly VideoEncoderProfile MediaFoundation = new("h264_mf", true);

    /// <summary>The ffmpeg arguments from <c>-c:v</c> through the keyframe options.</summary>
    /// <param name="frameHeight">The output height when the caller knows it, else zero.</param>
    public string Arguments(int frameHeight, int segment)
    {
        var keyframes = string.Format(
            CultureInfo.InvariantCulture, " -force_key_frames \"expr:gte(t,n_forced*{0})\"", segment);

        if (!IsHardware)
        {
            return $" -c:v libx264 -preset veryfast -profile:v main -level {(frameHeight > 1080 ? "5.1" : "4.1")}"
                   + " -pix_fmt yuv420p" + keyframes + " -sc_threshold 0";
        }

        // A hardware encoder's default GOP is short (VideoToolbox: 12 frames), which adds keyframes
        // between the forced ones. The ceiling sits past any segment at 120fps so only time cuts.
        var gop = string.Format(CultureInfo.InvariantCulture, " -g {0}", segment * 120);

        // VideoToolbox refuses a 4K frame at 4.1, and a source video's size is unknown here, so an
        // unknown height leaves the level to the encoder.
        var known = frameHeight > 0;
        var dotted = frameHeight > 1080 ? "5.1" : "4.1";
        var ceiling = frameHeight > 1080 ? "40M" : "12M";

        return Codec switch
        {
            // -q:v is VideoToolbox's constant quality (Apple Silicon only); 75 measured level with
            // x264 veryfast's CRF 23 on SSIM. The ceiling only binds on pathological frames.
            "h264_videotoolbox" => " -c:v h264_videotoolbox -profile:v main"
                + (known ? $" -level {dotted}" : "")
                + $" -pix_fmt nv12 -q:v 75 -maxrate {ceiling}" + gop + keyframes,

            // ICQ via -global_quality. Its level is the generic integer (41), not a dotted name.
            "h264_qsv" => " -c:v h264_qsv -preset veryfast -profile:v main"
                + (known ? $" -level {(frameHeight > 1080 ? "51" : "41")}" : "")
                + " -pix_fmt nv12 -global_quality 25" + gop + " -forced_idr 1" + keyframes,

            // Constant quality capped by the ceiling: -b:v 0 lets -cq drive.
            "h264_nvenc" => " -c:v h264_nvenc -preset p4 -rc vbr -cq 23 -b:v 0"
                + $" -maxrate {ceiling} -bufsize {(frameHeight > 1080 ? "80M" : "24M")} -profile:v main"
                + (known ? $" -level {dotted}" : "")
                + " -pix_fmt nv12" + gop + " -forced-idr 1 -no-scenecut 1" + keyframes,

            "h264_amf" => " -c:v h264_amf -quality speed -rc cqp -qp_i 22 -qp_p 24 -profile:v main"
                + (known ? $" -level {dotted}" : "")
                + " -pix_fmt nv12" + gop + " -forced_idr 1" + keyframes,

            // The last resort: a bitrate, as not every vendor's transform offers a quality mode.
            "h264_mf" => " -c:v h264_mf -hw_encoding 1 -rate_control u_vbr"
                + $" -b:v {(frameHeight > 1080 ? "20M" : "8M")} -pix_fmt nv12" + gop + keyframes,

            _ => throw new InvalidOperationException($"No arguments for encoder '{Codec}'."),
        };
    }
}
