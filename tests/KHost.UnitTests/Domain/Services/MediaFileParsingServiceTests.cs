using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class MediaFileParsingServiceTests
{
    private static MediaFileParsingService CreateService(MediaFileParsingService.ServiceOptions? opts = null)
    {
        var logger = Substitute.For<ILogger<MediaFileParsingService>>();
        var monitor = Substitute.For<IOptionsMonitor<MediaFileParsingService.ServiceOptions>>();
        monitor.CurrentValue.Returns(opts ?? new MediaFileParsingService.ServiceOptions());
        var analytics = Substitute.For<IAnalyticsService>();
        return new MediaFileParsingService(logger, monitor, analytics);
    }

    // Separator must match the host OS: on Unix a literal "C:\dir\x.mp4" is one long
    // filename, so the directory would survive into the parsed artist/title.
    // The file need not exist — TryProbeAsync treats a failed probe as "no metadata".
    private static string MediaPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "khost-parsing-tests", fileName);

    [Fact]
    public void ArtistFirst_DefaultFormat_SplitsCorrectly()
    {
        var svc = CreateService();

        var (title, artist) = svc.GetTitleAndArtistFromFilename(MediaPath("Bruno Mars - Finesse.mp4"));

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void TitleFirst_FormatOption_SwapsSides()
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            Format = FilenameFormat.TitleFirst
        });

        var (title, artist) = svc.GetTitleAndArtistFromFilename(MediaPath("Finesse - Bruno Mars.mp4"));

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void LabelPrefix_IsStrippedBeforeSplit()
    {
        var svc = CreateService();

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"[SBI-1234] Bruno Mars - Finesse.mp4");

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void TrackNumberPrefix_IsStrippedBeforeSplit()
    {
        var svc = CreateService();

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"01 - Bruno Mars - Finesse.mp4");

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void TitleParenArtist_FallbackPattern_IsAlwaysTitleFirst()
    {
        // "Title (Artist)" form is unambiguous and not affected by Format.
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            Format = FilenameFormat.ArtistFirst
        });

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"Finesse (Bruno Mars).mp4");

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void Underscore_FallbackPattern_IsAlwaysTitleFirst()
    {
        var svc = CreateService();

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"Finesse_Bruno Mars.mp4");

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }

    [Fact]
    public void NoSeparator_FallsBackToWholeNameAsTitle()
    {
        var svc = CreateService();

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"JustATitle.mp4");

        Assert.Equal("JustATitle", title);
        Assert.Null(artist);
    }

    // A still has no duration to probe for, and the host clock is what takes one down — without
    // a default it would arrive null and PlayAdAsync would refuse to show it at all.
    [Fact]
    public async Task LoadAndParseAsync_AnImage_GetsADefaultDuration()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("VenueCard.png"));

        Assert.Equal(MediaFormats.DefaultImageDuration, media.Duration);
        Assert.Equal("PNG", media.Format);
    }

    [Fact]
    public async Task LoadAndParseAsync_AVideo_DoesNotGetTheImageDefault()
    {
        var svc = CreateService();

        // The path does not exist, so ffprobe fails and duration stays unknown rather than
        // inheriting the still default.
        var media = await svc.LoadAndParseAsync(MediaPath("SomeSong.mp4"));

        Assert.Null(media.Duration);
    }

    [Fact]
    public async Task LoadAndParseAsync_NoArtist_UsesFallbackArtistName()
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            FallbackArtistName = "No Artist"
        });

        // Use a path that does not exist so FFprobe fails and we exercise the fallback branch.
        var media = await svc.LoadAndParseAsync(MediaPath("JustATitle.mp4"));

        Assert.Equal("JustATitle", media.Title);
        Assert.Equal("No Artist", media.Artist);
    }

    /// <summary>
    /// A card and an ad clip have no performer, so there is no artist for the fallback to stand in
    /// for. Inventing one put "Unknown Artist" beside every still in the pickers that name a row by
    /// title and artist.
    /// </summary>
    [Theory]
    [InlineData(MediaType.Image, "card-001.png")]
    [InlineData(MediaType.Video, "house-spot.mp4")]
    public async Task LoadAndParseAsync_SomethingWithNoPerformer_IsLeftWithoutAnArtist(MediaType type, string file)
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions { FallbackArtistName = "No Artist" });

        var media = await svc.LoadAndParseAsync(MediaPath(file), type);

        Assert.Equal(string.Empty, media.Artist);
    }

    [Theory]
    [InlineData(MediaType.Karaoke)]
    [InlineData(MediaType.Audio)]
    public async Task LoadAndParseAsync_SomethingWithAPerformer_StillFallsBack(MediaType type)
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions { FallbackArtistName = "No Artist" });

        var media = await svc.LoadAndParseAsync(MediaPath("JustATitle.mp4"), type);

        Assert.Equal("No Artist", media.Artist);
    }

    /// <summary>Taken at parse time now, rather than stamped on by the caller afterwards.</summary>
    [Fact]
    public async Task LoadAndParseAsync_CarriesTheTypeItWasGiven()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("card-001.png"), MediaType.Image);

        Assert.Equal(MediaType.Image, media.Type);
    }

    /// <summary>An artist the filename states is kept whatever the type — only the invented one goes.</summary>
    [Fact]
    public async Task LoadAndParseAsync_AVideoWhoseNameStatesAnArtist_KeepsIt()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("Toto - Africa.mp4"), MediaType.Video);

        Assert.Equal("Toto", media.Artist);
    }

    [Fact]
    public async Task LoadAndParseAsync_StripsKaraokeNoiseSuffix()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars - Finesse (Karaoke Version).cdg"));

        Assert.Equal("Finesse", media.Title);
        Assert.Equal("Bruno Mars", media.Artist);
        Assert.Equal("CDG", media.Format);
    }

    [Fact]
    public async Task LoadAndParseAsync_StripsMultipleStackedNoiseSuffixes()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars - Finesse (Karaoke) (HD).mp4"));

        Assert.Equal("Finesse", media.Title);
    }

    [Fact]
    public async Task LoadAndParseAsync_FeatInArtistField_AppendsToArtist()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars feat. Cardi B - Finesse.mp4"));

        // "feat. Cardi B" appears on the Artist side after the split, so it is preserved verbatim.
        Assert.Equal("Bruno Mars feat. Cardi B", media.Artist);
        Assert.Equal("Finesse", media.Title);
    }

    [Fact]
    public async Task LoadAndParseAsync_FeatInTitleField_AppendsToArtist()
    {
        var svc = CreateService();

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars - Finesse (feat. Cardi B).mp4"));

        Assert.Equal("Finesse", media.Title);
        Assert.Equal("Bruno Mars feat. Cardi B", media.Artist);
    }

    [Fact]
    public async Task LoadAndParseAsync_FeatHandlingMoveToNotes_PopulatesNotes()
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            FeaturingHandling = FeaturingHandling.MoveToNotes
        });

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars - Finesse (feat. Cardi B).mp4"));

        Assert.Equal("Finesse", media.Title);
        Assert.Equal("Bruno Mars", media.Artist);
        Assert.Equal("feat. Cardi B", media.Notes);
    }

    [Fact]
    public async Task LoadAndParseAsync_FeatHandlingIgnore_LeavesTitleAlone()
    {
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            FeaturingHandling = FeaturingHandling.Ignore
        });

        var media = await svc.LoadAndParseAsync(MediaPath("Bruno Mars - Finesse (feat. Cardi B).mp4"));

        Assert.Equal("Finesse (feat. Cardi B)", media.Title);
    }

    [Fact]
    public void ExtractProbeTags_ReturnsNulls_WhenTagsIsNull()
    {
        var (title, artist) = MediaFileParsingService.ExtractProbeTags(null);

        Assert.Null(title);
        Assert.Null(artist);
    }

    [Fact]
    public void ExtractProbeTags_ReturnsTitleAndArtist_WhenBothPresent()
    {
        var tags = new Dictionary<string, string> { { "title", "My Song" }, { "artist", "My Artist" } };

        var (title, artist) = MediaFileParsingService.ExtractProbeTags(tags);

        Assert.Equal("My Song", title);
        Assert.Equal("My Artist", artist);
    }

    [Fact]
    public void ExtractProbeTags_ReturnsNull_WhenTagValueIsWhitespace()
    {
        var tags = new Dictionary<string, string> { { "title", "   " }, { "artist", "" } };

        var (title, artist) = MediaFileParsingService.ExtractProbeTags(tags);

        Assert.Null(title);
        Assert.Null(artist);
    }

    [Fact]
    public void ExtractProbeTags_ReturnsNulls_WhenTagsIsEmpty()
    {
        var (title, artist) = MediaFileParsingService.ExtractProbeTags(new Dictionary<string, string>());

        Assert.Null(title);
        Assert.Null(artist);
    }

    [Fact]
    public void Constructor_InvalidPrefixRegex_DoesNotThrow_AndSkipsBadPattern()
    {
        // A bad regex in user config must not crash construction; the bad pattern is logged and skipped.
        var svc = CreateService(new MediaFileParsingService.ServiceOptions
        {
            PrefixStripPatterns = ["(unclosed", @"^\[.*?\]\s*"]
        });

        var (title, artist) = svc.GetTitleAndArtistFromFilename(@"[SBI-1234] Bruno Mars - Finesse.mp4");

        Assert.Equal("Finesse", title);
        Assert.Equal("Bruno Mars", artist);
    }
}
