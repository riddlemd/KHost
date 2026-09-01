using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Services;

public class MediaResultColumnSetTests
{
    private static readonly MediaResultColumn Thumb =
        new() { Key = "thumbnail", Header = "", Kind = MediaResultColumnKind.Thumbnail, Essential = false };
    private static readonly MediaResultColumn Title =
        new() { Key = MediaResultColumn.TitleKey, Header = "Title" };
    private static readonly MediaResultColumn Publisher =
        new() { Key = "publisher", Header = "Published by", Essential = false };
    private static readonly MediaResultColumn Length =
        new() { Key = MediaResultColumn.DurationKey, Header = "Duration" };

    private static readonly IReadOnlyList<MediaResultColumn> YouTubeShape = [Thumb, Title, Publisher, Length];

    private static IMediaProvider Provider(string source, IReadOnlyList<MediaResultColumn> columns)
    {
        var provider = Substitute.For<IMediaProvider>();
        provider.SourceName.Returns(source);
        provider.Columns.Returns(columns);
        return provider;
    }

    private static MediaSearchEntity Result(
        string source = "YouTube",
        string title = "Africa",
        string artist = "Toto",
        TimeSpan? duration = null,
        Dictionary<string, string>? fields = null) => new()
        {
            Source = source,
            SourceDisplayName = source,
            ForeignKey = "abc123",
            Title = title,
            Artist = artist,
            Duration = duration,
            Fields = fields ?? [],
        };

    [Fact]
    public void For_TheProviderDeclaredColumns_UsesThem()
    {
        var columns = MediaResultColumnSet.For([Result()], [Provider("YouTube", YouTubeShape)]);

        Assert.Equal(["", "Title", "Published by", "Duration"], columns.Select(c => c.Header));
    }

    [Fact]
    public void For_TheProviderDeclaredNone_FallsBackToTheDefault()
    {
        var columns = MediaResultColumnSet.For([Result()], [Provider("YouTube", [])]);

        Assert.Equal(["Title", "Artist", "Duration"], columns.Select(c => c.Header));
    }

    [Fact]
    public void For_NoProviderIsRegisteredForTheSource_FallsBackToTheDefault()
    {
        var columns = MediaResultColumnSet.For([Result()], [Provider("Library", YouTubeShape)]);

        Assert.Equal(MediaResultColumnSet.Default, columns);
    }

    /// <summary>
    /// One table cannot carry two providers' headings, so a mixed set drops to the shape every
    /// provider fills in rather than showing YouTube's columns over library rows.
    /// </summary>
    [Fact]
    public void For_ResultsFromMoreThanOneSource_FallsBackToTheDefault()
    {
        var columns = MediaResultColumnSet.For(
            [Result(source: "YouTube"), Result(source: "Library")],
            [Provider("YouTube", YouTubeShape)]);

        Assert.Equal(MediaResultColumnSet.Default, columns);
    }

    [Fact]
    public void For_NothingFound_FallsBackToTheDefault()
        => Assert.Equal(MediaResultColumnSet.Default, MediaResultColumnSet.For([], [Provider("YouTube", YouTubeShape)]));

    [Fact]
    public void Value_TheReservedKeys_ComeOffTheEntityItself()
    {
        var result = Result(title: "Africa", artist: "Toto", duration: TimeSpan.FromSeconds(310));

        Assert.Equal("Africa", MediaResultColumnSet.Value(result, Title));
        Assert.Equal("Toto", MediaResultColumnSet.Value(result, new() { Key = MediaResultColumn.ArtistKey, Header = "Artist" }));
        Assert.Equal("05:10", MediaResultColumnSet.Value(result, Length));
    }

    [Fact]
    public void Value_NoDuration_ShowsThePlaceholder()
        => Assert.Equal("--:--", MediaResultColumnSet.Value(Result(duration: null), Length));

