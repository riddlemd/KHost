using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services.IPC;
using KHost.Screen2;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Screen2;

/// <summary>
/// What reaches the page. The host resolves every one of these from the venue, so this class is a
/// forwarder — but the page's CSS carries fallbacks numerically identical to the host's own
/// defaults, which is how the safe zone and the offset were dropped here for as long as they
/// existed and still looked right: a venue that had set neither rendered correctly, and a venue
/// that set either was ignored. Nothing on screen could tell the difference.
/// </summary>
public class StreamMediaPlayerQrCodeTests
{
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];

    public StreamMediaPlayerQrCodeTests() => _player.SendToBrowser = _sentToPage.Add;

    private JsonElement FirstCode()
    {
        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;

        Assert.Equal("qr-codes", message.GetProperty("type").GetString());

        return message.GetProperty("codes").EnumerateArray().First();
    }

    private void Send(ScreenQrCodePlacement placement)
        => _player.SetQrCodes(new SetScreenQrCodesCommand { Codes = [placement] });

    private static ScreenQrCodePlacement Placement(int safeZone = 3, double offset = 4.5) => new()
    {
        ImageUrl = "data:image/svg+xml;base64,AAAA",
        Modules = 29,
        Corner = ScreenCorner.TopLeft,
        Size = ScreenQrSize.Large,
        SafeZone = safeZone,
        Offset = offset,
    };

    /// <summary>The venue's margin, in the code's own modules.</summary>
    [Fact]
    public void SetQrCodes_CarriesTheSafeZone()
    {
        Send(Placement(safeZone: 3));

        Assert.Equal(3, FirstCode().GetProperty("safeZone").GetInt32());
    }

    /// <summary>The venue's inset from the edges it sits against.</summary>
    [Fact]
    public void SetQrCodes_CarriesTheOffset()
    {
        Send(Placement(offset: 4.5));

        Assert.Equal(4.5, FirstCode().GetProperty("offset").GetDouble(), 3);
    }

    /// <summary>
    /// The page knows CSS words, not these enums' spelling — an unrecognised one falls back to a
    /// default there, so a mismatch in case is a setting that silently stops working.
    /// </summary>
    [Fact]
    public void SetQrCodes_LowercasesTheCornerAndSizeForTheirCssNames()
    {
        Send(Placement());

        var code = FirstCode();

        Assert.Equal("topleft", code.GetProperty("corner").GetString());
        Assert.Equal("large", code.GetProperty("size").GetString());
    }

    /// <summary>The module count is the floor the venue's size cannot draw under.</summary>
    [Fact]
    public void SetQrCodes_CarriesTheModuleCount()
    {
        Send(Placement());

        Assert.Equal(29, FirstCode().GetProperty("modules").GetInt32());
    }
}
