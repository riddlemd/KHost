using System.Text;

namespace KHost.Screen.FFmpeg;

/// <summary>
/// Reads interleaved AVI chunks from a non-seekable stream (pipe).
/// Only parses enough structure to extract raw video (<c>##dc</c>/<c>##db</c>)
/// and audio (<c>##wb</c>) data chunks from the <c>movi</c> list.
/// </summary>
internal sealed class AviDemuxer
{
    private readonly Stream _stream;
    private readonly byte[] _hdr = new byte[8]; // reusable FourCC + size read buffer

    public AviDemuxer(Stream stream) => _stream = stream;

    /// <summary>
    /// Skips past RIFF and header lists, positioning the reader at the first
    /// data chunk inside the <c>movi</c> list.
    /// </summary>
    public void SkipToMovi()
    {
        // RIFF 'AVI '
        ReadFourCC();      // "RIFF"
        ReadUInt32();      // total size (0 for piped output)
        ReadFourCC();      // "AVI "

        // Walk top-level chunks until we reach the 'movi' LIST.
        while (true)
        {
            string cc = ReadFourCC();
            uint size = ReadUInt32();

            if (cc == "LIST")
            {
                string listType = ReadFourCC();
                if (listType == "movi")
                    return; // now positioned at first movi chunk
                Skip(size - 4);
            }
            else
            {
                Skip(size + (size & 1)); // data + optional pad byte
            }
        }
    }

    /// <summary>
    /// Reads the next data chunk from the movi section.
    /// Returns null at end-of-stream.
    /// </summary>
    public AviChunk? ReadChunk()
    {
        while (true)
        {
            if (!TryReadExact(_hdr, 0, 8))
                return null; // EOF

            string cc = Encoding.ASCII.GetString(_hdr, 0, 4);
            uint size = BitConverter.ToUInt32(_hdr, 4);

            // Nested LIST (e.g. 'rec ' in OpenDML AVI) — enter it transparently.
            if (cc == "LIST")
            {
                if (!TryReadExact(_hdr, 0, 4)) return null; // list type
                continue; // next inner chunk
            }

            // JUNK / idx1 / other non-stream chunks — skip.
            if (cc.Length < 4 || !char.IsDigit(cc[0]) || !char.IsDigit(cc[1]))
            {
                Skip(size + (size & 1));
                continue;
            }

            byte[] data = new byte[size];
            if (!TryReadExact(data, 0, (int)size))
                return null;

            if ((size & 1) != 0)
                _stream.ReadByte(); // alignment pad

            char tag = cc[2]; // 'd' = video, 'w' = audio
            return new AviChunk(
                StreamIndex: (cc[0] - '0') * 10 + (cc[1] - '0'),
                IsVideo: tag == 'd',
                IsAudio: tag == 'w',
                Data: data);
        }
    }

    private string ReadFourCC()
    {
        ReadExact(_hdr, 0, 4);
        return Encoding.ASCII.GetString(_hdr, 0, 4);
    }

    private uint ReadUInt32()
    {
        ReadExact(_hdr, 0, 4);
        return BitConverter.ToUInt32(_hdr, 0);
    }

    private void Skip(uint bytes)
    {
        byte[] trash = new byte[Math.Min(bytes, 8192)];
        uint rem = bytes;
        while (rem > 0)
        {
            int n = _stream.Read(trash, 0, (int)Math.Min(rem, (uint)trash.Length));
            if (n == 0) throw new EndOfStreamException();
            rem -= (uint)n;
        }
    }

    private void ReadExact(byte[] buf, int offset, int count)
    {
        if (!TryReadExact(buf, offset, count))
            throw new EndOfStreamException();
    }

    private bool TryReadExact(byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = _stream.Read(buf, offset + total, count - total);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }
}

/// <summary>A single demuxed chunk from the AVI movi section.</summary>
internal sealed record AviChunk(int StreamIndex, bool IsVideo, bool IsAudio, byte[] Data);
