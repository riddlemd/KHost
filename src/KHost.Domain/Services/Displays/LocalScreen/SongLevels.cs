using System.Buffers.Binary;
using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>One audio input to a levels read: a file or URL ffmpeg opens, at a gain.</summary>
/// <param name="Input">A local path or an http address.</param>
/// <param name="Volume">0-100, as a stem carries it; 100 for a single file.</param>
public sealed record SongLevelsInput(string Input, int Volume = 100);

/// <summary>A song's loudness per band and channel, 30 times a second of song time, for a screen
/// that cannot listen to the song itself.</summary>
/// <remarks>
/// <para>Shaped for butterchurn, which is handed a 1024-sample waveform each frame, runs its own FFT
/// over it and keeps only how each of three bands (20-320Hz, 320-2800Hz, 2800-11025Hz) compares with
/// its own running average. The screen rebuilds a waveform from these levels, a tone per band, so
/// that ratio is what has to survive: each band is normalised against its own loudest moment, and
/// the band edges sit on butterchurn's two splits.</para>
/// <para>Layout, little-endian: <c>"KHLV"</c>, version, frames per second, band count, channel
/// count, then a <c>uint32</c> frame count; after that one byte per band per channel per frame,
/// frame-major then channel then band. A byte is a quarter of a decibel above a floor 63.75dB under
/// that band's loudest. <c>screen-ui/visualiser.js</c> reads this and carries the same band
/// centres — change one and change the other.</para>
/// </remarks>
public static class SongLevels
{
    public const int FramesPerSecond = 30;
    public const int Channels = 2;
    public const byte Version = 1;
    public const int HeaderLength = 12;

    /// <summary>Half of it still reaches butterchurn's 11025Hz ceiling.</summary>
    public const int SampleRate = 22050;

    /// <summary>Samples per analysis window: 21.5Hz bins, fine enough to split the bass.</summary>
    internal const int WindowSize = 1024;

    /// <summary>Where one band ends and the next begins, in Hz. 320 and 2800 are butterchurn's own
    /// splits, so no band is counted in two of its three.</summary>
    public static readonly IReadOnlyList<double> BandEdges = [20, 50, 125, 320, 700, 1400, 2800, 5600, 11025];

    public static int Bands => BandEdges.Count - 1;

    /// <summary>How far under its own loudest a band can go before it reads as silent.</summary>
    internal const double RangeDecibels = 63.75;

    /// <summary>A band that never comes within this of the song's loudest band is left quiet rather
    /// than stretched to full: normalised on its own, a band holding only hiss would draw as loud.</summary>
    internal const double QuietBandDecibels = 40;

    private static readonly byte[] Magic = "KHLV"u8.ToArray();

    /// <summary>Hop between frames, in samples.</summary>
    private static int Hop => SampleRate / FramesPerSecond;

    /// <summary>What to read for a song: the stems as loaded, else the file when it is one the host
    /// opens, else null.</summary>
    /// <remarks>Stems are read at the gains they were loaded with — what the room hears before
    /// anyone moves a fader. A <c>.cdg</c> is read through the audio beside it. A file in a
    /// provider's own container, loaded without stems, answers null: nothing the host can open.
    /// </remarks>
    /// <param name="resolveStem">A stem's URL as ffmpeg should open it, or null when it cannot.</param>
    public static IReadOnlyList<SongLevelsInput>? InputsFor(string filePath, DisplayLoad load, Func<string, string?> resolveStem)
    {
        if (load.Stems.Count > 0)
        {
            var inputs = new List<SongLevelsInput>();

            foreach (var stem in load.Stems)
            {
                if (stem.Volume <= 0) continue;
                if (resolveStem(stem.Url) is not { Length: > 0 } input) return null;

                inputs.Add(new SongLevelsInput(input, stem.Volume));
            }

            return inputs.Count > 0 ? inputs : null;
        }

        if (MediaFormats.IsGraphicsOnlyKaraoke(filePath))
            return MediaFormats.FindKaraokeAudio(filePath) is { } audio ? [new SongLevelsInput(audio)] : null;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return MediaFormats.AudioExtensions.Contains(extension) || MediaFormats.VideoExtensions.Contains(extension)
            ? [new SongLevelsInput(filePath)]
            : null;
    }

