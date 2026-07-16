using AstroDesk.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstroDesk.Data.Configurations;

internal sealed class SessionNoteConfiguration
    : IEntityTypeConfiguration<SessionNote>
{
    public void Configure(EntityTypeBuilder<SessionNote> builder)
    {
        builder.ToTable("SessionNotes");
        builder.ConfigureEntityBase();

        builder.Property(note => note.Content)
            .IsRequired();
        builder.Property(note => note.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(note => note.ShootingSessionId);
        builder.HasIndex(note => note.NotedAt);
        builder.HasIndex(note => new { note.ShootingSessionId, note.NotedAt });

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SessionNotes_Content_NotBlank",
                "length(trim(\"Content\")) > 0");
            tableBuilder.HasCheckConstraint(
                "CK_SessionNotes_Kind",
                "\"Kind\" IN ('General','Observation','Problem')");
        });
    }
}
