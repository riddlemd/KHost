using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Models.Plugins;

namespace KHost.UserInterface.Endpoints;

/// <summary>Serves a plugin's icon from outside wwwroot.</summary>
/// <remarks>Built from what the host knows, plugin and filename, never anything in the request.</remarks>
public static class PluginIconEndpoints
{
    public static IEndpointConventionBuilder MapPluginIcons(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/plugins/{pluginId}/icon.png", (
            string pluginId,
            IPluginsService plugins) =>
        {
            var plugin = plugins.Plugins.FirstOrDefault(p =>
                string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase));

            // HasIconImage is the discovery-time verdict: the plugin asked for an image and shipped
            // a PNG within the size cap. Anything else never had a URL worth answering.
            if (plugin is not { HasIconImage: true })
                return Results.NotFound();

            var path = Path.Combine(plugin.Directory, PluginIcon.FileName);

            return File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
        });
}
