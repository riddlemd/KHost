using System.Text;
using System.Text.Json;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Visualisations;

/// <summary>The shipped presets by name, and imported ones kept as files under the host's
/// <c>cache/visualiser-presets</c>, beside the database rather than in the library.</summary>
public sealed class VisualiserPresetService : IVisualiserPresetService
{
    /// <summary>Where the screen fetches an imported preset, under the host's media surface.</summary>
    public const string RoutePrefix = "/media/visualiser-presets/";

    /// <summary>The largest file taken. The shipped presets run to a few kilobytes and the heaviest
    /// in butterchurn's pack to tens; anything past this is not a preset.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>The longest name kept, well inside an entry's column and any file system's limit.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The presets the screen ships in <c>screen-ui/visualiser-presets.js</c>, by the names
    /// it holds them under. Change one and change the other; a test holds them together.</summary>
    internal static readonly IReadOnlyList<string> BundledNames =
    [
        "_Aderrasi - Wanderer in Curved Space - mash0000 - faclempt kibitzing meshuggana schmaltz (Geiss color mix)",
        "_Mig_049",
        "Aderrasi - Potion of Spirits",
        "Aderrasi - Songflower (Moss Posy)",
        "Aderrasi - Storm of the Eye (Thunder) - mash0000 - quasi pseudo meta concentrics",
        "Cope - The Neverending Explosion of Red Liquid Fire",
        "flexi + fishbrain - neon mindblob grafitti",
        "Flexi, martin + geiss - dedicated to the sherwin maxawow",
        "Rovastar - Oozing Resistance",
        "Unchained & Rovastar - Wormhole Pillars (Hall of Shadows mix)",
    ];

    /// <summary>What a built-in's name starts with when it is a calm scene rather than an analyser;
    /// the page lists those under a heading of their own.</summary>
    public const string AmbientPrefix = "ambient-";

    /// <summary>The host's own drawings, by the names <c>screen-ui/eq-visualisers.js</c> holds them
    /// under, with what a host is shown. Change one and change the other; a test holds them together.</summary>
    internal static readonly IReadOnlyList<(string Name, string Title)> BuiltIns =
    [
        ("spectrum-bars", "Spectrum bars"),
        ("mirrored-bars", "Mirrored bars"),
        ("oscilloscope", "Oscilloscope"),
        ("vu-meters", "Twin VU meters"),
        ("ambient-gradient", "Drifting colour"),
        ("ambient-bokeh", "Floating lights"),
        ("ambient-embers", "Rising embers"),
        ("ambient-rings", "Pulse rings"),
        ("ambient-beams", "Sweeping beams"),
    ];

    /// <summary>Whether a built-in is one of the calm scenes.</summary>
    public static bool IsAmbient(VisualiserPresetSource source, string name)
        => source == VisualiserPresetSource.BuiltIn && name.StartsWith(AmbientPrefix, StringComparison.Ordinal);

    private static readonly string[] EquationFields =
        ["init_eqs_str", "frame_eqs_str", "pixel_eqs_str", "warp", "comp"];

    private readonly string _directory;
    private readonly IMessageBroker _broker;
    private readonly ILogger<VisualiserPresetService> _logger;

