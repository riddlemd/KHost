using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KHost.LrcLib;

public static class ProjectExtensions
{
    public static IServiceCollection AddLrcLib(this IServiceCollection serviceCollection) =>
        AddLrcLib(serviceCollection, null);

    public static IServiceCollection AddLrcLib(this IServiceCollection serviceCollection, Action<LrcLibOptions>? configure)
    {
        var optionsBuilder = serviceCollection.AddOptions<LrcLibOptions>()
            .BindConfiguration(LrcLibOptions.SectionName);

        if (configure is not null)
            optionsBuilder.Configure(configure);

        serviceCollection.AddHttpClient<ILrcLibClient, LrcLibClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<LrcLibOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.UserAgent))
                throw new Exception("UserAgent is is required, see README.md for details.");

            http.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });

        return serviceCollection;
    }
}
