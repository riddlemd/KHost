using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Turns a plugin zip into files on disk, or refuses it: one call, validation included.</summary>
public interface IPluginPayloadReader
{
    /// <summary>Every entry is checked before write, so a refused archive leaves nothing behind.</summary>
    PluginPayloadContents Unpack(string zipPath, string destination, Guid? expectedId = null);
}
