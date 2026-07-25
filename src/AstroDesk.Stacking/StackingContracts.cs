namespace AstroDesk.Stacking;

/// <summary>
/// How the integrated frames are combined once they have been registered.
/// </summary>
public enum StackMethod
{
    /// <summary>
    /// Average with sigma clipping. Rejects satellite trails, planes, and cosmic
    /// rays that appear in only a few frames. The sensible default for a set of
    /// more than about a dozen frames.
    /// </summary>
    AverageSigmaClip,

    /// <summary>
    /// Plain average. Best signal-to-noise when every frame is clean, but a
    /// single aircraft ruins the result.
    /// </summary>
    Average,

    /// <summary>
    /// Median. Very robust to outliers, slightly noisier than a clipped average.
    /// </summary>
    Median,
}

/// <summary>
/// A single captured file that is a candidate for stacking.
/// </summary>
/// <param name="Path">Full local path to the file pulled from the phone.</param>
/// <param name="CapturedAt">When the file appeared on the laptop.</param>
public sealed record StackFrame(string Path, DateTimeOffset CapturedAt)
{
    /// <summary>
    /// True when the frame carries linear sensor data rather than a rendered,
    /// denoised, 8-bit picture.
    /// </summary>
    public bool IsRaw =>
        System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".dng" or ".raw";
}

/// <summary>
/// Everything Siril needs to turn a folder of frames into one image.
/// </summary>
/// <param name="WorkingDirectory">
/// Folder holding the frames. Siril writes its intermediates here too, so it
/// should be per-session rather than shared.
/// </param>
/// <param name="FrameExtension">
/// Extension of the frames to convert, without the dot. Siril's <c>convert</c>
/// works on one extension at a time, so a mixed RAW+JPEG folder must pick one.
/// </param>
/// <param name="OutputName">Base name for the stacked result, without extension.</param>
/// <param name="Method">How to combine the registered frames.</param>
/// <param name="SigmaLow">Low rejection threshold, used only for sigma clipping.</param>
/// <param name="SigmaHigh">High rejection threshold, used only for sigma clipping.</param>
/// <param name="DarksDirectory">
/// Folder of dark frames to calibrate with, or null to stack uncalibrated.
/// </param>
/// <remarks>
/// A dark frame is an exposure of the same length, ISO and temperature with no
/// light reaching the sensor. Averaging a set of them gives a map of what the
/// sensor produces on its own - hot pixels and thermal glow - which subtracts
/// out of every light frame. Stacking reduces random noise but cannot touch
/// this, because it is the same in every frame and averaging preserves it
/// perfectly. On a phone sensor running warm it is often the largest remaining
/// artefact.
/// </remarks>
public sealed record StackRequest(
    string WorkingDirectory,
    string FrameExtension,
    string OutputName = "stacked",
    StackMethod Method = StackMethod.AverageSigmaClip,
    double SigmaLow = 3.0,
    double SigmaHigh = 3.0,
    string? DarksDirectory = null);

/// <summary>
/// Outcome of a stacking run.
/// </summary>
/// <param name="Succeeded">True when Siril produced an output image.</param>
/// <param name="FramesStacked">
/// Number of frames Siril actually integrated. Registration discards frames it
/// cannot solve, so this is routinely lower than the number captured and is the
/// honest figure to show the user.
/// </param>
/// <param name="Message">Human-readable summary, including the failure reason.</param>
/// <param name="Log">Full Siril output, kept for diagnosis.</param>
/// <param name="OutputPath">
/// The stacked master. This is linear FITS, which carries the real data but
/// which nothing on Windows can display.
/// </param>
/// <param name="PreviewPath">
/// A stretched TIFF rendition of the same stack, written so the result can
/// actually be looked at without opening Siril.
/// </param>
public sealed record StackResult(
    bool Succeeded,
    string? OutputPath,
    int FramesStacked,
    string Message,
    string Log,
    string? PreviewPath = null);

/// <summary>
/// Runs the external stacking engine.
/// </summary>
public interface IStackEngine
{
    /// <summary>
    /// True when the configured engine executable was found on disk.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Path to the engine executable. Set from settings, like the adb and scrcpy paths.
    /// </summary>
    string ExecutablePath { get; set; }

    Task<StackResult> StackAsync(
        StackRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
