using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class ShootingSessionConfiguration
    : IEntityTypeConfiguration<ShootingSession>
{
    public void Configure(EntityTypeBuilder<ShootingSession> builder)
    {
        builder.ToTable("ShootingSessions");
        builder.ConfigureEntityBase();

        builder.Property(session => session.TargetName)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();
        builder.Property(session => session.SessionType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(session => session.LocationName)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();
        builder.Property(session => session.Camera)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(session => session.SelectedPhoneLens)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(session => session.WhiteBalance)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(session => session.FocusSetting)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(session => session.Problems)
            .HasMaxLength(4000);
        builder.Property<int?>("CurrentSessionSlot")
            .HasComputedColumnSql(
                "CASE WHEN \"Status\" IN ('Active','Paused') THEN 1 ELSE NULL END",
                stored: true);

        builder.HasIndex(session => session.StartTime);
        builder.HasIndex(session => session.EndTime);
        builder.HasIndex(session => session.TargetName);
        builder.HasIndex(session => session.LocationName);
        builder.HasIndex(session => session.SessionType);
        builder.HasIndex(session => session.Rating);
        builder.HasIndex(session => new { session.Status, session.StartTime });
        builder.HasIndex("CurrentSessionSlot")
            .IsUnique()
            .HasFilter("\"CurrentSessionSlot\" IS NOT NULL");

        builder.HasOne(session => session.EquipmentProfile)
            .WithMany(profile => profile.Sessions)
            .HasForeignKey(session => session.EquipmentProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(session => session.WeatherSnapshot)
            .WithOne(snapshot => snapshot.ShootingSession)
            .HasForeignKey<SessionWeatherSnapshot>(
                snapshot => snapshot.ShootingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(session => session.AstronomySnapshot)
            .WithOne(snapshot => snapshot.ShootingSession)
            .HasForeignKey<SessionAstronomySnapshot>(
                snapshot => snapshot.ShootingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(session => session.Screenshots)
            .WithOne(screenshot => screenshot.ShootingSession)
            .HasForeignKey(screenshot => screenshot.ShootingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(session => session.Notes)
            .WithOne(note => note.ShootingSession)
            .HasForeignKey(note => note.ShootingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(session => session.Screenshots)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(session => session.Notes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(session => session.PlannedIntegrationTime);
        builder.Ignore(session => session.TotalIntegrationTime);
        builder.Ignore(session => session.RemainingFrames);
        builder.Ignore(session => session.ProgressPercentage);
        builder.Ignore(session => session.EstimatedRemainingCaptureTime);
        builder.Ignore(session => session.ActualSessionDuration);

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_TargetName_NotBlank",
                "length(trim(\"TargetName\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_LocationName_NotBlank",
                "length(trim(\"LocationName\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Latitude",
                "\"Latitude\" >= -90.0 AND \"Latitude\" <= 90.0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Longitude",
                "\"Longitude\" >= -180.0 AND \"Longitude\" <= 180.0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_ExposureTime",
                "\"ExposureTime\" > 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Delays",
                "\"DelayBetweenFrames\" >= 0 AND \"InitialDelay\" >= 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_FrameCounts",
                "\"FrameCount\" >= 0 AND \"PlannedFrameCount\" >= 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_TotalPausedDuration",
                "\"TotalPausedDuration\" >= 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Iso",
                "\"Iso\" IS NULL OR \"Iso\" > 0");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Rating",
                "\"Rating\" IS NULL OR (\"Rating\" >= 1 AND \"Rating\" <= 5)");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Battery",
                "(\"BatteryPercentageAtStart\" IS NULL OR (\"BatteryPercentageAtStart\" >= 0 AND \"BatteryPercentageAtStart\" <= 100)) " +
                "AND (\"BatteryPercentageAtEnd\" IS NULL OR (\"BatteryPercentageAtEnd\" >= 0 AND \"BatteryPercentageAtEnd\" <= 100))");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Storage",
                "(\"StorageBytesAtStart\" IS NULL OR \"StorageBytesAtStart\" >= 0) " +
                "AND (\"StorageBytesAtEnd\" IS NULL OR \"StorageBytesAtEnd\" >= 0)");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_SessionType",
                "\"SessionType\" IN ('MilkyWay','Starscape','StarTrails','Moon','Planet','Constellation','DeepSky','Timelapse','TestSession','Other')");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Status",
                "\"Status\" IN ('Planned','Active','Paused','Completed')");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_Timeline",
                "(\"StartTime\" IS NULL OR \"StartTime\" >= \"CreatedAt\") " +
                "AND (\"EndTime\" IS NULL OR (\"StartTime\" IS NOT NULL AND \"EndTime\" >= \"StartTime\")) " +
                "AND (\"PausedAt\" IS NULL OR (\"StartTime\" IS NOT NULL AND \"PausedAt\" >= \"StartTime\"))");
            tableBuilder.HasCheckConstraint(
                "CK_ShootingSessions_StatusTimeline",
                "(\"Status\" = 'Planned' AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NULL) OR " +
                "(\"Status\" = 'Active' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NULL) OR " +
                "(\"Status\" = 'Paused' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NOT NULL) OR " +
                "(\"Status\" = 'Completed' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"PausedAt\" IS NULL)");
        });
    }
}
