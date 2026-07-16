using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class SessionAstronomySnapshotConfiguration
    : IEntityTypeConfiguration<SessionAstronomySnapshot>
{
    public void Configure(EntityTypeBuilder<SessionAstronomySnapshot> builder)
    {
        builder.ToTable("SessionAstronomySnapshots");
        builder.ConfigureEntityBase();

        builder.Property(snapshot => snapshot.MoonPhase)
            .HasMaxLength(100);
        builder.Property(snapshot => snapshot.ProviderName)
            .HasMaxLength(200);

        builder.HasIndex(snapshot => snapshot.ShootingSessionId)
            .IsUnique();
        builder.HasIndex(snapshot => snapshot.CapturedAt);

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SessionAstronomySnapshots_MoonIllumination",
                "\"MoonIlluminationPercent\" IS NULL OR (\"MoonIlluminationPercent\" >= 0.0 AND \"MoonIlluminationPercent\" <= 100.0)");
            tableBuilder.HasCheckConstraint(
                "CK_SessionAstronomySnapshots_MoonAltitude",
                "\"MoonAltitudeDegrees\" IS NULL OR (\"MoonAltitudeDegrees\" >= -90.0 AND \"MoonAltitudeDegrees\" <= 90.0)");
            tableBuilder.HasCheckConstraint(
                "CK_SessionAstronomySnapshots_DarkSkyWindow",
                "\"DarkSkyWindowStart\" IS NULL OR \"DarkSkyWindowEnd\" IS NULL OR \"DarkSkyWindowEnd\" >= \"DarkSkyWindowStart\"");
        });
    }
}
