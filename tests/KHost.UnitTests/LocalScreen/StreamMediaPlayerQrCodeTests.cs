using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The page's CSS fallbacks match the host's defaults, so a dropped value looked right.</summary>
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
        Corner = OverlayCorner.TopLeft,
        Size = QrCodeSize.Large,
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

    /// <summary>The page knows CSS words, not these enums; a mismatch falls back silently.</summary>
    [Fact]
    public void SetQrCodes_LowercasesTheCornerAndSizeForTheirCssNames()
    {
        Send(Placement());

        var code = FirstCode();

        Assert.Equal("topleft", code.GetProperty("corner").GetString());
        Assert.Equal("large", code.GetProperty("size").GetString());
    }

    /// <summary>The card's corner has the codes' same trap: a CSS-word match, silent fallback.</summary>
    [Fact]
    public void SetBreakMusicCard_CarriesTheTrackAndItsCorner()
    {
        _player.SetBreakMusicCard(new SetBreakMusicCardCommand
        {
            Enabled = true,
            Title = "Free Fallin'",
            Artist = "Tom Petty",
            Corner = OverlayCorner.BottomLeft,
            Offset = 2.5,
        });

        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;

        Assert.Equal("break-music-card", message.GetProperty("type").GetString());
        Assert.True(message.GetProperty("enabled").GetBoolean());
        Assert.Equal("Free Fallin'", message.GetProperty("title").GetString());
        Assert.Equal("Tom Petty", message.GetProperty("artist").GetString());
        Assert.Equal("bottomleft", message.GetProperty("corner").GetString());
        Assert.Equal(2.5, message.GetProperty("offset").GetDouble(), 3);
    }

    /// <summary>Disabled is how the card comes down, so it has to reach the page as one.</summary>
    [Fact]
    public void SetBreakMusicCard_Disabled_ReachesThePageAsATakeDown()
    {
        _player.SetBreakMusicCard(new SetBreakMusicCardCommand { Enabled = false });

        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;

        Assert.Equal("break-music-card", message.GetProperty("type").GetString());
        Assert.False(message.GetProperty("enabled").GetBoolean());
    }

    /// <summary>The module count is the floor the venue's size cannot draw under.</summary>
    [Fact]
    public void SetQrCodes_CarriesTheModuleCount()
    {
        Send(Placement());

        Assert.Equal(29, FirstCode().GetProperty("modules").GetInt32());
    }
}
