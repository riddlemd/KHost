namespace KHost.Plugins.Sdk.Models;

/// <summary>
/// How the console should render a column's values. The provider hands over the raw value and
/// the host formats it, so a plugin never has to guess a width, a locale, or a theme.
/// </summary>
public enum MediaResultColumnKind
{
    Text,

    /// <summary>Read from <see cref="MediaSearchEntity.Duration"/>, shown as m:ss.</summary>
    Duration,

    /// <summary>The value is an image URL. Empty leaves the cell blank rather than broken.</summary>
    Thumbnail,
}

/// <summary>
/// One column of a provider's search results. A provider that declares none gets the console's
/// default title/artist/length, which is what the local library wants and little else does.
/// </summary>
public sealed record MediaResultColumn
{
    /// <summary>Title, artist and duration read from the entity itself.</summary>
    public const string TitleKey = "title";
    public const string ArtistKey = "artist";
    public const string DurationKey = "duration";

    /// <summary>
    /// Which value fills the cell. The three keys above come from <see cref="MediaSearchEntity"/>'s
    /// own properties; anything else is looked up in <see cref="MediaSearchEntity.Fields"/>.
    /// </summary>
    public required string Key { get; init; }

    public required string Header { get; init; }

    public MediaResultColumnKind Kind { get; init; } = MediaResultColumnKind.Text;

    /// <summary>
    /// False lets the console drop this column when the panel is too narrow to carry every one,
    /// rightmost droppable first. The first column is never dropped — something has to name the
    /// row a host is choosing between.
    /// </summary>
    public bool Essential { get; init; } = true;
}
