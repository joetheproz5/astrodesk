namespace AstroDesk.Core.Entities;

public abstract class EntityBase
{
    protected EntityBase()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    public void MarkUpdated(DateTimeOffset? timestamp = null)
    {
        var value = timestamp ?? DateTimeOffset.UtcNow;
        if (value < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                "The update timestamp cannot precede the previous update timestamp.");
        }

        UpdatedAt = value;
    }
}