    // Serialises writes, so an import and a delete of one name cannot interleave.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public VisualiserPresetService(ILogger<VisualiserPresetService> logger, IMessageBroker broker, string? directory = null)
    {
        _logger = logger;
        _broker = broker;
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "cache", "visualiser-presets");
    }

    public IReadOnlyList<VisualiserPreset> ReadAll()
    {
        var presets = BuiltIns
            .Select(builtIn => new VisualiserPreset { Name = builtIn.Name, Title = builtIn.Title, Source = VisualiserPresetSource.BuiltIn })
            .Concat(BundledNames.Select(name => new VisualiserPreset { Name = name, Source = VisualiserPresetSource.Bundled }))
            .ToList();

        if (!Directory.Exists(_directory)) return presets;

        try
        {
            presets.AddRange(new DirectoryInfo(_directory).EnumerateFiles("*.json")
                .Select(file => new VisualiserPreset
                {
                    Name = Path.GetFileNameWithoutExtension(file.Name),
                    Source = VisualiserPresetSource.Imported,
                    ImportedUtc = file.LastWriteTimeUtc,
                })
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not list the imported visualiser presets in {Directory}", _directory);
        }

        return presets;
    }

    public async Task<VisualiserPresetImport> ImportAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (NameFrom(fileName) is not { } name)
            return Refused("That file has no usable name.");

        var bytes = await ReadAtMostAsync(content, MaxBytes + 1, cancellationToken);
        if (bytes.Length > MaxBytes)
            return Refused($"That file is over {MaxBytes / 1024} KB, far larger than any preset.");

        if (Validate(bytes) is { } problem)
            return Refused(problem);

        await _lock.WaitAsync(cancellationToken);
        bool replaced;
        try
        {
            Directory.CreateDirectory(_directory);

            var path = PathFor(name);
            replaced = File.Exists(path);

            // Written aside and moved in, so a screen fetching mid-import never reads half a file.
            var staging = path + ".importing";
            await File.WriteAllBytesAsync(staging, bytes, cancellationToken);
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }

        _logger.LogInformation("Imported visualiser preset {Name} ({Bytes} bytes){Replaced}",
            name, bytes.Length, replaced ? ", replacing the last" : "");
        _broker.Announce(new VisualiserPresetsChanged());

        return new VisualiserPresetImport
        {
            Preset = new VisualiserPreset
            {
                Name = name,
                Source = VisualiserPresetSource.Imported,
                ImportedUtc = File.GetLastWriteTimeUtc(PathFor(name)),
            },
            Replaced = replaced,
        };
    }

    public async Task<string?> ReadImportedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!IsStoredName(name)) return null;

        var path = PathFor(name);
        if (!File.Exists(path)) return null;

        try { return await File.ReadAllTextAsync(path, cancellationToken); }
        catch (IOException) { return null; }
    }

    public bool DeleteImported(string name)
    {
        if (!IsStoredName(name)) return false;

        _lock.Wait();
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return false;

            File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }

        _broker.Announce(new VisualiserPresetsChanged());
        return true;
    }

    /// <summary>Why these bytes are not a butterchurn preset, or null when they are one.</summary>
    /// <remarks>Shape only: the equations are code, run by whatever draws the preset, and whether
    /// they draw anything is found out by looking.</remarks>
    internal static string? Validate(byte[] bytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return "That is not a butterchurn preset (.json). A MilkDrop .milk file has to be converted first.";
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "That file is JSON, but not a butterchurn preset: it is not an object.";

            if (!root.TryGetProperty("baseVals", out var baseVals) || baseVals.ValueKind != JsonValueKind.Object)
                return "That file is JSON, but not a butterchurn preset: it has no baseVals.";

            foreach (var list in (string[])["shapes", "waves"])
            {
                if (root.TryGetProperty(list, out var value) && value.ValueKind != JsonValueKind.Array)
                    return $"That file is not a butterchurn preset: its {list} is not a list.";
            }

            foreach (var field in EquationFields)
            {
                if (root.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return $"That file is not a butterchurn preset: its {field} is not text.";
            }
        }

        return null;
    }

    /// <summary>The name a file is kept under: its own, less the extension, made safe to be a file
    /// name on any system. Null when nothing usable is left.</summary>
    internal static string? NameFrom(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Only the last segment: a browser may hand over a path, and a name is never one.
        var bare = fileName.Replace('\\', '/');
        bare = bare[(bare.LastIndexOf('/') + 1)..];
        if (bare.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) bare = bare[..^5];

        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new StringBuilder(bare.Length);
        foreach (var c in bare) safe.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);

        var name = safe.ToString().Trim().TrimEnd('.');
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();

        return name.Length == 0 || name.Trim('.').Length == 0 ? null : name;
    }

    /// <summary>Whether a name asked for is one this store could have written.</summary>
    /// <remarks>Checked before any path is built, so a name from a URL can never leave the folder.</remarks>
    private static bool IsStoredName(string? name) => name is not null && NameFrom(name + ".json") == name;

    private string PathFor(string name) => Path.Combine(_directory, name + ".json");

    private static VisualiserPresetImport Refused(string error) => new() { Error = error };

    private static async Task<byte[]> ReadAtMostAsync(Stream content, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while (buffer.Length < limit && (read = await content.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellationToken)) > 0)
            buffer.Write(chunk, 0, read);

        return buffer.ToArray();
    }
}
