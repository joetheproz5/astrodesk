using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.ConfigureEntityBase();

        builder.Property(setting => setting.Key)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();

        builder.Property(setting => setting.Value)
            .IsRequired();

        builder.Property(setting => setting.ValueType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(setting => setting.Category)
            .HasMaxLength(100);

        builder.HasIndex(setting => setting.Key)
            .IsUnique();

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_AppSettings_Key_NotBlank",
                "length(trim(\"Key\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_AppSettings_ValueType_NotBlank",
                "length(trim(\"ValueType\")) > 0");
        });
    }
}
