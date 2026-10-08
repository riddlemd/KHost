using KHost.Abstractions.Models;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.Domain.Services.QrCodes;

public class QrCodePngExporterTests : IDisposable
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-qr-export-{Guid.NewGuid():N}");
    private readonly IQrCodeService _codes = Substitute.For<IQrCodeService>();
    private readonly QrCodePngExporter _exporter;

    public QrCodePngExporterTests()
    {
        _codes.ReadRegisteredAsync("example-owner").Returns(new QrCodeRegistration
        {
            OwnerId = "example-owner",
            Payload = "https://example.test/join",
        });

        _exporter = new QrCodePngExporter(_codes, _directory);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveAsync_ARegisteredCode_WritesAPngNamedAsAsked()
    {
        var path = await _exporter.SaveAsync("example-owner", "The Lounge QR code");

        Assert.Equal(Path.Combine(_directory, "The Lounge QR code.png"), path);
        Assert.Equal(PngSignature, File.ReadAllBytes(path!).Take(8));
    }

    /// <summary>A second save keeps the first: the host may have printed or designed with it.</summary>
    [Fact]
    public async Task SaveAsync_TheNameIsTaken_NumbersTheNewFileRatherThanOverwriting()
    {
        var first = await _exporter.SaveAsync("example-owner", "The Lounge QR code");
        var firstBytes = File.ReadAllBytes(first!);

        var second = await _exporter.SaveAsync("example-owner", "The Lounge QR code");

        Assert.Equal(Path.Combine(_directory, "The Lounge QR code (2).png"), second);
        Assert.Equal(firstBytes, File.ReadAllBytes(first!));
    }

    [Fact]
    public async Task SaveAsync_AnOwnerWithNoCode_WritesNothing()
    {
        var path = await _exporter.SaveAsync("nobody", "The Lounge QR code");

        Assert.Null(path);
        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any());
    }

    /// <summary>A venue name is free text; a slash in it must not reach outside the folder.</summary>
    [Fact]
    public async Task SaveAsync_ANameWithPathCharacters_StaysInTheFolder()
    {
        var path = await _exporter.SaveAsync("example-owner", "AC/DC Night: Main QR code");

        Assert.Equal(_directory, Path.GetDirectoryName(path));
        Assert.Equal("AC-DC Night- Main QR code.png", Path.GetFileName(path));
    }

    /// <summary>The code saved is the one registered now, so a scanner reads what the room would.</summary>
    [Fact]
    public async Task SaveAsync_EncodesTheRegisteredPayload()
    {
        var path = await _exporter.SaveAsync("example-owner", "The Lounge QR code");

        using var generator = new QRCoder.QRCodeGenerator();
        using var expected = generator.CreateQrCode("https://example.test/join", QRCoder.QRCodeGenerator.ECCLevel.M);
        Assert.Equal(new QRCoder.PngByteQRCode(expected).GetGraphic(20, drawQuietZones: true), File.ReadAllBytes(path!));
    }
}
