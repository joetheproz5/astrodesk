using System.Globalization;
using System.Text;

namespace AstroDesk.Stacking;

/// <summary>
/// Builds the Siril script (<c>.ssf</c>) that converts, registers, and stacks a
/// session's frames.
/// </summary>
/// <remarks>
/// Registration is the entire point of running Siril rather than averaging the
/// frames ourselves. On a fixed tripod the sky rotates about 15 degrees per hour,
/// so successive frames do not line up: over a 40-minute session a star crosses
/// several hundred pixels and the field also rotates about the pole. Averaging
/// unregistered frames therefore produces trails, not a cleaner image.
/// <c>register</c> solves each frame against a reference star field and resamples
/// it onto a common grid, which is what makes integration meaningful.
/// </remarks>
public static class SirilScriptBuilder
{
    /// <summary>
    /// Base name handed to Siril's <c>convert</c>.
    /// </summary>
    public const string ConvertName = "astrodesk";

    /// <summary>
    /// The sequence Siril actually creates, which is the convert name with a
    /// trailing underscore.
    /// </summary>
    /// <remarks>
    /// Verified against Siril 1.4.4: <c>convert astrodesk</c> writes
    /// <c>astrodesk_.seq</c> and frames named <c>astrodesk_00001.fit</c>.
    /// Referring to it as "astrodesk" fails with "Reading sequence failed, file
    /// cannot be opened: astrodesk.seq" and the whole run aborts before
    /// registration.
    /// </remarks>
    public const string SequenceName = ConvertName + "_";

    /// <summary>
    /// Script format version. Siril refuses scripts that do not declare one it
    /// understands, and 1.2 is the long-lived stable line.
    /// </summary>
    public const string RequiredVersion = "1.2.0";

    /// <summary>
    /// Below this many frames, sigma clipping has too small a sample to estimate
    /// a per-pixel distribution and will reject good signal. Fall back to a plain
    /// average instead of silently producing a worse stack.
    /// </summary>
    public const int MinimumFramesForSigmaClipping = 8;

    public static string Build(StackRequest request, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FrameExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputName);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        string extension = request.FrameExtension.TrimStart('.').ToLowerInvariant();
        StackMethod method = ResolveMethod(request.Method, frameCount);

        var script = new StringBuilder();
        script.AppendLine($"requires {RequiredVersion}");
        script.AppendLine();

        bool raw = IsRawExtension(extension);
        bool calibrating = HasDarks(request);

        // Convert whatever the phone produced into a Siril sequence.
        //
        // -debayer interpolates the Bayer mosaic to colour, and it belongs at
        // convert time only when nothing else will do it. With darks it has to
        // wait: a dark must be subtracted from the raw mosaic, before
        // interpolation mixes neighbouring photosites together, so calibrate
        // does the debayering instead. Debayering here as well would apply it
        // twice and ruin the frames.
        script.Append("convert ").Append(ConvertName).Append(" -out=.");
        if (raw && !calibrating)
        {
            script.Append(" -debayer");
        }

        script.AppendLine();
        script.AppendLine($"cd .");

        // Calibrate against a master dark when one is available. Siril prefixes
        // calibrated frames with pp_, so everything downstream shifts.
        string sequence = SequenceName;
        if (calibrating)
        {
            script.AppendLine();
            AppendDarkCalibration(script, raw);
            sequence = $"pp_{SequenceName}";
            script.AppendLine();
        }

        // Global star alignment. This is the step that defeats sky rotation.
        script.AppendLine($"register {sequence}");

        // Stack the registered sequence. Siril prefixes registered output with r_.
        script.AppendLine(BuildStackCommand($"r_{sequence}", request, method));

        // Autostretch on save only affects the preview copy, not the linear data.
        script.AppendLine($"load {request.OutputName}");
        script.AppendLine($"autostretch");
        script.AppendLine($"savetif {request.OutputName}_preview");
        script.AppendLine("close");

        return script.ToString();
    }

    /// <summary>
    /// Folder name holding the dark frames inside a run.
    /// </summary>
    public const string DarksFolderName = "darks";

    /// <summary>
    /// Base name handed to Siril's <c>convert</c> for the darks.
    /// </summary>
    public const string DarkConvertName = "dark";

    public const string MasterDarkName = "master_dark";

    /// <summary>
    /// True when the request has a usable set of darks.
    /// </summary>
    public static bool HasDarks(StackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !string.IsNullOrWhiteSpace(request.DarksDirectory);
    }

    /// <summary>
    /// Builds a master dark and subtracts it from the light frames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The darks are converted and stacked inside their own folder, and only
    /// then referenced from the working directory. Converting them alongside the
    /// lights would be simpler and wrong: Siril's <c>convert</c> takes every
    /// convertible file in the current directory, so the darks would be swept
    /// into the light sequence and averaged into the result as though they were
    /// exposures of the sky.
    /// </para>
    /// <para>
    /// The master is a plain rejection stack with normalisation off. Normalising
    /// darks would scale away the very offsets the calibration exists to
    /// measure.
    /// </para>
    /// </remarks>
    private static void AppendDarkCalibration(StringBuilder script, bool raw)
    {
        script.AppendLine($"cd {DarksFolderName}");
        script.AppendLine($"convert {DarkConvertName} -out=.");
        script.AppendLine($"stack {DarkConvertName}_ rej 3 3 -nonorm -out={MasterDarkName}");
        script.AppendLine("cd ..");

        script.Append("calibrate ").Append(SequenceName)
            .Append(" -dark=").Append(DarksFolderName).Append('/').Append(MasterDarkName);

        // A raw frame is still a Bayer mosaic at this point, so the calibration
        // has to be told it is working on one, and it takes over the debayering
        // that convert would otherwise have done.
        if (raw)
        {
            script.Append(" -cfa -equalize_cfa -debayer");
        }

        script.AppendLine();
    }

    /// <summary>
    /// Sigma clipping needs a reasonable sample per pixel to work; with a handful
    /// of frames it discards real signal, so degrade to a plain average.
    /// </summary>
    public static StackMethod ResolveMethod(StackMethod requested, int frameCount) =>
        requested == StackMethod.AverageSigmaClip && frameCount < MinimumFramesForSigmaClipping
            ? StackMethod.Average
            : requested;

    public static bool IsRawExtension(string extension) =>
        extension.TrimStart('.').ToLowerInvariant() is "dng" or "raw" or "cr2" or "nef" or "arw";

    private static string BuildStackCommand(
        string sequence,
        StackRequest request,
        StackMethod method) =>
        method switch
        {
            StackMethod.AverageSigmaClip => string.Create(
                CultureInfo.InvariantCulture,
                $"stack {sequence} rej {request.SigmaLow} {request.SigmaHigh} -norm=addscale -out={request.OutputName}"),
            StackMethod.Median =>
                $"stack {sequence} med -norm=addscale -out={request.OutputName}",
            _ =>
                $"stack {sequence} mean -norm=addscale -out={request.OutputName}",
        };
}
