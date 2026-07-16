using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class SessionWeatherSnapshotConfiguration
    : IEntityTypeConfiguration<SessionWeatherSnapshot>
{
    public void Configure(EntityTypeBuilder<SessionWeatherSnapshot> builder)
    {
        builder.ToTable("SessionWeatherSnapshots");
        builder.ConfigureEntityBase();

        builder.Property(snapshot => snapshot.DewRisk)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(snapshot => snapshot.ProviderName)
            .HasMaxLength(200);

        builder.HasIndex(snapshot => snapshot.ShootingSessionId)
            .IsUnique();
        builder.HasIndex(snapshot => snapshot.CapturedAt);

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SessionWeatherSnapshots_Humidity",
                "\"HumidityPercent\" IS NULL OR (\"HumidityPercent\" >= 0.0 AND \"HumidityPercent\" <= 100.0)");
            tableBuilder.HasCheckConstraint(
                "CK_SessionWeatherSnapshots_CloudCover",
                "\"CloudCoverPercent\" IS NULL OR (\"CloudCoverPercent\" >= 0.0 AND \"CloudCoverPercent\" <= 100.0)");
            tableBuilder.HasCheckConstraint(
                "CK_SessionWeatherSnapshots_WindSpeed",
                "\"WindSpeedKilometersPerHour\" IS NULL OR \"WindSpeedKilometersPerHour\" >= 0.0");
            tableBuilder.HasCheckConstraint(
                "CK_SessionWeatherSnapshots_Visibility",
                "\"VisibilityKilometers\" IS NULL OR \"VisibilityKilometers\" >= 0.0");
            tableBuilder.HasCheckConstraint(
                "CK_SessionWeatherSnapshots_DewRisk",
                "\"DewRisk\" IN ('Unavailable','Low','Moderate','High')");
        });
    }
}
