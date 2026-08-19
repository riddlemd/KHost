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
            serviceCollection.AddDbContextFactory<DefaultContext>(options =>
                options.UseSqlite($"Data Source={DatabaseLocation.FilePath}")
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

            serviceCollection.AddOptions<DatabaseInitializer.ServiceOptions>()
                .BindConfiguration(DatabaseInitializer.ServiceOptions.SectionName);

            serviceCollection.AddSingleton<IMediaRepository, MediaRepository>();
            serviceCollection.AddSingleton<IUsersRepository, UsersRepository>();
            serviceCollection.AddSingleton<IUserGroupsRepository, UserGroupsRepository>();
            serviceCollection.AddSingleton<IVenuesRepository, VenuesRepository>();
            serviceCollection.AddSingleton<IPerformancesRepository, PerformancesRepository>();
            serviceCollection.AddSingleton<ITipsRepository, TipsRepository>();
            serviceCollection.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

            return serviceCollection;
        }
    }
}
