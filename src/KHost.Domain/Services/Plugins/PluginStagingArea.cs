using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// Holds no state of its own — staging is the folder on disk — so the copy <c>AddPlugins</c> builds
/// to apply pending work before the container exists and the singleton the installer resolves later
/// are interchangeable.
/// </summary>
/// <remarks>
/// Installs are keyed by plugin id and removals by folder name, because the two answer different
/// questions. An install replaces a plugin wherever it already sits, so it takes every folder
/// carrying that id. A removal is a host pointing at one row on the Plugins page — and the row it
/// pointed at is only identifiable by its folder once two of them share an id.
/// </remarks>
public class PluginStagingArea(string pluginsDirectory, string stagingDirectory) : IPluginStagingArea
{
    public void ApplyPending()
    {
        if (!Directory.Exists(stagingDirectory))
            return;

        var installed = MapInstalled();

        // Removals first: a stage that both removes and reinstalls an id is an update, and the
        // install has to be the one that survives.
        foreach (var marker in Directory.GetFiles(stagingDirectory, "*" + PluginPaths.RemovalSuffix))
        {
            try
            {
                DeleteInstalled(Path.GetFileNameWithoutExtension(marker));

                File.Delete(marker);
            }
            catch (Exception)
            {
                // Runs before there is a logger to tell. The marker stays and retries next start.
            }
        }

        foreach (var staged in Directory.GetDirectories(stagingDirectory))
        {
            var name = Path.GetFileName(staged);

            if (name.StartsWith('.') || !Guid.TryParse(name, out var id))
                continue;

            try
            {
                // Every copy, not just the first found: an id spread across two hand-named folders
                // would otherwise outlive the install meant to replace it, and the leftover shows
                // on the Plugins page as a duplicate the host never installed.
                foreach (var directory in installed.GetValueOrDefault(id, []))
                    DeleteInstalled(Path.GetFileName(directory));

                Directory.Move(staged, Path.Combine(pluginsDirectory, id.ToString()));
            }
            catch (Exception ex)
            {
                SetAside(staged, ex.Message);
            }
        }
    }

