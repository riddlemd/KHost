using KHost.Domain.Chronography;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;

namespace KHost.UserInterface.Services;

/// <summary>
/// Works out which columns a set of search results is shown in, and what goes in each cell. Split
/// out of the panel because picking the columns is the whole point of the feature and a component
/// that renders them is a poor place to prove it.
/// </summary>
public static class MediaResultColumnSet
{
    /// <summary>What a provider gets for declaring nothing: the shape the local library wants.</summary>
    public static readonly IReadOnlyList<MediaResultColumn> Default =
    [
        new() { Key = MediaResultColumn.TitleKey, Header = "Title" },
        new() { Key = MediaResultColumn.ArtistKey, Header = "Artist", Essential = false },
        new() { Key = MediaResultColumn.DurationKey, Header = "Duration" },
    ];

    private const string NoDuration = "--:--";

    /// <summary>
    /// The declaring provider's columns when every result came from it. Results from more than one
    /// source share one table, so they fall back to the default rather than showing one provider's
    /// headings over another's rows.
    /// </summary>
    public static IReadOnlyList<MediaResultColumn> For(
        IReadOnlyList<MediaSearchEntity> results, IReadOnlyList<IMediaProvider> providers)
    {
        if (results.Count == 0)
            return Default;

        var source = results[0].Source;

        if (results.Any(result => result.Source != source))
            return Default;

        var declared = providers
            .FirstOrDefault(provider => provider.SourceName == source)
            ?.Columns;

        return declared is { Count: > 0 } ? declared : Default;
    }

    /// <summary>
    /// The cell's text. Title, artist and duration come off the entity itself so a provider does
    /// not have to copy what it already filled in; everything else is its own.
    /// </summary>
    public static string Value(MediaSearchEntity result, MediaResultColumn column) => column.Key switch
    {
        MediaResultColumn.TitleKey => result.Title,
        MediaResultColumn.ArtistKey => result.Artist,
        MediaResultColumn.DurationKey => result.Duration?.ToTotalMinutesAndSeconds() ?? NoDuration,
        _ => result.Fields.GetValueOrDefault(column.Key, string.Empty),
    };

    /// <summary>
    /// The column that names the row: the first that is not a picture. It carries the "already
    /// queued" badge and is never dropped, however narrow the panel gets — a row a host cannot
    /// read is not one they can choose between.
    /// </summary>
    public static int PrimaryIndex(IReadOnlyList<MediaResultColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].EffectiveKind != MediaResultColumnKind.Thumbnail)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// How many droppable columns sit at or to the right of this one, so the panel sheds the
    /// rightmost first. 0 never sheds.
    /// </summary>
    public static int ShedOrder(IReadOnlyList<MediaResultColumn> columns, int index)
    {
        if (index == PrimaryIndex(columns) || columns[index].Essential)
            return 0;

        var order = 0;
        var primary = PrimaryIndex(columns);

        for (var i = columns.Count - 1; i >= index; i--)
        {
            if (i != primary && !columns[i].Essential)
                order++;
        }

        return order;
    }
}
