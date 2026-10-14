using System.Text.Json;

namespace KHost.UnitTests;

/// <summary>A plugin's <c>.deps.json</c> in the shape the .NET SDK writes it, naming the
/// <c>KHost.Abstractions</c> version the plugin was compiled against.</summary>
public static class PluginDepsJson
{
    /// <summary>The document, or one naming no KHost.Abstractions when
    /// <paramref name="abstractionsVersion"/> is null.</summary>
    public static string For(string? abstractionsVersion, string pluginName = "Plugin")
    {
        var libraries = new Dictionary<string, object>
        {
            [$"{pluginName}/1.0.0"] = new { type = "project", serviceable = false, sha512 = "" },
            ["Microsoft.Extensions.Logging.Abstractions/10.0.7"] = new { type = "package", serviceable = true },
        };

        if (abstractionsVersion is not null)
        {
            libraries[$"KHost.Abstractions/{abstractionsVersion}"] = new { type = "package", serviceable = true };
            libraries[$"KHost.Common/{abstractionsVersion}"] = new { type = "package", serviceable = true };
        }

        return JsonSerializer.Serialize(new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
            compilationOptions = new { },
            targets = new Dictionary<string, object> { [".NETCoreApp,Version=v10.0"] = new { } },
            libraries,
        });
    }
}
