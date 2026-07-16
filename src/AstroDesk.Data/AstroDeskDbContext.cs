using AstroDesk.Core.Entities;
using AstroDesk.Data.Converters;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Data;

public sealed class AstroDeskDbContext(DbContextOptions<AstroDeskDbContext> options)
    : DbContext(options)
{
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<SavedLocation> SavedLocations => Set<SavedLocation>();

    public DbSet<ShootingSession> ShootingSessions => Set<ShootingSession>();

    public DbSet<SessionWeatherSnapshot> SessionWeatherSnapshots =>
        Set<SessionWeatherSnapshot>();

    public DbSet<SessionAstronomySnapshot> SessionAstronomySnapshots =>
        Set<SessionAstronomySnapshot>();

    public DbSet<SessionScreenshot> SessionScreenshots => Set<SessionScreenshot>();

    public DbSet<SessionNote> SessionNotes => Set<SessionNote>();

    public DbSet<EquipmentProfile> EquipmentProfiles => Set<EquipmentProfile>();

    public DbSet<OverlayPreset> OverlayPresets => Set<OverlayPreset>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableUtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<TimeSpan>()
            .HaveConversion<TimeSpanToTicksConverter>();
        configurationBuilder.Properties<TimeSpan?>()
            .HaveConversion<NullableTimeSpanToTicksConverter>();
        configurationBuilder.Properties<decimal>()
            .HaveConversion<DecimalToDoubleConverter>();
        configurationBuilder.Properties<decimal?>()
            .HaveConversion<NullableDecimalToDoubleConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AstroDeskDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditValues();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditValues();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditValues()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                {
                    entry.Property(entity => entity.Id).CurrentValue = Guid.NewGuid();
                }

                if (entry.Entity.CreatedAt == default)
                {
                    entry.Property(entity => entity.CreatedAt).CurrentValue = now;
                }

                if (entry.Entity.UpdatedAt < entry.Entity.CreatedAt)
                {
                    entry.Property(entity => entity.UpdatedAt).CurrentValue =
                        entry.Entity.CreatedAt;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAt).IsModified = false;
                entry.Property(entity => entity.UpdatedAt).CurrentValue = now;
            }
        }
    }
}
