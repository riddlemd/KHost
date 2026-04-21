using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.DataAccess.Contexts;
using KHost.DataAccess.Repositories;
using KHost.DataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.DataAccess
{
    public static class ProjectExtensions
    {
        public static IServiceCollection AddDataAccess(this IServiceCollection serviceCollection)
        {
            var databaseFilePath = Path.Combine(AppContext.BaseDirectory, "cache", "khost.db");

            serviceCollection.AddDbContextFactory<DefaultContext>(options =>
                options.UseSqlite($"Data Source={databaseFilePath}")
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

            serviceCollection.AddSingleton<IMediaRepository, MediaRepository>();
            serviceCollection.AddSingleton<ISingersRepository, SingersRepository>();
            serviceCollection.AddSingleton<IVenuesRepository, VenuesRepository>();
            serviceCollection.AddSingleton<IPerformancesRepository, PerformancesRepository>();
            serviceCollection.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

            return serviceCollection;
        }
    }
}
