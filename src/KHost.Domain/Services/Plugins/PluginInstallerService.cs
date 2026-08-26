using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

public class PluginInstallerService : BaseService, IPluginInstallerService
{
    public const string HttpClientName = "PluginDownload";

    /// <summary>A plugin is a handful of assemblies. These bound a hostile or broken publisher —
    /// the compressed cap covers the download, the expanded one a zip that unpacks to fill the disk.</summary>
    private const long MaxPayloadBytes = 64L * 1024 * 1024;
    private const long MaxExpandedBytes = 256L * 1024 * 1024;
    private const int RecentCap = 20;

    private readonly ConcurrentDictionary<Guid, ActiveInstall> _active = new();
    private readonly List<PluginInstallInfo> _recent = [];
    private readonly object _recentLock = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginStagingArea _staging;
    private readonly IMessageBroker _broker;
    private readonly TimeProvider _timeProvider;

    public PluginInstallerService(
        ILogger<PluginInstallerService> logger,
        IHttpClientFactory httpClientFactory,
        IPluginStagingArea staging,
        TimeProvider timeProvider,
        IMessageBroker broker)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _staging = staging;
        _timeProvider = timeProvider;
        _broker = broker;
    }

    public IReadOnlyList<PluginInstallInfo> Snapshot()
    {
        List<PluginInstallInfo> recent;

        lock (_recentLock)
            recent = [.. _recent];

        return
        [
            .. _active.Values.Select(a => a.Info).OrderByDescending(i => i.StartedUtc),
            .. recent,
        ];
    }

    public PluginStagingState Staged() => _staging.Read();

    public async Task<PluginInstallInfo> InstallAsync(PluginCatalogEntry entry, PluginCatalogRelease release)
    {
        var info = new PluginInstallInfo
        {
            PluginId = entry.Id,
            Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id.ToString() : entry.Name,
            Version = release.Version,
            StartedUtc = _timeProvider.GetUtcNow().UtcDateTime,
            State = PluginInstallState.Downloading,
        };

        if (!release.IsInstallable)
            return SettleUnstarted(info, "The catalog offers this release without an https URL and a checksum.");

        var active = new ActiveInstall { Cts = new CancellationTokenSource(), Info = info };

        if (!_active.TryAdd(entry.Id, active))
        {
            active.Cts.Dispose();

            return _active.TryGetValue(entry.Id, out var running) ? running.Info : info;
        }

        Announce();

        var work = _staging.WorkPathFor(entry.Id);

        try
        {
            // Scratch a killed run left behind would collide with this extraction.
            TryDeleteDirectory(work);
            Directory.CreateDirectory(work);

            var zipPath = Path.Combine(work, "payload.zip");
            var hash = await DownloadAsync(release, zipPath, entry.Id, active.Cts.Token);

            if (!hash.Equals(release.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The download does not match the checksum the catalog published.");

            SetState(entry.Id, PluginInstallState.Verifying);

            var payload = Path.Combine(work, "payload");

            Extract(zipPath, payload);

            var root = FindManifestRoot(payload)
                ?? throw new InvalidOperationException($"The download contains no {PluginLoader.ManifestFileName}.");

            Validate(root, entry);

            _staging.Stage(root, entry.Id);

            Logger.LogInformation("Plugin '{Name}' {Version} staged; restart required", info.Name, release.Version);

            return Settle(entry.Id, PluginInstallState.Staged, null);
        }
        catch (OperationCanceledException)
        {
            return Settle(entry.Id, PluginInstallState.Cancelled, null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Installing plugin '{Name}' failed", info.Name);

            return Settle(entry.Id, PluginInstallState.Failed, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    public void Cancel(Guid pluginId)
    {
        // Only signalled here; the install task itself settles the row, so a cancel and a download
        // that finished a moment earlier cannot both write a terminal state.
        if (_active.TryGetValue(pluginId, out var active))
            active.Cts.Cancel();
    }

    public void CancelAll()
    {
        foreach (var active in _active.Values)
            active.Cts.Cancel();
    }

    public void MarkForRemoval(Guid pluginId)
    {
        _staging.MarkForRemoval(pluginId);

        Announce();
    }

    public void ClearStaged(Guid pluginId)
    {
        _staging.Clear(pluginId);

        Announce();
    }

    private async Task<string> DownloadAsync(PluginCatalogRelease release, string destination, Guid pluginId, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        if (total > MaxPayloadBytes)
            throw new InvalidOperationException("The download is larger than this host will accept.");

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;

            // Checked per chunk as well as against Content-Length, which a chunked response omits.
            if (written > MaxPayloadBytes)
                throw new InvalidOperationException("The download is larger than this host will accept.");

            hasher.AppendData(buffer, 0, read);

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            if (total is > 0)
                ReportProgress(pluginId, (double)written / total.Value);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Extract(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        long expanded = 0;

        // Every entry is checked before a byte is written: a zip that escapes its destination or
        // expands past the cap must not leave half its contents on disk.
        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;

            if (expanded > MaxExpandedBytes)
                throw new InvalidOperationException("The download expands to more than this host will accept.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            if (!Path.GetFullPath(Path.Combine(destination, entry.FullName)).StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"The download writes outside its folder ('{entry.FullName}').");
        }

        archive.ExtractToDirectory(destination);
    }

    /// <summary>A release zip commonly wraps its contents in one folder named for the tag.</summary>
    private static string? FindManifestRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, PluginLoader.ManifestFileName)))
            return extracted;

        var children = Directory.GetDirectories(extracted);

        return children.Length == 1 && File.Exists(Path.Combine(children[0], PluginLoader.ManifestFileName))
            ? children[0]
            : null;
    }

    private static void Validate(string root, PluginCatalogEntry entry)
    {
        var manifestPath = Path.Combine(root, PluginLoader.ManifestFileName);

        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{PluginLoader.ManifestFileName} is empty.");

        if (manifest.Id != entry.Id)
            throw new InvalidOperationException($"The download declares plugin id {manifest.Id}, but the catalog lists {entry.Id}.");

        if (manifest.ApiVersion != PluginApi.CurrentVersion)
            throw new InvalidOperationException($"Requires plugin API v{manifest.ApiVersion}; this host supports v{PluginApi.CurrentVersion}.");

        // The manifest came off the network and the loader hands EntryAssembly straight to
        // LoadFromAssemblyPath, so a traversing name would load an assembly from outside the folder.
        var entryPath = Path.GetFullPath(Path.Combine(root, manifest.EntryAssembly));

        if (!entryPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Entry assembly '{manifest.EntryAssembly}' points outside the plugin folder.");

        if (!File.Exists(entryPath))
            throw new InvalidOperationException($"Entry assembly '{manifest.EntryAssembly}' is missing from the download.");
    }

    private void ReportProgress(Guid pluginId, double fraction)
    {
        if (!_active.TryGetValue(pluginId, out var active))
            return;

        var clamped = Math.Clamp(fraction, 0d, 1d);
        var previousPercent = active.Info.Progress is { } previous ? (int)(previous * 100) : -1;
        var newPercent = (int)(clamped * 100);

        active.Info = active.Info with { Progress = clamped };

        // Announced only on an integer-percent change; a fast download reports per 80KB chunk.
        if (newPercent != previousPercent)
            Announce();
    }

    private void SetState(Guid pluginId, PluginInstallState state)
    {
        if (!_active.TryGetValue(pluginId, out var active))
            return;

        active.Info = active.Info with { State = state };

        Announce();
    }

    private PluginInstallInfo Settle(Guid pluginId, PluginInstallState state, string? error)
    {
        if (!_active.TryRemove(pluginId, out var active))
            return SettleUnstarted(FallbackInfo(pluginId), error);

        active.Cts.Dispose();

        var settled = active.Info with { State = state, Error = error };

        AddToRecent(settled);
        Announce();

        return settled;
    }

    private PluginInstallInfo SettleUnstarted(PluginInstallInfo info, string? error)
    {
        var settled = info with { State = PluginInstallState.Failed, Error = error };

        AddToRecent(settled);
        Announce();

        return settled;
    }

    private PluginInstallInfo FallbackInfo(Guid pluginId) => new()
    {
        PluginId = pluginId,
        Name = pluginId.ToString(),
        Version = string.Empty,
        StartedUtc = _timeProvider.GetUtcNow().UtcDateTime,
    };

    private void AddToRecent(PluginInstallInfo info)
    {
        lock (_recentLock)
        {
            _recent.RemoveAll(i => i.PluginId == info.PluginId);
            _recent.Insert(0, info);

            if (_recent.Count > RecentCap)
                _recent.RemoveRange(RecentCap, _recent.Count - RecentCap);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Scratch and superseded staging folders; a locked one is cleared on the next attempt.
        }
    }

    private void Announce() => _broker.Announce(new PluginInstallsChanged());

    private sealed class ActiveInstall
    {
        public required CancellationTokenSource Cts { get; init; }
        public required PluginInstallInfo Info { get; set; }
    }
}
