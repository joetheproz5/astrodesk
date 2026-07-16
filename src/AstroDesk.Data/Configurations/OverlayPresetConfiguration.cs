using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class OverlayPresetConfiguration
    : IEntityTypeConfiguration<OverlayPreset>
{
    public void Configure(EntityTypeBuilder<OverlayPreset> builder)
    {
        builder.ToTable("OverlayPresets");
        builder.ConfigureEntityBase();

        builder.Property(preset => preset.Name)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();
        builder.Property(preset => preset.ColorHex)
            .HasMaxLength(9)
            .IsRequired();

        builder.HasIndex(preset => preset.Name)
            .IsUnique();
        builder.HasIndex(preset => preset.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = 1");

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_OverlayPresets_Name_NotBlank",
                "length(trim(\"Name\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_OverlayPresets_Opacity",
                "\"Opacity\" >= 0.0 AND \"Opacity\" <= 1.0");
            tableBuilder.HasCheckConstraint(
                "CK_OverlayPresets_LineThickness",
                "\"LineThickness\" > 0.0 AND \"LineThickness\" <= 20.0");
            tableBuilder.HasCheckConstraint(
                "CK_OverlayPresets_ColorHex",
                "(length(\"ColorHex\") = 7 AND \"ColorHex\" GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]') " +
                "OR (length(\"ColorHex\") = 9 AND \"ColorHex\" GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')");
        });
    }
}
