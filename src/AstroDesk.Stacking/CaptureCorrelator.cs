using System.IO;

namespace AstroDesk.Stacking;

/// <summary>
/// Why an imported file did or did not count as completing a shutter press.
/// </summary>
public enum CaptureMatch
{
    /// <summary>The file completes the outstanding shutter press.</summary>
    NewCapture,

    /// <summary>Another rendition of a capture already counted, such as the DNG beside its JPEG.</summary>
    DuplicateOfCountedCapture,

    /// <summary>Predates the shutter press, so it belongs to an earlier shot or an earlier session.</summary>
    PredatesShutter,

    /// <summary>Not a still image: video, thumbnail, sidecar, or a hidden or partially written file.</summary>
    NotACapture,

    /// <summary>No shutter press is outstanding, so nothing is waiting to be completed.</summary>
    NoPendingShutter,
}

/// <summary>
/// Decides which imported files complete which shutter presses.
/// </summary>
/// <remarks>
/// <para>
/// A shutter press does not map one-to-one onto a file. With RAW+JPEG enabled
/// Samsung writes two files for a single press — <c>20260718_132308.jpg</c> and
/// <c>20260718_132308.dng</c> — and they can land in different sync polls because
/// the DNG is much larger. Treating every imported file as "the next frame
/// arrived" therefore advances the run twice for one exposure, firing the next
/// shutter while the camera is still busy. That press is silently ignored, and
/// the run desynchronises permanently.
/// </para>
/// <para>
/// Correlation is by filename stem, which is what Samsung actually shares between
/// renditions of one capture, combined with a timestamp gate so files written
/// before the press cannot satisfy it.
/// </para>
/// </remarks>
public sealed class CaptureCorrelator
{
    /// <summary>
    /// Extensions that represent a still capture. Video and anything else in the
    /// camera folder is not a frame; the folder routinely holds hundreds of mp4s.
    /// </summary>
    private static readonly string[] CaptureExtensions =
        [".jpg", ".jpeg", ".png", ".dng", ".raw", ".heic", ".heif", ".tif", ".tiff"];

    /// <summary>
    /// Fragments that mark a file as not-a-capture even with a plausible
    /// extension: scanner sidecars, thumbnails, and half-written temporaries.
    /// </summary>
    private static readonly string[] ExcludedFragments =
        [".pending", ".tmp", ".temp", ".part", ".crdownload", "thumb", ".trashed"];

    private readonly HashSet<string> _countedStems =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    private DateTimeOffset? _shutterPressedAt;

    /// <summary>Stems counted so far in this run.</summary>
    public int CapturesCounted
    {
        get
        {
            lock (_sync)
            {
                return _countedStems.Count;
            }
        }
    }

    /// <summary>
    /// Records that a shutter command has been sent and a capture is now expected.
    /// </summary>
    /// <param name="pressedAt">
    /// Time the command was issued. Files older than this cannot have been
    /// produced by it.
    /// </param>
    public void ShutterPressed(DateTimeOffset pressedAt)
    {
        lock (_sync)
        {
            _shutterPressedAt = pressedAt;
        }
    }

    /// <summary>Clears all state for a fresh run.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _countedStems.Clear();
            _shutterPressedAt = null;
        }
    }

    /// <summary>
    /// Classifies an imported file, and counts it when it completes the
    /// outstanding shutter press.
    /// </summary>
    /// <param name="path">Local path of the imported file.</param>
    /// <param name="writtenAt">
    /// When the capture was produced. The file's own timestamp is preferable to
    /// the import time, since the sync poll can lag several seconds behind.
    /// </param>
    public CaptureMatch Classify(string path, DateTimeOffset writtenAt)
    {
        if (!IsCaptureFile(path))
        {
            return CaptureMatch.NotACapture;
        }

        string stem = Path.GetFileNameWithoutExtension(path);

        lock (_sync)
        {
            // A second rendition of a capture already counted: the DNG arriving
            // after its JPEG. Checked first, and deliberately before the pending
            // check, because counting a capture clears the outstanding press —
            // so by the time the DNG lands there is no press to match, and
            // reporting it as "nothing pending" would hide the RAW+JPEG pairing
            // that this whole mechanism exists to detect.
            if (_countedStems.Contains(stem))
            {
                return CaptureMatch.DuplicateOfCountedCapture;
            }

            if (_shutterPressedAt is not { } pressedAt)
            {
                return CaptureMatch.NoPendingShutter;
            }

            // Files from before the press belong to an earlier shot, or were
            // already sitting in the camera folder when the run started.
            // A small tolerance absorbs clock skew between phone and laptop.
            if (writtenAt + FileSystemTimestampTolerance < pressedAt)
            {
                return CaptureMatch.PredatesShutter;
            }

            _countedStems.Add(stem);
            _shutterPressedAt = null;
            return CaptureMatch.NewCapture;
        }
    }

    /// <summary>
    /// Slack allowed between a file's timestamp and the shutter press.
    /// </summary>
    /// <remarks>
    /// The phone's clock and the laptop's clock are not identical, and the
    /// timestamp may be taken when the exposure started rather than when the file
    /// was closed. Too tight a gate would reject genuine captures.
    /// </remarks>
    public static readonly TimeSpan FileSystemTimestampTolerance = TimeSpan.FromSeconds(90);

    public static bool IsCaptureFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        if (name.Length == 0 || name.StartsWith('.'))
        {
            return false;
        }

        foreach (string fragment in ExcludedFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return CaptureExtensions.Contains(
            Path.GetExtension(name).ToLowerInvariant());
    }
}
