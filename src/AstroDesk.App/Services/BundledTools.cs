using System.IO;

namespace AstroDesk.App.Services;

/// <summary>
/// Locates the external tools shipped alongside the application.
/// </summary>
/// <remarks>
/// adb, scrcpy, and siril-cli live in a <c>tools</c> folder rather than being
/// installed system-wide, so a machine needs no separate setup and nothing is
/// written outside the app directory. During development the executable runs
/// from <c>bin/Debug/.../win-x64</c>, several levels below the repository root
/// where <c>tools</c> actually sits, so the folder is found by walking upwards
/// rather than by assuming a fixed relative path.
/// </remarks>
public static class BundledTools
{
    private const string ToolsFolderName = "tools";
    private const int MaximumLevelsToWalkUp = 8;

    /// <summary>
    /// Full path to the bundled tools folder, or null when it is not present.
    /// </summary>
    public static string? ToolsRoot { get; } = FindToolsRoot();

    /// <summary>
    /// Directories worth searching for a bundled executable, most specific first.
    /// </summary>
    public static IReadOnlyList<string> SearchDirectories { get; } = BuildSearchDirectories();

    /// <summary>
    /// Resolves a bundled executable by relative path, for example
    /// <c>siril/bin/siril-cli.exe</c>. Returns null when it is not bundled.
    /// </summary>
    public static string? Find(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (ToolsRoot is null)
        {
            return null;
        }

        string candidate = Path.Combine(ToolsRoot, relativePath);
        return File.Exists(candidate) ? candidate : null;
    }

    public static string? FindAdb() => Find(Path.Combine("scrcpy", "adb.exe"));

    public static string? FindScrcpy() => Find(Path.Combine("scrcpy", "scrcpy.exe"));

    public static string? FindSirilCli() => Find(Path.Combine("siril", "bin", "siril-cli.exe"));

    private static string? FindToolsRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        for (int level = 0; level < MaximumLevelsToWalkUp && directory is not null; level++)
        {
            string candidate = Path.Combine(directory.FullName, ToolsFolderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildSearchDirectories()
    {
        if (ToolsRoot is null)
        {
            return [];
        }

        List<string> directories = [ToolsRoot];
        foreach (string subdirectory in new[] { "scrcpy", Path.Combine("siril", "bin") })
        {
            string path = Path.Combine(ToolsRoot, subdirectory);
            if (Directory.Exists(path))
            {
                directories.Add(path);
            }
        }

        return directories;
    }
}
