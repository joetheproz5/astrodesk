namespace AstroDesk.Infrastructure.Storage;

public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AstroDesk")
            : Path.GetFullPath(dataRoot);

        DatabasePath = Path.Combine(DataRoot, "astrodesk.db");
        SessionRoot = Path.Combine(DataRoot, "Sessions");
        LogRoot = Path.Combine(DataRoot, "Logs");
        ScreenshotRoot = Path.Combine(DataRoot, "Preview Screenshots");
    }

    public string DataRoot { get; }

    public string DatabasePath { get; }

    public string SessionRoot { get; private set; }

    public string LogRoot { get; }

    public string ScreenshotRoot { get; private set; }

    public void SetSessionRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        Directory.CreateDirectory(fullPath);
        SessionRoot = fullPath;
    }

    public void SetScreenshotRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        Directory.CreateDirectory(fullPath);
        ScreenshotRoot = fullPath;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(SessionRoot);
        Directory.CreateDirectory(LogRoot);
        Directory.CreateDirectory(ScreenshotRoot);
    }
}
