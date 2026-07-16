using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Data.Initialization;

public sealed class DatabaseInitializer(
    AstroDeskDbContext dbContext,
    ILogger<DatabaseInitializer> logger)
    : IDatabaseInitializer
{
    private static readonly Action<ILogger, string, Exception?>
        DatabaseInitialized = LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1000, nameof(DatabaseInitialized)),
            "AstroDesk database initialized successfully at {DatabasePath}.");

    private static readonly Action<
            ILogger,
            DatabaseFailureKind,
            string,
            Exception?>
        DatabaseInitializationFailed =
            LoggerMessage.Define<DatabaseFailureKind, string>(
                LogLevel.Error,
                new EventId(1001, nameof(DatabaseInitializationFailed)),
                "AstroDesk database initialization failed ({FailureKind}) at {DatabasePath}.");

    private readonly AstroDeskDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ILogger<DatabaseInitializer> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        string databasePath = GetDatabasePath();

        try
        {
            EnsureDatabaseDirectory(databasePath);

            await _dbContext.Database
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await ConfigureConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await VerifyIntegrityAsync(cancellationToken)
                    .ConfigureAwait(false);
                await _dbContext.Database
                    .MigrateAsync(cancellationToken)
                    .ConfigureAwait(false);
                await VerifyIntegrityAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await _dbContext.Database
                    .CloseConnectionAsync()
                    .ConfigureAwait(false);
            }

            DatabaseInitialized(_logger, databasePath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DatabaseInitializationException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw CreateSqliteException(databasePath, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateException(
                databasePath,
                DatabaseFailureKind.AccessDenied,
                "AstroDesk does not have permission to use its database folder. " +
                "Choose a writable data folder or correct the folder permissions.",
                exception);
        }
        catch (IOException exception)
        {
            throw CreateException(
                databasePath,
                DatabaseFailureKind.InputOutput,
                "The database could not be read or written. Check free disk space, " +
                "the health of the drive, and whether security software is blocking the file, then retry.",
                exception);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            throw CreateException(
                databasePath,
                DatabaseFailureKind.Migration,
                "A database migration could not be applied. Keep the database and its " +
                "-wal and -shm files together, make a backup, and review the application log before retrying.",
                exception);
        }
    }

    private async Task ConfigureConnectionAsync(
        CancellationToken cancellationToken)
    {
        await _dbContext.Database
            .ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken)
            .ConfigureAwait(false);
        await _dbContext.Database
            .ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken)
            .ConfigureAwait(false);
        await _dbContext.Database
            .ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken)
            .ConfigureAwait(false);
        await _dbContext.Database
            .ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task VerifyIntegrityAsync(CancellationToken cancellationToken)
    {
        await using var command = _dbContext.Database
            .GetDbConnection()
            .CreateCommand();
        command.CommandText = "PRAGMA quick_check;";

        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(
                Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseInitializationException(
                "The AstroDesk database failed SQLite's integrity check.",
                GetDatabasePath(),
                DatabaseFailureKind.Corrupt,
                "Do not delete the database. Close AstroDesk, copy the database together " +
                "with any -wal and -shm files to a safe location, then restore a known-good " +
                "backup or rename the damaged files so AstroDesk can create a new database.",
                new InvalidDataException(
                    $"SQLite quick_check returned '{result ?? "<null>"}'."));
        }
    }

    private string GetDatabasePath()
    {
        string dataSource = _dbContext.Database.GetDbConnection().DataSource;

        return string.IsNullOrWhiteSpace(dataSource)
            ? "<unknown>"
            : Path.GetFullPath(dataSource);
    }

    private static void EnsureDatabaseDirectory(string databasePath)
    {
        if (databasePath == "<unknown>")
        {
            return;
        }

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private DatabaseInitializationException CreateSqliteException(
        string databasePath,
        SqliteException exception)
    {
        (DatabaseFailureKind kind, string recoverySuggestion) =
            exception.SqliteErrorCode switch
            {
                5 or 6 => (
                    DatabaseFailureKind.Locked,
                    "Another AstroDesk instance or process is using the database. " +
                    "Close the other process and retry."),
                8 => (
                    DatabaseFailureKind.AccessDenied,
                    "The database is read-only. Move it to a writable folder or correct its permissions."),
                10 => (
                    DatabaseFailureKind.InputOutput,
                    "SQLite reported a storage I/O error. Check the drive and free space, " +
                    "keep the database with its -wal and -shm files, and retry after making a backup."),
                11 or 26 => (
                    DatabaseFailureKind.Corrupt,
                    "Do not delete the database. Back up the database together with its " +
                    "-wal and -shm files, then restore a known-good copy or rename the damaged " +
                    "files so AstroDesk can create a new database."),
                13 => (
                    DatabaseFailureKind.StorageFull,
                    "The drive is full. Free space without deleting the AstroDesk database, then retry."),
                14 => (
                    DatabaseFailureKind.CannotOpen,
                    "SQLite could not open the database. Verify that the data folder exists, " +
                    "is writable, and is not blocked by security software."),
                _ => (
                    DatabaseFailureKind.Unknown,
                    "Keep the database and its -wal and -shm files together, make a backup, " +
                    "and review the AstroDesk log before retrying."),
            };

        return CreateException(
            databasePath,
            kind,
            recoverySuggestion,
            exception);
    }

    private DatabaseInitializationException CreateException(
        string databasePath,
        DatabaseFailureKind failureKind,
        string recoverySuggestion,
        Exception innerException)
    {
        DatabaseInitializationFailed(
            _logger,
            failureKind,
            databasePath,
            innerException);

        return new DatabaseInitializationException(
            $"AstroDesk could not initialize its local database ({failureKind}).",
            databasePath,
            failureKind,
            recoverySuggestion,
            innerException);
    }
}
