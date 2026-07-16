using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class SavedLocationConfiguration : IEntityTypeConfiguration<SavedLocation>
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<SavedLocation> builder)
    {
        builder.ToTable("SavedLocations");
        builder.ConfigureEntityBase();

        builder.Property(location => location.Name)
            .HasMaxLength(200)
            .UseCollation("NOCASE")
            .IsRequired();

        builder.Property(location => location.TimeZoneId)
            .HasMaxLength(200);

        builder.HasIndex(location => location.Name)
            .IsUnique();

        builder.HasIndex(location => location.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = 1");

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SavedLocations_Name_NotBlank",
                "length(trim(\"Name\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_SavedLocations_Latitude",
                "\"Latitude\" >= -90.0 AND \"Latitude\" <= 90.0");
            tableBuilder.HasCheckConstraint(
                "CK_SavedLocations_Longitude",
                "\"Longitude\" >= -180.0 AND \"Longitude\" <= 180.0");
        });

        builder.HasData(CreateSeedLocations());
    }

    private static object[] CreateSeedLocations()
    {
        return
        [
            CreateSeed(
                "1eb6fb77-e935-4f2f-b635-c18474befb25",
                "Beirut",
                33.8938,
                35.5018,
                0,
                false),
            CreateSeed(
                "ad8f2bee-f3fd-4f2c-ab40-416ff6df6738",
                "Zahle",
                33.8463,
                35.9020,
                900,
                false),
            CreateSeed(
                "3685a745-3e51-4824-bf11-ed51e6581b84",
                "Faraya",
                34.0111,
                35.8285,
                1_850,
                true),
            CreateSeed(
                "177ce03d-d347-4471-aa63-ad942906396d",
                "Tannourine",
                34.2090,
                35.9200,
                1_450,
                false),
            CreateSeed(
                "499b960b-71bb-4393-82bb-d71e68958a22",
                "Jezzine",
                33.5417,
                35.5844,
                950,
                false),
            CreateSeed(
                "da59de93-b9da-451a-a784-0508817005fc",
                "Bekaa Valley",
                33.8500,
                35.9000,
                900,
                false),
        ];
    }

    private static object CreateSeed(
        string id,
        string name,
        double latitude,
        double longitude,
        double elevationMeters,
        bool isDefault)
    {
        return new
        {
            Id = Guid.Parse(id),
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            ElevationMeters = (double?)elevationMeters,
            TimeZoneId = "Asia/Beirut",
            IsDefault = isDefault,
        };
    }
}