    /// <summary>ffmpeg's arguments to decode the inputs, summed, as 16-bit stereo PCM on stdout.</summary>
    /// <remarks>An argument list rather than one string, so a path is never re-quoted. One thread:
    /// this runs beside the song's own encode and must never be what slows it.</remarks>
    public static IReadOnlyList<string> BuildArguments(IReadOnlyList<SongLevelsInput> inputs)
    {
        if (inputs.Count == 0) throw new ArgumentException("Nothing to read levels from.", nameof(inputs));

        var arguments = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-threads", "1" };

        foreach (var input in inputs) arguments.AddRange(["-vn", "-i", input.Input]);

        if (inputs.Count == 1 && inputs[0].Volume >= 100)
        {
            arguments.AddRange(["-map", "0:a:0"]);
        }
        else
        {
            // normalize=0, as the host's own mix: amix otherwise divides by the input count.
            var stages = inputs.Select((input, index) => FormattableString.Invariant(
                $"[{index}:a:0]volume={input.Volume / 100.0:F3}[s{index}]"));
            var labels = string.Concat(inputs.Select((_, index) => $"[s{index}]"));

            arguments.AddRange([
                "-filter_complex",
                string.Join(';', stages) + FormattableString.Invariant($";{labels}amix=inputs={inputs.Count}:normalize=0[x]"),
                "-map", "[x]",
            ]);
        }

        arguments.AddRange([
            "-ac", Channels.ToString(CultureInfo.InvariantCulture),
            "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
            "-f", "s16le", "pipe:1",
        ]);

        return arguments;
    }

