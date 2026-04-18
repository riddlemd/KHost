using Microsoft.Extensions.DependencyInjection;

namespace KHost.DataAccess
{
    public static class ProjectExtensions
    {
        public static IServiceCollection AddDataAccess(this IServiceCollection serviceCollection)
        {


            return serviceCollection;
        }
    }
}
