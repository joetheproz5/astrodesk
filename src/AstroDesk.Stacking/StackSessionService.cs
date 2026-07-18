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

public interface IStackSessionService
{
    event EventHandler<StackFrameAddedEventArgs>? FrameAdded;

    /// <summary>
    /// True while incoming captures are being collected for stacking.
    /// </summary>
    bool IsArmed { get; }

    /// <summary>
    /// How many frames the user asked for. Zero means keep collecting until stopped.
    /// </summary>
    int TargetFrameCount { get; }

    /// <summary>
    /// Folder holding this run's frames, or empty when not armed.
    /// </summary>
    string SessionFolder { get; }

    IReadOnlyList<StackFrame> Frames { get; }

    /// <summary>
    /// Starts a new collection run under <paramref name="rootFolder"/>.
    /// </summary>
    string Arm(string rootFolder, int targetFrameCount);

    void Disarm();

    /// <summary>
    /// Offers a newly captured file to the run. Ignored when not armed, when the
    /// target is already met, or when the file is not a stackable image.
    /// </summary>
    StackFrame? Offer(string capturedPath, DateTimeOffset capturedAt);

    /// <summary>
    /// Picks the extension to stack. Siril converts one extension at a time, and
    /// RAW carries far more recoverable signal than a rendered JPEG.
    /// </summary>
    string? ResolveFrameExtension();
}

/// <summary>
/// Collects captures into a per-run folder while stack mode is armed.
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

    public bool IsArmed { get; private set; }

    public int TargetFrameCount { get; private set; }

    public string SessionFolder { get; private set; } = string.Empty;

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
            string folder = Path.Combine(
                rootFolder,
                $"stack-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(folder);
            SessionFolder = folder;
            TargetFrameCount = targetFrameCount;
            IsArmed = true;
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
        }

        _logger.LogInformation("Stack mode disarmed.");
    }

    public StackFrame? Offer(string capturedPath, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(capturedPath) || !IsStackable(capturedPath))
        {
            return null;
        }

        StackFrame frame;
        int collected;
        int target;

        lock (_sync)
        {
            if (!IsArmed || SessionFolder.Length == 0)
            {
                return null;
            }

            if (TargetFrameCount > 0 && _frames.Count >= TargetFrameCount)
            {
                return null;
            }

            string destination = Path.Combine(SessionFolder, Path.GetFileName(capturedPath));
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
            _frames.Add(frame);
            collected = _frames.Count;
            target = TargetFrameCount;

            // Stop on our own once the requested number of frames has landed, so
            // a long unattended session does not keep copying after the goal.
            if (TargetFrameCount > 0 && collected >= TargetFrameCount)
            {
                IsArmed = false;
                _logger.LogInformation("Stack target of {Target} frames reached.", TargetFrameCount);
            }
        }

        FrameAdded?.Invoke(this, new StackFrameAddedEventArgs(frame, collected, target));
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
