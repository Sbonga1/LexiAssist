using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LexiAssist.Web.Data;

/// <summary>Allows Azure SQL migrations to be generated and applied without starting the web app.</summary>
public sealed class AzureSqlApplicationDbContextFactory : IDesignTimeDbContextFactory<AzureSqlApplicationDbContext>
{
    public AzureSqlApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AzureSqlApplicationDbContext>()
            .UseSqlServer(
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ??
                "Server=(local);Database=LexiAssist;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_AzureSql"))
            .Options;

        return new AzureSqlApplicationDbContext(options);
    }
}
