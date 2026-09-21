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
    // Windows. The failure is a black screen, not a build error.
    [Fact]
    public void BuildPlayerPage_Always_InlinesHlsJs()
    {
        var page = Program.BuildPlayerPage();

        // An event name from the library itself, not one player.js uses: the page must carry
        // the source rather than a reference to it.
        Assert.Contains("MEDIA_ATTACHED", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=\"hls.light.min.js\"></script>", page);
    }

    // The native path is a second renderer in the same page, so it ships the same way the rest
    // does. Missing, a kit load would find no engine and the screen would simply stay dark.
    [Fact]
    public void BuildPlayerPage_Always_InlinesTheKitEngine()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("function createKitEngine", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=\"kit-engine.js\"></script>", page, StringComparison.Ordinal);
    }

    // player.js calls createKitEngine when a native load arrives, so the engine has to be defined
    // by then — the same ordering rule hls.js is held to below.
    [Fact]
    public void BuildPlayerPage_Always_PutsTheKitEngineBeforeThePlayer()
    {
        var page = Program.BuildPlayerPage();

        var engine = page.IndexOf("function createKitEngine", StringComparison.Ordinal);
        var player = page.IndexOf("createKitEngine(kitCanvas", StringComparison.Ordinal);

        Assert.True(engine >= 0, "the engine is missing from the page");
        Assert.True(player >= 0, "the player never reaches for the engine");
        Assert.True(engine < player, "the player would call an engine that is not defined yet");
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

    // canPlayType answers 'maybe' for mpegurl on both web views, so it can never pick a path,
    // and with the native fallback gone there is no second path for it to pick.
    [Fact]
    public void BuildPlayerPage_Always_BranchesOnHlsSupportRatherThanCanPlayType()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("if (!window.Hls || !Hls.isSupported())", page, StringComparison.Ordinal);
        Assert.DoesNotContain("canPlayType", page, StringComparison.Ordinal);
    }

    // Left to itself hls.js attaches via URL.createObjectURL, but the page is a raw string in the
    // web view, so that URL is blob:null/…, which WebKit refuses silently; macOS played nothing.
    [Fact]
    public void BuildPlayerPage_Always_AttachesTheMediaSourceItselfRatherThanLettingHlsMintAUrl()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("el.srcObject = mediaSource", page, StringComparison.Ordinal);
        Assert.Contains("instance.attachMedia({ media: el, mediaSource })", page, StringComparison.Ordinal);
    }

    // Chromium's srcObject throws TypeError on a bare MediaSource, so the attach above cannot be
    // the only way in; unguarded it abandoned load() mid-call and the Windows screen went black.
    [Fact]
    public void BuildPlayerPage_Always_FallsBackWhenSrcObjectRefusesTheMediaSource()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("try {", page, StringComparison.Ordinal);
        Assert.Contains("instance.attachMedia(el);", page, StringComparison.Ordinal);
    }

    // teardown clears src; an attached MediaSource outlives that, and the next song would then
    // append to the source the last one already ended.
    [Fact]
    public void BuildPlayerPage_Always_ClearsSrcObjectOnTeardown()
    {
        var page = Program.BuildPlayerPage();

        Assert.Contains("el.srcObject = null", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlayerPage_Always_KeepsTheElementsThePlayerDrives()
    {
        var page = Program.BuildPlayerPage();

        // Two players, stacked: a rebuilt stream is brought up behind the one still sounding and
        // swapped for it, so the room never hears the join.
        Assert.Contains("id=\"video-b\"", page);
        Assert.Contains("function handOver(next)", page, StringComparison.Ordinal);

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

    // A stop that leaves a pending handover running lets the swap land mid-fade: the replacement
    // arrives at full volume unramped, so the room hears no fade and the song cuts off in one step.
    [Fact]
    public void BuildPlayerPage_Always_DropsAPendingHandoverBeforeFadingOut()
    {
        var page = Program.BuildPlayerPage();

        var fade = page[page.IndexOf("async function fadeOutAndStop", StringComparison.Ordinal)..];

        Assert.StartsWith(
            "async function fadeOutAndStop(fadeMs) {\n    const generation = playbackGeneration;\n\n    //",
            fade.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);

        Assert.Contains("cancelHandover();", fade[..600], StringComparison.Ordinal);
    }

    // Checked only after the ramp, a fade the host has already superseded goes on pulling the
    // volume down over the song that replaced it, which arrives and then quietly disappears.
    [Fact]
    public void BuildPlayerPage_Always_AbandonsAFadeTheHostHasSuperseded()
    {
        var page = Program.BuildPlayerPage();

        var ramp = page[page.IndexOf("async function fadeOutAndStop", StringComparison.Ordinal)..];
        ramp = ramp[..ramp.IndexOf("teardown();", StringComparison.Ordinal)];

        // Inside the tick, not merely after the await it is driving.
        Assert.Contains("if (generation !== playbackGeneration) return resolve(false);", ramp, StringComparison.Ordinal);

        // And the level goes back, or the element the next song is already using stays silent.
        Assert.Contains("element.volume = currentVolume;", ramp, StringComparison.Ordinal);
    }
}
