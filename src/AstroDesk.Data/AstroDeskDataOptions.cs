namespace AstroDesk.Data;

public sealed class AstroDeskDataOptions
{
    public string DatabasePath { get; set; } = GetDefaultDatabasePath();

    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableDetailedErrors { get; set; }

    public bool EnableSensitiveDataLogging { get; set; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabasePath);

        if (CommandTimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                CommandTimeoutSeconds,
                "The database command timeout must be between 1 and 300 seconds.");
        }
    }

    private static string GetDefaultDatabasePath()
    {
        string localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localData, "AstroDesk", "astrodesk.db");
    }
}
