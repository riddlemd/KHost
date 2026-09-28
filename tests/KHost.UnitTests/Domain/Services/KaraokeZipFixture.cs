using System.IO.Compression;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Builds zips entry by entry, including the malformed ones no archiver would produce.</summary>
internal static class KaraokeZipFixture
{
    public static string Write(string directory, string fileName, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(directory, fileName);

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return path;
    }

    /// <summary>Sets the "encrypted" flag on every entry, which is all a reader goes by.</summary>
    /// <remarks>.NET cannot write an encrypted zip, so the flag is set by hand in both the local
    /// headers and the central directory.</remarks>
    public static void MarkEncrypted(string path)
    {
        var bytes = File.ReadAllBytes(path);

        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            if (bytes[i] != 0x50 || bytes[i + 1] != 0x4b) continue;

            if (bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04) bytes[i + 6] |= 1;
            else if (bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02) bytes[i + 8] |= 1;
        }

        File.WriteAllBytes(path, bytes);
    }

    public static byte[] Graphics => [0x09, 1, 0, 0, 3, 0, 0, 0];

    public static byte[] Audio => [0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0];
}
