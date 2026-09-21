using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

/// <inheritdoc cref="IBackgroundPackService" />
public sealed class BackgroundPackService : BaseService, IBackgroundPackService
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "Backgrounds";

        /// <summary>The host's own folder of backgrounds, or null for none.</summary>
        public string? Folder { get; set; }

        /// <summary>Where the set shipped with the app sits. Beside the binary unless overridden.
        /// </summary>
        /// <remarks>Configurable so a test can point it somewhere, not because a host would.
        /// </remarks>
        public string? BuiltInFolder { get; set; }
    }

    private readonly IOptionsMonitor<ServiceOptions> _options;

    public BackgroundPackService(ILogger<BackgroundPackService> logger, IOptionsMonitor<ServiceOptions> options)
        : base(logger) => _options = options;

    // Read live, so a host changing the folder does not have to restart.
    private ServiceOptions Options => _options.CurrentValue;

    private string BuiltInFolder => Options.BuiltInFolder is { Length: > 0 } configured
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "backgrounds");

    public Task<BackgroundPack> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Read());

    private BackgroundPack Read()
    {
        var shipped = Entries(BuiltInFolder);
        var host = Entries(Options.Folder);

        // The host's own folder shadows a shipped background of the same name, so replacing one we
        // ship is dropping a file in rather than an argument about precedence. Theirs first for the
        // same reason: what a host put there is what they are looking for.
        var byName = new Dictionary<string, BackgroundPackEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in host.Concat(shipped))
            byName.TryAdd(entry.File, entry);

        var entries = byName.Values
            .OrderBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BackgroundPack
        {
            Entries = entries,
            // Only the host's folder can be wrong: one shipped with the app that is missing is a
            // broken install, not something a host can act on from here.
            Problem = Options.Folder switch
            {
                null or "" => BackgroundPackProblem.NoFolderSet,
                var folder when !Directory.Exists(folder) => BackgroundPackProblem.FolderMissing,
                _ => BackgroundPackProblem.None,
            },
        };
    }

    private List<BackgroundPackEntry> Entries(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        string[] files;

        try
        {
            // Top level only: a pack is one flat folder, so a clip filed away in a subdirectory is
            // not quietly part of it.
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Could not read the background folder {Folder}", folder);
            return [];
        }

        var stills = files
            .Where(path => MediaFormats.ImageExtensions.Contains(Extension(path)))
            .ToLookup(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. files
                .Where(path => MediaFormats.VideoExtensions.Contains(Extension(path)))
                .Select(path => new BackgroundPackEntry
                {
                    File = Path.GetFileName(path),
                    Name = Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    StillPath = stills[Path.GetFileNameWithoutExtension(path)]
                        .OrderBy(still => still, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(),
                }),
        ];
    }

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();
}
