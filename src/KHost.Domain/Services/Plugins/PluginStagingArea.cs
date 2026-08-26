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
                if (Guid.TryParse(Path.GetFileNameWithoutExtension(marker), out var id))
                    DeleteInstalled(installed, id);

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
                DeleteInstalled(installed, id);
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
        var removals = new HashSet<Guid>();
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
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(marker), out var id))
                removals.Add(id);
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

        // A removal left pending for this id would undo what was just staged.
        var marker = RemovalMarkerPath(pluginId);

        if (File.Exists(marker))
            File.Delete(marker);
    }

    public void MarkForRemoval(Guid pluginId)
    {
        Directory.CreateDirectory(stagingDirectory);

        // A staged install for the same id would reinstate what is being removed.
        TryDeleteDirectory(StagedPath(pluginId));
        File.WriteAllText(RemovalMarkerPath(pluginId), string.Empty);
    }

    public void Clear(Guid pluginId)
    {
        TryDeleteDirectory(StagedPath(pluginId));
        TryDeleteDirectory(StagedPath(pluginId) + PluginPaths.FailureSuffix);

        var marker = RemovalMarkerPath(pluginId);

        if (File.Exists(marker))
            File.Delete(marker);
    }

    private string StagedPath(Guid pluginId) => Path.Combine(stagingDirectory, pluginId.ToString());

    private string RemovalMarkerPath(Guid pluginId)
        => Path.Combine(stagingDirectory, pluginId + PluginPaths.RemovalSuffix);

    // Folder names under plugins/ are the host's to choose — one dropped in by hand is named
    // whatever the host called it — so an id is only ever resolved through the manifest inside.
    private Dictionary<Guid, string> MapInstalled()
    {
        var map = new Dictionary<Guid, string>();

        if (!Directory.Exists(pluginsDirectory))
        {
            Directory.CreateDirectory(pluginsDirectory);
            return map;
        }

        foreach (var directory in Directory.GetDirectories(pluginsDirectory))
        {
            if (IdOf(directory) is { } id)
                map.TryAdd(id, directory);
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

    private static void DeleteInstalled(Dictionary<Guid, string> installed, Guid id)
    {
        if (!installed.TryGetValue(id, out var directory) || !Directory.Exists(directory))
            return;

        Directory.Delete(directory, recursive: true);
        installed.Remove(id);
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
