using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Services;

/// <summary>Reads venue settings the model no longer maps, still sitting in rows saved before they
/// were retired, so a startup step can carry them somewhere they now live.</summary>
public interface IRetiredVenueSettingsReader
{
    /// <summary>The break music mode this venue chose before App Settings picked one for every venue;
    /// null when it never chose one, no longer exists, or has been saved since the field was retired.</summary>
    Task<string?> ReadBreakMusicProviderAsync(Guid venueId);
}

internal sealed class RetiredVenueSettingsReader : IRetiredVenueSettingsReader
{
    private readonly IDbContextFactory<DefaultContext> _contextFactory;

    public RetiredVenueSettingsReader(IDbContextFactory<DefaultContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<string?> ReadBreakMusicProviderAsync(Guid venueId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // Raw SQL because the key is unmapped; EF drops it from the JSON the next time it saves the venue.
        var stored = await context.Database
            .SqlQuery<string?>($"""SELECT json_extract("Settings", '$.BreakMusicProvider') AS "Value" FROM "Venues" WHERE "Id" = {venueId}""")
            // Single, not First: the filter sits inside the raw SQL, so EF sees an unfiltered First and warns.
            .SingleOrDefaultAsync();

        return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
    }
}
