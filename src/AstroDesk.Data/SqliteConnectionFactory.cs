using Microsoft.Data.Sqlite;

namespace AstroDesk.Data;

internal static class SqliteConnectionFactory
{
    public static string CreateConnectionString(AstroDeskDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        string fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(options.DatabasePath));

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = options.CommandTimeoutSeconds,
        };

        return builder.ToString();
    }
}
