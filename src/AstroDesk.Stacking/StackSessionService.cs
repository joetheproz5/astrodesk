using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

public sealed class StackFrameAddedEventArgs(StackFrame frame, int collected, int target)
    : EventArgs
{
    public StackFrame Frame { get; } = frame;

    public int Collected { get; } = collected;

    public int Target { get; } = target;
}

/// <summary>
/// Reports a dark copied into the current run without presenting it as a light frame.
/// </summary>
/// <param name="frame">The copied dark frame.</param>
/// <param name="collected">Total dark frames collected in this run.</param>
public sealed class StackDarkFrameAddedEventArgs(StackFrame frame, int collected)
    : EventArgs
{
    public StackFrame Frame { get; } = frame;

    public int Collected { get; } = collected;
}

public interface IStackSessionService
{
    event EventHandler<StackFrameAddedEventArgs>? FrameAdded;

    event EventHandler<StackDarkFrameAddedEventArgs>? DarkFrameAdded;

    /// <summary>
    /// True while incoming captures are being collected for stacking.
    /// </summary>
    bool IsArmed { get; }

    /// <summary>
    /// True while incoming captures are being routed into this run's darks folder.
    /// </summary>
    bool IsCollectingDarks { get; }

    /// <summary>
    /// How many frames the user asked for. Zero means keep collecting until stopped.
    /// </summary>
    int TargetFrameCount { get; }

    /// <summary>
    /// Number of dark frames collected separately from the light target.
    /// </summary>
    int DarkFrameCount { get; }

    /// <summary>
    /// Folder holding this run's frames, or empty when not armed.
    /// </summary>
    string SessionFolder { get; }

    /// <summary>
    /// Folder holding this run's dark frames, or empty before a run exists.
    /// </summary>
    string DarksFolder { get; }

    IReadOnlyList<StackFrame> Frames { get; }

    /// <summary>
    /// Starts a new collection run under <paramref name="rootFolder"/>.
    /// </summary>
    string Arm(string rootFolder, int targetFrameCount);

    void Disarm();

    /// <summary>
    /// Routes subsequent captures into the current run's darks folder and pauses
    /// light collection. A run must already exist.
    /// </summary>
    void StartDarkCollection();

    /// <summary>
    /// Stops routing captures into the darks folder.
    /// </summary>
    void StopDarkCollection();

    /// <summary>
    /// Offers a newly captured file to the active light or dark collection mode.
    /// Ignored when collection is off, when the light target is already met, or
    /// when the file is not a stackable image.
    /// </summary>
    StackFrame? Offer(string capturedPath, DateTimeOffset capturedAt);

    /// <summary>
    /// Picks the extension to stack. Siril converts one extension at a time, and
    /// RAW carries far more recoverable signal than a rendered JPEG.
    /// </summary>
    string? ResolveFrameExtension();
}