    public PluginStagingState Read()
    {
        if (!Directory.Exists(stagingDirectory))
            return PluginStagingState.Empty;

        var installs = new HashSet<Guid>();
        var removals = new HashSet<string>(StringComparer.Ordinal);
        var failures = new Dictionary<Guid, string>();

        foreach (var directory in Directory.GetDirectories(stagingDirectory))
        {
            var name = Path.GetFileName(directory);

            if (name.StartsWith('.'))
                continue;

            if (name.EndsWith(PluginPaths.FailureSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (Guid.TryParse(Path.GetFileNameWithoutExtension(name), out var failedId))
                    failures[failedId] = ReadFailure(directory);
            }
            else if (Guid.TryParse(name, out var id))
            {
                installs.Add(id);
            }
        }

        foreach (var marker in Directory.GetFiles(stagingDirectory, "*" + PluginPaths.RemovalSuffix))
        {
            var folder = Path.GetFileNameWithoutExtension(marker);

            if (!string.IsNullOrEmpty(folder))
                removals.Add(folder);
        }

        return new PluginStagingState { Installs = installs, Removals = removals, Failures = failures };
    }

    public string WorkPathFor(Guid pluginId)
        => Path.Combine(stagingDirectory, PluginPaths.WorkFolderName, pluginId.ToString());

    public void Stage(string payloadRoot, Guid pluginId)
    {
        var target = StagedPath(pluginId);

        Directory.CreateDirectory(stagingDirectory);
        TryDeleteDirectory(target);
        TryDeleteDirectory(target + PluginPaths.FailureSuffix);

        Directory.Move(payloadRoot, target);

        // A removal left pending for any copy of this id would undo what was just staged.
        ClearRemovalsFor(pluginId);
    }

    public void MarkForRemoval(string pluginFolderName)
    {
        if (InstalledPath(pluginFolderName) is not { } directory)
            return;

        Directory.CreateDirectory(stagingDirectory);

        // Removing a copy while an install of the same id is staged would put it straight back.
        if (IdOf(directory) is { } id)
            TryDeleteDirectory(StagedPath(id));

        File.WriteAllText(RemovalMarkerPath(pluginFolderName)!, string.Empty);
    }

    public void Clear(Guid pluginId)
    {
        TryDeleteDirectory(StagedPath(pluginId));
        TryDeleteDirectory(StagedPath(pluginId) + PluginPaths.FailureSuffix);

        ClearRemovalsFor(pluginId);
    }

    public void ClearRemoval(string pluginFolderName)
    {
        if (RemovalMarkerPath(pluginFolderName) is { } marker && File.Exists(marker))
            File.Delete(marker);
    }

    private string StagedPath(Guid pluginId) => Path.Combine(stagingDirectory, pluginId.ToString());

    private string? RemovalMarkerPath(string pluginFolderName)
        => InstalledPath(pluginFolderName) is null
            ? null
            : Path.Combine(stagingDirectory, pluginFolderName + PluginPaths.RemovalSuffix);

    /// <summary>
    /// Where a marker's folder name sits under <c>plugins/</c>, or null if it does not. A marker
    /// names a direct child and never a path: one that resolves anywhere else is a corrupt or
    /// hostile name, and following it would delete a directory outside the plugins folder.
    /// </summary>
    private string? InstalledPath(string pluginFolderName)
    {
        if (string.IsNullOrWhiteSpace(pluginFolderName))
            return null;

        var root = Path.GetFullPath(pluginsDirectory);

        string candidate;

        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, pluginFolderName));
        }
        catch (Exception)
        {
            return null;
        }

        return Path.GetDirectoryName(candidate) == root ? candidate : null;
    }

    /// <summary>The Available tab knows an id and not a folder, so undoing a removal from there
    /// undoes it for every copy the id is installed under.</summary>
    private void ClearRemovalsFor(Guid pluginId)
    {
        foreach (var directory in MapInstalled().GetValueOrDefault(pluginId, []))
            ClearRemoval(Path.GetFileName(directory));
    }

    // Folder names under plugins/ are the host's to choose — one dropped in by hand is named
    // whatever the host called it — so an id is only ever resolved through the manifest inside.
    // Two folders may well carry one id, which is a state the Plugins page reports and a host
    // recovers from by removing the copy it does not want.
    private Dictionary<Guid, List<string>> MapInstalled()
    {
        var map = new Dictionary<Guid, List<string>>();

        if (!Directory.Exists(pluginsDirectory))
        {
            Directory.CreateDirectory(pluginsDirectory);
            return map;
        }

        foreach (var directory in Directory.GetDirectories(pluginsDirectory))
        {
            if (IdOf(directory) is not { } id)
                continue;

            if (!map.TryGetValue(id, out var directories))
                map[id] = directories = [];

            directories.Add(directory);
        }

        return map;
    }

    private static Guid? IdOf(string pluginDirectory)
    {
        var manifestPath = Path.Combine(pluginDirectory, PluginLoader.ManifestFileName);

        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonSerializerOptions.Web)?.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DeleteInstalled(string pluginFolderName)
    {
        if (InstalledPath(pluginFolderName) is not { } directory || !Directory.Exists(directory))
            return;

        Directory.Delete(directory, recursive: true);
    }

    private static void SetAside(string stagedDirectory, string error)
    {
        try
        {
            var failedDirectory = stagedDirectory + PluginPaths.FailureSuffix;

            if (Directory.Exists(failedDirectory))
                Directory.Delete(failedDirectory, recursive: true);

            Directory.Move(stagedDirectory, failedDirectory);
            File.WriteAllText(Path.Combine(failedDirectory, PluginPaths.FailureFileName), error);
        }
        catch (Exception)
        {
            // A staging folder that cannot even be renamed is the host's to clear by hand.
        }
    }

    private static string ReadFailure(string failedDirectory)
    {
        try
        {
            var path = Path.Combine(failedDirectory, PluginPaths.FailureFileName);

            return File.Exists(path) ? File.ReadAllText(path).Trim() : "Install failed.";
        }
        catch (Exception)
        {
            return "Install failed.";
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
            // Superseded staging folders; a locked one is cleared on the next attempt.
        }
    }
}
