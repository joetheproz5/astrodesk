using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;
using AstroDesk.Data.Initialization;
using AstroDesk.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AstroDesk.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAstroDeskData(
        this IServiceCollection services,
        Action<AstroDeskDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var dataOptions = new AstroDeskDataOptions();
        configure?.Invoke(dataOptions);
        dataOptions.Validate();

        services.AddSingleton(dataOptions);
        services.AddDbContext<AstroDeskDbContext>(
            (_, optionsBuilder) =>
            {
                optionsBuilder.UseSqlite(
                    SqliteConnectionFactory.CreateConnectionString(dataOptions),
                    sqliteOptions =>
                    {
                        sqliteOptions.CommandTimeout(
                            dataOptions.CommandTimeoutSeconds);
                        sqliteOptions.MigrationsAssembly(
                            typeof(AstroDeskDbContext).Assembly.FullName);
                    });

                optionsBuilder.EnableDetailedErrors(
                    dataOptions.EnableDetailedErrors);
                optionsBuilder.EnableSensitiveDataLogging(
                    dataOptions.EnableSensitiveDataLogging);
            });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<ShootingSessionRepository>();
        services.AddScoped<IShootingSessionRepository>(
            provider => provider.GetRequiredService<ShootingSessionRepository>());
        services.AddScoped<IRepository<ShootingSession>>(
            provider => provider.GetRequiredService<ShootingSessionRepository>());
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        return services;
    }
}
