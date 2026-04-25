using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KHost.DataAccess.Contexts;

internal class DefaultContextDesignTimeFactory : IDesignTimeDbContextFactory<DefaultContext>
{
    public DefaultContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DefaultContext>()
            .UseSqlite("Data Source=khost_design.db")
            .Options;

        return new DefaultContext(options);
    }
}
