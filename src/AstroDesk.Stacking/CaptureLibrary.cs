namespace AstroDesk.Stacking;

/// <summary>
/// One night's worth of captures on disk.
/// </summary>
public sealed record CaptureRunSummary(
    string Name,
    string FolderPath,
    DateTimeOffset CapturedAt,
    int FrameCount,
    long TotalBytes,
    string? PreviewFramePath,
    string? StackResultPath)
{
    public bool HasResult => StackResultPath is not null;

    /// <summary>
    /// True for the catch-all entry holding captures that were never part of a
    /// stack run. Deleting that would take the loose originals with it.
    /// </summary>
    public bool IsLooseCaptures { get; init; }
}

/// <summary>
/// Finds what previous sessions have left on disk.
/// </summary>
/// <remarks>
/// Raw frames are around 28 MB each and a stacked result can be over a hundred,
/// so a few nights fill a laptop. Nothing in the app could see any of it, which
/// meant the only way to find out was Explorer and arithmetic.
/// </remarks>
public static class CaptureLibrary
{
    private static readonly string[] FrameExtensions =
        [".dng", ".raw", ".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    /// <summary>
    /// Extensions a finished integration is written to, biggest-first in the
    /// sense of which is worth showing as "the result".
    /// </summary>
    private static readonly string[] ResultNames = ["stacked_preview.tif", "stacked.fit"];

    public static IReadOnlyList<CaptureRunSummary> Scan(string captureRoot)
    {
        if (string.IsNullOrWhiteSpace(captureRoot) || !Directory.Exists(captureRoot))
        {
            return [];
        }

        List<CaptureRunSummary> runs = [];

        foreach (string folder in SafeDirectories(captureRoot))
        {
            CaptureRunSummary? run = Describe(folder, Path.GetFileName(folder), loose: false);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        // Captures that never became a run still take the same space, so they are
        // shown rather than quietly omitted - but as one entry, since listing a
        // hundred individual frames would bury the runs.
        CaptureRunSummary? loose = DescribeLoose(captureRoot);
        if (loose is not null)
        {
            runs.Add(loose);
        }

        return [.. runs.OrderByDescending(run => run.CapturedAt)];
    }

    public static long TotalBytes(IEnumerable<CaptureRunSummary> runs) =>
        runs?.Sum(run => run.TotalBytes) ?? 0;

    /// <summary>
    /// Formats a size the way someone deciding what to delete wants to read it.
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B",
    };

    private static CaptureRunSummary? Describe(string folder, string name, bool loose)
    {
        FileInfo[] files = SafeFiles(folder);
        FileInfo[] frames = [.. files.Where(IsFrame)];
        if (frames.Length == 0)
        {
            return null;
        }

        string? result = ResultNames
            .Select(candidate => Path.Combine(folder, candidate))
            .FirstOrDefault(File.Exists);

        return new CaptureRunSummary(
            name,
            folder,
            frames.Min(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
            frames.Length,
            files.Sum(file => file.Length),
            frames.OrderBy(file => file.Name).First().FullName,
            result)
        {
            IsLooseCaptures = loose,
        };
    }

    private static CaptureRunSummary? DescribeLoose(string captureRoot)
    {
        FileInfo[] frames = [.. SafeFiles(captureRoot).Where(IsFrame)];
        if (frames.Length == 0)
        {
            return null;
        }

        return new CaptureRunSummary(
            "Loose captures",
            captureRoot,
            frames.Min(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
            frames.Length,
            frames.Sum(file => file.Length),
            frames.OrderBy(file => file.Name).First().FullName,
            StackResultPath: null)
        {
            IsLooseCaptures = true,
        };
    }

    private static bool IsFrame(FileInfo file) =>
        FrameExtensions.Contains(file.Extension.ToLowerInvariant());

    /// <summary>
    /// A folder that vanished or is not readable must not take the whole listing
    /// down: this runs while captures are still arriving.
    /// </summary>
    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static FileInfo[] SafeFiles(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).GetFiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
