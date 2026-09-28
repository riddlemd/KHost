using KHost.Abstractions.Exceptions;
using KHost.Common.Media;
using KHost.Domain.Services;
using static KHost.UnitTests.Domain.Services.KaraokeZipFixture;

namespace KHost.UnitTests.Domain.Services;

public class KaraokeZipTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("khost-zip").FullName;
    private readonly string _output;

    public KaraokeZipTests() => _output = Directory.CreateDirectory(Path.Combine(_directory, "out")).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ExtractPairAsync_AFlatPair_LandsUnderFixedNamesWithTheAudioBesideTheGraphics()
    {
        var zip = Write(_directory, "Song.zip", ("Artist - Song.cdg", Graphics), ("Artist - Song.mp3", Audio));

        var graphics = await KaraokeZip.ExtractPairAsync(zip, _output);

        Assert.Equal(Path.Combine(_output, "karaoke.cdg"), graphics);
        Assert.Equal(Graphics, await File.ReadAllBytesAsync(graphics));

        // The rule every loose pair is found by finds this one too.
        var audio = MediaFormats.FindKaraokeAudio(graphics);
        Assert.Equal(Path.Combine(_output, "karaoke.mp3"), audio);
        Assert.Equal(Audio, await File.ReadAllBytesAsync(audio!));
    }

    [Fact]
    public async Task ExtractPairAsync_UpperCaseNamesAndAnotherAudio_StillPair()
    {
        var zip = Write(_directory, "Song.zip", ("SONG.CDG", Graphics), ("song.WAV", Audio));

        var graphics = await KaraokeZip.ExtractPairAsync(zip, _output);

        Assert.Equal(Path.Combine(_output, "karaoke.wav"), MediaFormats.FindKaraokeAudio(graphics));
    }

    [Fact]
    public void Validate_ArchiverClutter_IsPassedOver()
    {
        var zip = Write(_directory, "Song.zip",
            ("song.cdg", Graphics), ("song.mp3", Audio),
            ("__MACOSX/._song.cdg", [1]), (".DS_Store", [1]));

        KaraokeZip.Validate(zip);
    }

    public static TheoryData<string, (string, byte[])[]> NotOneSong => new()
    {
        { "nested", [("Song/song.cdg", Graphics), ("Song/song.mp3", Audio)] },
        { "two pairs", [("a.cdg", Graphics), ("a.mp3", Audio), ("b.cdg", Graphics), ("b.mp3", Audio)] },
        { "graphics only", [("song.cdg", Graphics)] },
        { "audio only", [("song.mp3", Audio)] },
        { "stems differ", [("song.cdg", Graphics), ("other.mp3", Audio)] },
        { "not audio", [("song.cdg", Graphics), ("song.txt", Audio)] },
        { "escaping entry", [("../song.cdg", Graphics), ("song.mp3", Audio)] },
        { "escaping backslash", [("..\\song.cdg", Graphics), ("song.mp3", Audio)] },
        { "extra file", [("song.cdg", Graphics), ("song.mp3", Audio), ("readme.txt", [1])] },
    };

    [Theory]
    [MemberData(nameof(NotOneSong))]
    public async Task ExtractPairAsync_NotOneFlatPair_IsRefusedAndWritesNothing(string shape, (string, byte[])[] entries)
    {
        var zip = Write(_directory, "Song.zip", entries);

        var thrown = await Assert.ThrowsAsync<KHostException>(() => KaraokeZip.ExtractPairAsync(zip, _output));

        Assert.True(thrown.ReferenceCode == KaraokeZip.ShapeCode, shape);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_output));
        Assert.Equal(new[] { _output, zip }.Order(), Directory.EnumerateFileSystemEntries(_directory).Order());
    }

    [Fact]
    public async Task ExtractPairAsync_OverTheExpandedCap_IsRefused()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", new byte[600]), ("song.mp3", new byte[600]));

        var thrown = await Assert.ThrowsAsync<KHostException>(
            () => KaraokeZip.ExtractPairAsync(zip, _output, maxExpandedBytes: 1000));

        Assert.Equal(KaraokeZip.TooLargeCode, thrown.ReferenceCode);
    }

    /// <summary>The cap is checked against the declared sizes, so a header that understates
    /// them must not let more than it declared reach the disk.</summary>
    [Fact]
    public async Task ExtractPairAsync_AHeaderUnderstatingTheSize_WritesNoMoreThanItDeclared()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", new byte[600]), ("song.mp3", new byte[600]));
        UnderstateSizes(zip, to: 10);

        try { await KaraokeZip.ExtractPairAsync(zip, _output, maxExpandedBytes: 1000); }
        catch (KHostException) { }

        Assert.All(Directory.EnumerateFiles(_output), file => Assert.True(new FileInfo(file).Length <= 10));
    }

    [Fact]
    public async Task ExtractPairAsync_JustUnderTheCap_IsTaken()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", new byte[500]), ("song.mp3", new byte[500]));

        await KaraokeZip.ExtractPairAsync(zip, _output, maxExpandedBytes: 1000);
    }

    [Fact]
    public void Validate_Encrypted_IsRefusedAsEncrypted()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", Graphics), ("song.mp3", Audio));
        MarkEncrypted(zip);

        Assert.Equal(KaraokeZip.EncryptedCode,
            Assert.Throws<KHostException>(() => KaraokeZip.Validate(zip)).ReferenceCode);
    }

    [Fact]
    public void Validate_NotAZipAtAll_IsRefusedAsCorrupt()
    {
        var zip = Path.Combine(_directory, "Song.zip");
        File.WriteAllBytes(zip, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Equal(KaraokeZip.CorruptCode,
            Assert.Throws<KHostException>(() => KaraokeZip.Validate(zip)).ReferenceCode);
    }

    [Fact]
    public async Task ExtractPairAsync_DamagedEntryData_IsRefusedAsCorruptAndLeavesNoPartial()
    {
        var zip = Write(_directory, "Song.zip", ("song.cdg", new byte[4096]), ("song.mp3", new byte[4096]));

        // Scribble over the first entry's deflated body, leaving the directory readable.
        var bytes = await File.ReadAllBytesAsync(zip);
        var nameLength = BitConverter.ToUInt16(bytes, 26);
        var extraLength = BitConverter.ToUInt16(bytes, 28);
        var body = 30 + nameLength + extraLength;
        for (var i = body; i < body + 8; i++) bytes[i] = 0xFF;
        await File.WriteAllBytesAsync(zip, bytes);

        var thrown = await Assert.ThrowsAsync<KHostException>(() => KaraokeZip.ExtractPairAsync(zip, _output));

        Assert.Equal(KaraokeZip.CorruptCode, thrown.ReferenceCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_output));
    }

    private static void UnderstateSizes(string path, uint to)
    {
        var bytes = File.ReadAllBytes(path);

        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            if (bytes[i] != 0x50 || bytes[i + 1] != 0x4b) continue;

            if (bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04) BitConverter.TryWriteBytes(bytes.AsSpan(i + 22), to);
            else if (bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02) BitConverter.TryWriteBytes(bytes.AsSpan(i + 24), to);
        }

        File.WriteAllBytes(path, bytes);
    }
}
