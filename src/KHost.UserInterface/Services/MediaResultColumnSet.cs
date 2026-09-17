using KHost.Common.Chronography;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Services;

/// <summary>Which columns a search result set shows, and what goes in each cell.</summary>
/// <remarks>Split out of the panel since picking the columns is the whole point.</remarks>
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

    /// <summary>The declaring provider's columns if every result is theirs, else the console's own.</summary>
    /// <remarks>One provider's headings must never sit over another's rows.</remarks>
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

    /// <summary>The cell's text; title, artist and duration come off the entity itself.</summary>
    /// <remarks>A provider need not copy what it already filled in.</remarks>
    public static string Value(MediaSearchEntity result, MediaResultColumn column) => column.Key switch
    {
        MediaResultColumn.TitleKey => result.Title,
        MediaResultColumn.ArtistKey => result.Artist,
        MediaResultColumn.DurationKey => result.Duration?.ToTotalMinutesAndSeconds() ?? NoDuration,
        _ => result.Fields.GetValueOrDefault(column.Key, string.Empty),
    };

    /// <summary>The first non-picture column; carries the "already queued" badge.</summary>
    /// <remarks>Never dropped, however narrow the panel gets.</remarks>
    public static int PrimaryIndex(IReadOnlyList<MediaResultColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].EffectiveKind != MediaResultColumnKind.Thumbnail)
                return i;
        }

        return 0;
    }

    /// <summary>How many droppable columns sit at or right of this one; 0 never sheds.</summary>
    /// <remarks>So the panel sheds the rightmost column first.</remarks>
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
