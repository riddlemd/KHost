using KHost.Abstractions.Services.IPC;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.IPC.SignalR;

public static class ProjectExtensions
{
    public static IServiceCollection AddSignalRIPCServer(this IServiceCollection services)
    {
        services.AddSignalR()
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ScreenCommandJsonContext.Default));

        services.AddSingleton<ScreenServerService>();
        services.AddSingleton<IScreenTransport>(sp => sp.GetRequiredService<ScreenServerService>());
        services.AddSingleton<IHubCallback>(sp => sp.GetRequiredService<ScreenServerService>());

        return services;
    }

    public static IEndpointConventionBuilder MapIPCServer(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/ipc/screen")
        => endpoints.MapHub<ScreenHub>(pattern);

    public static IServiceCollection AddSignalRIPCClient(this IServiceCollection services)
    {
        services.AddSingleton<IScreenClient, ScreenClient>();
        return services;
    }

    public static IScreenClient CreateScreenClient() => new ScreenClient();

    public static IScreenClient CreateScreenClient(ILoggerFactory? loggerFactory) => new ScreenClient(loggerFactory);
}
