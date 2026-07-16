using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class EquipmentProfileConfiguration
    : IEntityTypeConfiguration<EquipmentProfile>
{
    public void Configure(EntityTypeBuilder<EquipmentProfile> builder)
    {
        builder.ToTable("EquipmentProfiles");
        builder.ConfigureEntityBase();

        builder.Property(profile => profile.Name)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();
        builder.Property(profile => profile.Camera)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(profile => profile.Lens)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(profile => profile.Tripod)
            .HasMaxLength(200);
        builder.Property(profile => profile.Accessories)
            .HasMaxLength(2000);

        builder.HasIndex(profile => profile.Name)
            .IsUnique();
        builder.HasIndex(profile => profile.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = 1");

        builder.Navigation(profile => profile.Sessions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_EquipmentProfiles_Name_NotBlank",
                "length(trim(\"Name\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_EquipmentProfiles_Camera_NotBlank",
                "length(trim(\"Camera\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_EquipmentProfiles_Lens_NotBlank",
                "length(trim(\"Lens\")) > 0");
        });
    }
}