/// <summary>
/// Collects light captures into a per-run folder and dark captures into its
/// dedicated calibration subfolder.
/// </summary>
/// <remarks>
/// Frames are copied rather than moved: the originals stay in the user's normal
/// capture folder so that arming stack mode never puts their only copy of a shot
/// somewhere unexpected.
/// </remarks>
public sealed class StackSessionService(ILogger<StackSessionService>? logger = null)
    : IStackSessionService
{
    private static readonly string[] StackableExtensions =
        [".dng", ".raw", ".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    private readonly ILogger<StackSessionService> _logger =
        logger ?? NullLogger<StackSessionService>.Instance;

    private readonly List<StackFrame> _frames = [];
    private readonly object _sync = new();

    public event EventHandler<StackFrameAddedEventArgs>? FrameAdded;

    public event EventHandler<StackDarkFrameAddedEventArgs>? DarkFrameAdded;

    public bool IsArmed { get; private set; }

    public bool IsCollectingDarks { get; private set; }

    public int TargetFrameCount { get; private set; }

    public int DarkFrameCount { get; private set; }

    public string SessionFolder { get; private set; } = string.Empty;

    public string DarksFolder
    {
        get
        {
            lock (_sync)
            {
                return SessionFolder.Length == 0
                    ? string.Empty
                    : Path.Combine(SessionFolder, SirilScriptBuilder.DarksFolderName);
            }
        }
    }

    public IReadOnlyList<StackFrame> Frames
    {
        get
        {
            lock (_sync)
            {
                return _frames.ToArray();
            }
        }
    }

    public string Arm(string rootFolder, int targetFrameCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        ArgumentOutOfRangeException.ThrowIfNegative(targetFrameCount);

        lock (_sync)
        {
            _frames.Clear();
            DarkFrameCount = 0;
            string folder = Path.Combine(
                rootFolder,
                $"stack-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(folder);
            SessionFolder = folder;
            TargetFrameCount = targetFrameCount;
            IsArmed = true;
            IsCollectingDarks = false;
            _logger.LogInformation(
                "Stack mode armed for {Target} frames in {Folder}.",
                targetFrameCount,
                folder);
            return folder;
        }
    }

    public void Disarm()
    {
        lock (_sync)
        {
            IsArmed = false;
            IsCollectingDarks = false;
        }

        _logger.LogInformation("Stack mode disarmed.");
    }

    public void StartDarkCollection()
    {
        lock (_sync)
        {
            if (SessionFolder.Length == 0)
            {
                throw new InvalidOperationException(
                    "Start a stack run before collecting dark frames.");
            }

            Directory.CreateDirectory(
                Path.Combine(SessionFolder, SirilScriptBuilder.DarksFolderName));
            IsArmed = false;
            IsCollectingDarks = true;
        }

        _logger.LogInformation("Dark collection started in {Folder}.", DarksFolder);
    }

    public void StopDarkCollection()
    {
        lock (_sync)
        {
            IsCollectingDarks = false;
        }

        _logger.LogInformation("Dark collection stopped with {Count} frames.", DarkFrameCount);
    }

    public StackFrame? Offer(string capturedPath, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(capturedPath) || !IsStackable(capturedPath))
        {
            return null;
        }

        StackFrame frame;
        StackFrameAddedEventArgs? lightAdded = null;
        StackDarkFrameAddedEventArgs? darkAdded = null;

        lock (_sync)
        {
            if ((!IsArmed && !IsCollectingDarks) || SessionFolder.Length == 0)
            {
                return null;
            }

            bool dark = IsCollectingDarks;
            if (!dark && TargetFrameCount > 0 && _frames.Count >= TargetFrameCount)
            {
                return null;
            }

            string destinationFolder = dark
                ? Path.Combine(SessionFolder, SirilScriptBuilder.DarksFolderName)
                : SessionFolder;
            string destination = Path.Combine(destinationFolder, Path.GetFileName(capturedPath));
            try
            {
                File.Copy(capturedPath, destination, overwrite: true);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not copy {Path} into the stack folder.",
                    capturedPath);
                return null;
            }

            frame = new StackFrame(destination, capturedAt);
            if (dark)
            {
                DarkFrameCount++;
                darkAdded = new StackDarkFrameAddedEventArgs(frame, DarkFrameCount);
            }
            else
            {
                _frames.Add(frame);
                int collected = _frames.Count;
                lightAdded = new StackFrameAddedEventArgs(frame, collected, TargetFrameCount);

                // Stop on our own once the requested number of light frames has
                // landed. Darks have their own count and never consume this target.
                if (TargetFrameCount > 0 && collected >= TargetFrameCount)
                {
                    IsArmed = false;
                    _logger.LogInformation(
                        "Stack target of {Target} frames reached.",
                        TargetFrameCount);
                }
            }
        }

        if (darkAdded is not null)
        {
            DarkFrameAdded?.Invoke(this, darkAdded);
        }
        else if (lightAdded is not null)
        {
            FrameAdded?.Invoke(this, lightAdded);
        }

        return frame;
    }

    public string? ResolveFrameExtension()
    {
        StackFrame[] frames = Frames.ToArray();
        if (frames.Length == 0)
        {
            return null;
        }

        // Prefer RAW: it is linear, unsharpened and 12+ bits, so averaging it
        // actually recovers signal. A JPEG has already been denoised and
        // compressed, which destroys much of what stacking would otherwise find.
        string[] byPreference = [".dng", ".raw", ".tif", ".tiff", ".png", ".jpg", ".jpeg"];
        foreach (string candidate in byPreference)
        {
            StackFrame[] matching = [.. frames.Where(frame =>
                Path.GetExtension(frame.Path)
                    .Equals(candidate, StringComparison.OrdinalIgnoreCase))];

            if (matching.Length == 0)
            {
                continue;
            }

            // Preferring RAW is only right if the engine can read it. Samsung's
            // Expert RAW writes JPEG XL sensor data, which Siril's libraw
            // rejects: it converts none of them and the run stacks nothing.
            // Falling back to the JPEG companion loses depth but produces a
            // result, which is strictly better than a silent failure.
            if (candidate is ".dng" or ".raw" &&
                !matching.Any(frame => DngPreviewExtractor.IsRawDataReadable(frame.Path)))
            {
                _logger.LogInformation(
                    "Skipping {Extension}: the sensor data is in a format the stacker cannot read.",
                    candidate);
                continue;
            }

            return candidate.TrimStart('.');
        }

        return null;
    }

    private static bool IsStackable(string path) =>
        StackableExtensions.Contains(
            Path.GetExtension(path).ToLowerInvariant());
}
