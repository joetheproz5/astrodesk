using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AstroDesk.Data;

public sealed class AstroDeskDbContextFactory
    : IDesignTimeDbContextFactory<AstroDeskDbContext>
{
    public AstroDeskDbContext CreateDbContext(string[] args)
    {
        string databasePath =
            Environment.GetEnvironmentVariable("ASTRODESK_DB_PATH")
            ?? Path.Combine(
                Path.GetTempPath(),
                "AstroDesk",
                "astrodesk.design.db");

        var dataOptions = new AstroDeskDataOptions
        {
            DatabasePath = databasePath,
        };

        var optionsBuilder = new DbContextOptionsBuilder<AstroDeskDbContext>();
        optionsBuilder.UseSqlite(
            SqliteConnectionFactory.CreateConnectionString(dataOptions),
            sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(dataOptions.CommandTimeoutSeconds);
                sqliteOptions.MigrationsAssembly(typeof(AstroDeskDbContext).Assembly.FullName);
            });

        return new AstroDeskDbContext(optionsBuilder.Options);
    }
}
