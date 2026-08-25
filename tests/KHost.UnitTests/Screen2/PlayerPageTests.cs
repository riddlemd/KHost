using KHost.Screen2;

namespace KHost.UnitTests.Screen2;

public class PlayerPageTests
{
    [Fact]
    public void BuildPlayerPage_Always_InlinesThePlayerScript()
    {
        var page = Program.BuildPlayerPage();

        // A marker from player.js itself: the page must carry the source, not a reference to it.
        Assert.Contains("window.external.sendMessage", page);
        Assert.DoesNotContain("<script src=\"player.js\"></script>", page);
    }

    // Chromium has no native HLS, so a page that ships without the library plays nothing on
    // Windows — and the failure is a black screen, not a build error.
    [Fact]
    public void BuildPlayerPage_Always_InlinesHlsJs()
    {
        var page = Program.BuildPlayerPage();

        // An event name from the library itself, not one player.js uses: the page must carry
        // the source rather than a reference to it.
        Assert.Contains("MEDIA_ATTACHED", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=\"hls.light.min.js\"></script>", page);
    }

    // hls.js has to be defined before player.js reads it to choose a playback path.
    [Fact]
    public void BuildPlayerPage_Always_PutsHlsJsBeforeThePlayer()
    {
        var page = Program.BuildPlayerPage();

        var library = page.IndexOf("MEDIA_ATTACHED", StringComparison.Ordinal);
        var player = page.IndexOf("Hls.isSupported", StringComparison.Ordinal);

        Assert.True(library >= 0, "the library is missing from the page");
        Assert.True(player >= 0, "the player is missing from the page");
        Assert.True(player > library, "hls.js must be inlined before player.js reads Hls");
    }

    // canPlayType answers 'maybe' for mpegurl on both web views, so it can never pick a path —
    // and with the native fallback gone there is no second path for it to pick.
    [Fact]
    public void BuildPlayerPage_Always_BranchesOnHlsSupportRatherThanCanPlayType()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("if (!window.Hls || !Hls.isSupported())", page, StringComparison.Ordinal);
        Assert.DoesNotContain("canPlayType", page, StringComparison.Ordinal);
    }

    // Left to itself hls.js attaches through URL.createObjectURL, and the page is handed to the
    // web view as a raw string, so that URL is blob:null/… — which WebKit refuses to load into a
    // media element, silently, before hls.js has anything to report. The screen played nothing on
    // macOS for exactly this reason.
    [Fact]
    public void BuildPlayerPage_Always_AttachesTheMediaSourceItselfRatherThanLettingHlsMintAUrl()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("video.srcObject = mediaSource", page, StringComparison.Ordinal);
        Assert.Contains("hls.attachMedia({ media: video, mediaSource })", page, StringComparison.Ordinal);
    }

    // teardown clears src; an attached MediaSource outlives that, and the next song would then
    // append to the source the last one already ended.
    [Fact]
    public void BuildPlayerPage_Always_ClearsSrcObjectOnTeardown()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("video.srcObject = null", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlayerPage_Always_KeepsTheElementsThePlayerDrives()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("id=\"video\"", page);
        Assert.Contains("id=\"background\"", page);
        Assert.Contains("id=\"still\"", page);
        Assert.Contains("id=\"placeholder\"", page);
        Assert.Contains("id=\"blanked\"", page);
        Assert.Contains("id=\"hostlost\"", page);
    }

    // The bed is a second element rather than a second source on the video: it carries no
    // timeline, and sharing the element would put it under the same correction as the song.
    [Fact]
    public void BuildPlayerPage_Always_HandlesTheBackgroundChannelCommands()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("'bg-load'", page);
        Assert.Contains("'bg-play'", page);
        Assert.Contains("'bg-pause'", page);
        Assert.Contains("'bg-stop'", page);
        Assert.Contains("'bg-volume'", page);
        Assert.Contains("type: 'bg-ended'", page);
    }

    [Fact]
    public void BuildPlayerPage_Always_HandlesTheStillCommands()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("'show-image'", page);
        Assert.Contains("'hide-image'", page);
    }

    // A screen is rarely the same shape as the picture, so the host's choice has to reach the
    // element as an object-fit rather than the page assuming one.
    [Fact]
    public void BuildPlayerPage_Always_MapsEveryScalingModeToAnObjectFit()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("objectFit", page);
        Assert.Contains("fit: 'contain'", page);
        Assert.Contains("fill: 'cover'", page);
        Assert.Contains("stretch: 'fill'", page);
        Assert.Contains("original: 'none'", page);
    }
}
