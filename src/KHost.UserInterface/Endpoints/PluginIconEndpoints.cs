using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Models.Plugins;

namespace KHost.UserInterface.Endpoints;

/// <summary>
/// Serves a plugin's own icon, which lives beside its manifest under plugins/ and so is outside
/// wwwroot. The id picks a plugin the host already discovered and the filename is fixed, so the
/// path is built from what the host knows rather than from anything in the request.
/// </summary>
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
