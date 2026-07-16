using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal static class EntityTypeBuilderExtensions
{
    public static void ConfigureEntityBase<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : EntityBase
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id)
            .ValueGeneratedNever();

        builder.Property(entity => entity.CreatedAt)
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.HasIndex(entity => entity.CreatedAt);
        builder.HasIndex(entity => entity.UpdatedAt);

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                $"CK_{typeof(TEntity).Name}_UpdatedAt",
                "\"UpdatedAt\" >= \"CreatedAt\"");
        });
    }
}
