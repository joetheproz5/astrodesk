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
    /// Siril's sequence name for the converted frames. Every later command
    /// refers to this, and the registered output becomes "r_" + this.
    /// </summary>
    public const string SequenceName = "astrodesk";

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

        // Convert whatever the phone produced into a Siril sequence. -debayer
        // matters for DNG: the sensor data is a Bayer mosaic and must be
        // interpolated to colour before frames can be compared or combined.
        script.Append("convert ").Append(SequenceName).Append(" -out=.");
        if (IsRawExtension(extension))
        {
            script.Append(" -debayer");
        }

        script.AppendLine();
        script.AppendLine($"cd .");

        // Global star alignment. This is the step that defeats sky rotation.
        script.AppendLine($"register {SequenceName}");

        // Stack the registered sequence. Siril prefixes registered output with r_.
        script.AppendLine(BuildStackCommand($"r_{SequenceName}", request, method));

        // Autostretch on save only affects the preview copy, not the linear data.
        script.AppendLine($"load {request.OutputName}");
        script.AppendLine($"autostretch");
        script.AppendLine($"savetif {request.OutputName}_preview");
        script.AppendLine("close");

        return script.ToString();
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
