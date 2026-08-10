using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KHost.DataAccess.Contexts;

internal class DefaultContextDesignTimeFactory : IDesignTimeDbContextFactory<DefaultContext>
{
    public DefaultContext CreateDbContext(string[] args)
    {
        var dbPath = Path.Combine(
            Path.GetDirectoryName(typeof(DefaultContextDesignTimeFactory).Assembly.Location)!,
            "khost_design.db"
        );

        var options = new DbContextOptionsBuilder<DefaultContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new DefaultContext(options);
    }
}
