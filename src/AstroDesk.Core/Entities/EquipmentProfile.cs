namespace AstroDesk.Core.Entities;

public sealed class EquipmentProfile : EntityBase
{
    private readonly List<ShootingSession> _sessions = [];

    private EquipmentProfile()
    {
        Name = string.Empty;
        Camera = string.Empty;
        Lens = string.Empty;
    }

    public EquipmentProfile(
        string name,
        string camera,
        string lens,
        string? tripod = null,
        string? accessories = null,
        bool isDefault = false)
    {
        Name = RequiredText(name, nameof(name), 200);
        Camera = RequiredText(camera, nameof(camera), 200);
        Lens = RequiredText(lens, nameof(lens), 200);
        Tripod = OptionalText(tripod, nameof(tripod), 200);
        Accessories = OptionalText(accessories, nameof(accessories), 2000);
        IsDefault = isDefault;
    }

    public string Name { get; private set; }

    public string Camera { get; private set; }

    public string Lens { get; private set; }

    public string? Tripod { get; private set; }

    public string? Accessories { get; private set; }

    public bool IsDefault { get; private set; }

    public IReadOnlyCollection<ShootingSession> Sessions => _sessions.AsReadOnly();

    public void Update(
        string name,
        string camera,
        string lens,
        string? tripod,
        string? accessories,
        bool isDefault,
        DateTimeOffset? timestamp = null)
    {
        Name = RequiredText(name, nameof(name), 200);
        Camera = RequiredText(camera, nameof(camera), 200);
        Lens = RequiredText(lens, nameof(lens), 200);
        Tripod = OptionalText(tripod, nameof(tripod), 200);
        Accessories = OptionalText(accessories, nameof(accessories), 2000);
        IsDefault = isDefault;
        MarkUpdated(timestamp);
    }

    private static string RequiredText(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string? OptionalText(string? value, string paramName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : RequiredText(value, paramName, maxLength);
    }
}
