namespace AstroDesk.Data.Initialization;

public enum DatabaseFailureKind
{
    Unknown = 0,
    Locked = 1,
    Corrupt = 2,
    AccessDenied = 3,
    StorageFull = 4,
    CannotOpen = 5,
    InputOutput = 6,
    Migration = 7,
}