    /// <summary>Reads interleaved 16-bit stereo PCM at <see cref="SampleRate"/> to its end and
    /// returns the encoded track.</summary>
    public static async Task<byte[]> AnalyseAsync(Stream pcm, CancellationToken cancellationToken = default)
    {
        var analyser = new Analyser();
        var buffer = new byte[64 * 1024];
        var carry = 0;

        while (true)
        {
            var read = await pcm.ReadAsync(buffer.AsMemory(carry), cancellationToken);
            if (read == 0) break;

            var available = carry + read;
            var whole = available - available % (2 * Channels);

            for (var offset = 0; offset < whole; offset += 2 * Channels)
            {
                analyser.Add(
                    BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset)) / 32768f,
                    BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 2)) / 32768f);
            }

            // A read can end mid-sample; the remainder leads the next one.
            carry = available - whole;
            if (carry > 0) Buffer.BlockCopy(buffer, whole, buffer, 0, carry);
        }

        return Encode(analyser.Finish());
    }

    /// <summary>Quantises per-frame band powers (decibels, [frame][channel][band]) into the track.</summary>
    internal static byte[] Encode(IReadOnlyList<double[][]> frames)
    {
        var bands = Bands;
        var reference = new double[bands];
        Array.Fill(reference, double.NegativeInfinity);

        foreach (var frame in frames)
            foreach (var channel in frame)
                for (var band = 0; band < bands; band++)
                    reference[band] = Math.Max(reference[band], channel[band]);

        var loudest = reference.Max();
        for (var band = 0; band < bands; band++)
            reference[band] = Math.Max(reference[band], loudest - QuietBandDecibels);

        var output = new byte[HeaderLength + frames.Count * Channels * bands];
        Magic.CopyTo(output, 0);
        output[4] = Version;
        output[5] = FramesPerSecond;
        output[6] = (byte)bands;
        output[7] = Channels;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), (uint)frames.Count);

        var at = HeaderLength;
        foreach (var frame in frames)
            for (var channel = 0; channel < Channels; channel++)
                for (var band = 0; band < bands; band++)
                    output[at++] = Quantise(frame[channel][band], reference[band]);

        return output;
    }

    internal static byte Quantise(double decibels, double reference)
    {
        if (double.IsNegativeInfinity(reference) || double.IsNaN(decibels)) return 0;

        var steps = Math.Round((decibels - reference + RangeDecibels) * 4);
        return (byte)Math.Clamp(steps, 0, 255);
    }

    /// <summary>The track's frames as [frame][channel][band] bytes; null for anything that is not one.</summary>
    public static byte[][][]? Decode(ReadOnlySpan<byte> track)
    {
        if (track.Length < HeaderLength || !track[..4].SequenceEqual(Magic) || track[4] != Version) return null;

        int bands = track[6], channels = track[7];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(track[8..]);
        if (bands == 0 || channels == 0 || track.Length != HeaderLength + (long)count * bands * channels) return null;

        var frames = new byte[count][][];
        var at = HeaderLength;

        for (var frame = 0; frame < count; frame++)
        {
            frames[frame] = new byte[channels][];
            for (var channel = 0; channel < channels; channel++)
            {
                frames[frame][channel] = track.Slice(at, bands).ToArray();
                at += bands;
            }
        }

        return frames;
    }

    /// <summary>Band power per window, a window every <see cref="Hop"/> samples from the first.</summary>
    private sealed class Analyser
    {
        private static readonly double[] Hann = [.. Enumerable.Range(0, WindowSize)
            .Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (WindowSize - 1)))];

        /// <summary>The FFT bins each band sums, [first, last).</summary>
        private static readonly (int First, int Last)[] BandBins = [.. Enumerable.Range(0, Bands).Select(band => (
            Math.Max(1, (int)Math.Ceiling(BandEdges[band] * WindowSize / SampleRate)),
            Math.Min(WindowSize / 2, (int)Math.Ceiling(BandEdges[band + 1] * WindowSize / SampleRate))))];

        private readonly List<double[][]> _frames = [];
        private readonly float[][] _pending = [new float[WindowSize * 4], new float[WindowSize * 4]];
        private readonly double[] _real = new double[WindowSize];
        private readonly double[] _imaginary = new double[WindowSize];

        /// <summary>Samples held; the first of them is where the next window starts.</summary>
        private int _count;

        public void Add(float left, float right)
        {
            if (_count == _pending[0].Length)
            {
                // Only the unanalysed tail is ever held, so this happens once, if at all.
                Array.Resize(ref _pending[0], _count * 2);
                Array.Resize(ref _pending[1], _count * 2);
            }

            _pending[0][_count] = left;
            _pending[1][_count] = right;
            _count++;

            if (_count < WindowSize) return;

            AnalyseWindow(WindowSize);
            Consume(Hop);
        }

        /// <summary>The frames, and the tail's too: a window short of samples is padded with silence.</summary>
        public IReadOnlyList<double[][]> Finish()
        {
            while (_count > 0)
            {
                AnalyseWindow(Math.Min(_count, WindowSize));
                Consume(Math.Min(_count, Hop));
            }

            return _frames;
        }

        private void Consume(int samples)
        {
            _count -= samples;
            Array.Copy(_pending[0], samples, _pending[0], 0, _count);
            Array.Copy(_pending[1], samples, _pending[1], 0, _count);
        }

        private void AnalyseWindow(int available)
        {
            var frame = new double[Channels][];

            for (var channel = 0; channel < Channels; channel++)
            {
                for (var n = 0; n < WindowSize; n++)
                {
                    _real[n] = n < available ? _pending[channel][n] * Hann[n] : 0;
                    _imaginary[n] = 0;
                }

                Fft(_real, _imaginary);

                frame[channel] = new double[Bands];
                for (var band = 0; band < Bands; band++)
                {
                    var power = 0.0;
                    for (var bin = BandBins[band].First; bin < BandBins[band].Last; bin++)
                        power += _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin];

                    frame[channel][band] = power > 0 ? 10 * Math.Log10(power) : double.NegativeInfinity;
                }
            }

            _frames.Add(frame);
        }

        /// <summary>In-place radix-2 FFT; the length must be a power of two.</summary>
        private static void Fft(double[] real, double[] imaginary)
        {
            var length = real.Length;

            for (int i = 1, j = 0; i < length; i++)
            {
                var bit = length >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }

            for (var size = 2; size <= length; size <<= 1)
            {
                var angle = -2 * Math.PI / size;
                double stepReal = Math.Cos(angle), stepImaginary = Math.Sin(angle);

                for (var start = 0; start < length; start += size)
                {
                    double wReal = 1, wImaginary = 0;

                    for (var k = 0; k < size / 2; k++)
                    {
                        int even = start + k, odd = even + size / 2;
                        var tReal = real[odd] * wReal - imaginary[odd] * wImaginary;
                        var tImaginary = real[odd] * wImaginary + imaginary[odd] * wReal;

                        real[odd] = real[even] - tReal;
                        imaginary[odd] = imaginary[even] - tImaginary;
                        real[even] += tReal;
                        imaginary[even] += tImaginary;

                        (wReal, wImaginary) = (wReal * stepReal - wImaginary * stepImaginary, wReal * stepImaginary + wImaginary * stepReal);
                    }
                }
            }
        }
    }
}
