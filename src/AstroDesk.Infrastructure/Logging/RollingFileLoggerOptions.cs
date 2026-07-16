namespace AstroDesk.Infrastructure.Logging;

public sealed class RollingFileLoggerOptions
{
    public string DirectoryPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AstroDesk",
        "Logs");

    public string FilePrefix { get; set; } = "astrodesk";

    public int RetainedFileCount { get; set; } = 14;

    public long MaximumFileSizeBytes { get; set; } = 5 * 1024 * 1024;
}
