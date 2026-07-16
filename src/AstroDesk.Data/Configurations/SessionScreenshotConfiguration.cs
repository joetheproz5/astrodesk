using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class SessionScreenshotConfiguration
    : IEntityTypeConfiguration<SessionScreenshot>
{
    public void Configure(EntityTypeBuilder<SessionScreenshot> builder)
    {
        builder.ToTable("SessionScreenshots");
        builder.ConfigureEntityBase();

        builder.Property(screenshot => screenshot.FilePath)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(screenshot => screenshot.ImageFormat)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(screenshot => screenshot.ShootingSessionId);
        builder.HasIndex(screenshot => screenshot.CapturedAt);
        builder.HasIndex(
                screenshot => new
                {
                    screenshot.ShootingSessionId,
                    screenshot.FilePath,
                })
            .IsUnique();

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SessionScreenshots_FilePath_NotBlank",
                "length(trim(\"FilePath\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_SessionScreenshots_ImageFormat_NotBlank",
                "length(trim(\"ImageFormat\")) > 0");
        });
    }
}