    [Fact]
    public void Value_AProvidersOwnKey_ComesFromItsFields()
    {
        var result = Result(fields: new() { ["publisher"] = "Sing King" });

        Assert.Equal("Sing King", MediaResultColumnSet.Value(result, Publisher));
    }

    [Fact]
    public void Value_AKeyTheResultDidNotFillIn_IsEmpty()
        => Assert.Equal(string.Empty, MediaResultColumnSet.Value(Result(), Publisher));

    /// <summary>
    /// The badge and the pin ride the column that names the row. A picture names nothing, so a
    /// provider leading with one does not lose the title to it.
    /// </summary>
    /// <summary>
    /// Value reads the reserved keys off the entity while the panel lays a column out by its kind.
    /// A provider naming the duration column without also declaring the kind — which is the
    /// obvious way to write it — had the two disagree, and the length was laid out as free text
    /// and given a share of the row meant for titles.
    /// </summary>
    [Fact]
    public void EffectiveKind_TheReservedDurationKey_IsADurationWhateverWasDeclared()
    {
        var declaredBare = new MediaResultColumn { Key = MediaResultColumn.DurationKey, Header = "Duration" };

        Assert.Equal(MediaResultColumnKind.Text, declaredBare.Kind);
        Assert.Equal(MediaResultColumnKind.Duration, declaredBare.EffectiveKind);
    }

    [Fact]
    public void EffectiveKind_AnyOtherColumn_KeepsWhatItDeclared()
    {
        Assert.Equal(MediaResultColumnKind.Thumbnail, Thumb.EffectiveKind);
        Assert.Equal(MediaResultColumnKind.Text, Publisher.EffectiveKind);
        Assert.Equal(MediaResultColumnKind.Text, Title.EffectiveKind);
    }

    [Fact]
    public void PrimaryIndex_TheFirstColumnIsAPicture_SkipsPastIt()
        => Assert.Equal(1, MediaResultColumnSet.PrimaryIndex(YouTubeShape));

    [Fact]
    public void PrimaryIndex_TheFirstColumnNamesTheRow_IsThatColumn()
        => Assert.Equal(0, MediaResultColumnSet.PrimaryIndex(MediaResultColumnSet.Default));

    [Fact]
    public void ShedOrder_TheColumnNamingTheRow_NeverSheds()
        => Assert.Equal(0, MediaResultColumnSet.ShedOrder(YouTubeShape, 1));

    [Fact]
    public void ShedOrder_AnEssentialColumn_NeverSheds()
        => Assert.Equal(0, MediaResultColumnSet.ShedOrder(YouTubeShape, 3));

    /// <summary>
    /// Counting droppable columns from the right must not sweep up an essential one standing
    /// between them. Only checking the last column hides this, because nothing is to its right.
    /// </summary>
    [Fact]
    public void ShedOrder_AnEssentialColumnWithADroppableOneToItsRight_StillNeverSheds()
    {
        IReadOnlyList<MediaResultColumn> shape = [Thumb, Title, Length, Publisher];

        Assert.Equal(0, MediaResultColumnSet.ShedOrder(shape, 2));
        Assert.Equal(1, MediaResultColumnSet.ShedOrder(shape, 3));
    }

    [Fact]
    public void ShedOrder_TheDroppableColumns_GoRightmostFirst()
    {
        // Publisher sits right of the thumbnail, so it is the first to go.
        Assert.Equal(1, MediaResultColumnSet.ShedOrder(YouTubeShape, 2));
        Assert.Equal(2, MediaResultColumnSet.ShedOrder(YouTubeShape, 0));
    }

    [Fact]
    public void ShedOrder_TheDefaultShape_ShedsOnlyTheArtist()
    {
        Assert.Equal(0, MediaResultColumnSet.ShedOrder(MediaResultColumnSet.Default, 0));
        Assert.Equal(1, MediaResultColumnSet.ShedOrder(MediaResultColumnSet.Default, 1));
        Assert.Equal(0, MediaResultColumnSet.ShedOrder(MediaResultColumnSet.Default, 2));
    }
}
