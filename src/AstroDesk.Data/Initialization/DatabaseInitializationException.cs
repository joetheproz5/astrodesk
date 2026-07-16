namespace AstroDesk.Data.Initialization;

public sealed class DatabaseInitializationException : Exception
{
    public DatabaseInitializationException(
        string message,
        string databasePath,
        DatabaseFailureKind failureKind,
        string recoverySuggestion,
        Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoverySuggestion);

        DatabasePath = databasePath;
        FailureKind = failureKind;
        RecoverySuggestion = recoverySuggestion;
    }

    public string DatabasePath { get; }

    public DatabaseFailureKind FailureKind { get; }

    public string RecoverySuggestion { get; }

    public bool IsPotentiallyRecoverable =>
        FailureKind is not DatabaseFailureKind.Unknown;

    public bool CanRetryWithoutDataRecovery =>
        FailureKind is DatabaseFailureKind.Locked
            or DatabaseFailureKind.AccessDenied
            or DatabaseFailureKind.StorageFull
            or DatabaseFailureKind.CannotOpen
            or DatabaseFailureKind.InputOutput;
}
