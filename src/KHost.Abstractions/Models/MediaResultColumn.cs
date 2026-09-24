namespace KHost.Abstractions.Models;

/// <summary>How the console renders a column. The provider hands the raw value, host formats it.</summary>
public enum MediaResultColumnKind
{
    /// <summary>Plain text, sized to share the row equally with other Text columns.</summary>
    Text,

    /// <summary>Read from <see cref="MediaSearchEntity.Duration"/>, shown as m:ss.</summary>
    Duration,

    /// <summary>The value is an image URL. Empty leaves the cell blank rather than broken.</summary>
    Thumbnail,

    /// <summary>Short text sized to its own words, not an equal table share like Text columns.</summary>
    Label,
}

/// <summary>One search-result column; none declared gets the console's default title/artist/length.</summary>
public sealed record MediaResultColumn
{
    /// <summary>Reads <see cref="MediaSearchEntity.Title"/> rather than <see cref="MediaSearchEntity.Fields"/>.</summary>
    public const string TitleKey = "title";

    /// <summary>Reads <see cref="MediaSearchEntity.Artist"/> rather than <see cref="MediaSearchEntity.Fields"/>.</summary>
    public const string ArtistKey = "artist";

    /// <summary>Reads <see cref="MediaSearchEntity.Duration"/> rather than <see cref="MediaSearchEntity.Fields"/>.</summary>
    public const string DurationKey = "duration";

    /// <summary>Fields key for a row's picture: Thumbnail's target, and a spanning row's image.</summary>
    public const string ThumbnailKey = "thumbnail";

    /// <summary>Which value fills the cell: the three keys above, else a Fields lookup.</summary>
    public required string Key { get; init; }

    /// <summary>The column's title, shown at the head of the table.</summary>
    public required string Header { get; init; }

    /// <summary>How the column's value is drawn.</summary>
    public MediaResultColumnKind Kind { get; init; } = MediaResultColumnKind.Text;

    /// <summary>How this column lays out; the duration key always implies Duration regardless.</summary>
    public MediaResultColumnKind EffectiveKind =>
        Key == DurationKey ? MediaResultColumnKind.Duration : Kind;

    /// <summary>False drops this column when narrow; the first column is never dropped.</summary>
    public bool Essential { get; init; } = true;
}
