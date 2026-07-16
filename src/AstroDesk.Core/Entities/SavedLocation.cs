namespace AstroDesk.Core.Entities;

public sealed class SavedLocation : EntityBase
{
    private SavedLocation()
    {
        Name = string.Empty;
    }

    public SavedLocation(
        string name,
        double latitude,
        double longitude,
        double? elevationMeters = null,
        string? timeZoneId = null,
        bool isDefault = false)
    {
        SetDetails(name, latitude, longitude, elevationMeters, timeZoneId);
        IsDefault = isDefault;
    }

    public string Name { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double? ElevationMeters { get; private set; }

    public string? TimeZoneId { get; private set; }

    public bool IsDefault { get; private set; }

    public void Update(
        string name,
        double latitude,
        double longitude,
        double? elevationMeters = null,
        string? timeZoneId = null,
        DateTimeOffset? timestamp = null)
    {
        SetDetails(name, latitude, longitude, elevationMeters, timeZoneId);
        MarkUpdated(timestamp);
    }

    public void SetDefault(bool isDefault, DateTimeOffset? timestamp = null)
    {
        IsDefault = isDefault;
        MarkUpdated(timestamp);
    }

    private void SetDetails(
        string name,
        double latitude,
        double longitude,
        double? elevationMeters,
        string? timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmedName = name.Trim();
        if (trimmedName.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Location name cannot exceed 200 characters.");
        }

        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }

        if (elevationMeters is { } elevation && !double.IsFinite(elevation))
        {
            throw new ArgumentOutOfRangeException(nameof(elevationMeters), "Elevation must be finite.");
        }

        if (timeZoneId?.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(timeZoneId), "Time zone ID cannot exceed 200 characters.");
        }

        Name = trimmedName;
        Latitude = latitude;
        Longitude = longitude;
        ElevationMeters = elevationMeters;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
    }
}
