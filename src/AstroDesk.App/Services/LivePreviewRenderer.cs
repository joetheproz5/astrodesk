using System.IO;
using System.Windows.Media;
using AstroDesk.Stacking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.App.Services;

/// <summary>
/// Bridges captured files and the running preview stack.
/// </summary>
/// <remarks>
/// The decoding itself lives in <see cref="PreviewDecoder"/>, in the stacking
/// library, so the pipeline tests exercise the real thing rather than a copy of
/// it. What is left here is the part that belongs to the app: logging which
/// files were skipped, and handing WPF an <see cref="ImageSource"/>.
/// </remarks>
public interface ILivePreviewRenderer
{
    /// <summary>Working width of the preview stack, in pixels.</summary>
    int TargetWidth { get; }

    /// <summary>
    /// Decodes a captured file into an RGB preview frame, or null when the file
    /// cannot be read as an image.
    /// </summary>
    PreviewImage? Decode(string path);

    /// <summary>Renders a stacked result into a frozen, displayable bitmap.</summary>
    ImageSource Render(PreviewImage image);

    /// <summary>
    /// Small colour thumbnail of a capture for the frame list, or null when the
    /// file cannot be read.
    /// </summary>
    ImageSource? LoadThumbnail(string path, int width = 160);
}

public sealed class LivePreviewRenderer(ILogger<LivePreviewRenderer>? logger = null)
    : ILivePreviewRenderer
{
    private readonly ILogger<LivePreviewRenderer> _logger =
        logger ?? NullLogger<LivePreviewRenderer>.Instance;

    public int TargetWidth => PreviewDecoder.DefaultTargetWidth;

    public PreviewImage? Decode(string path)
    {
        PreviewImage? frame = PreviewDecoder.Decode(path, TargetWidth);
        if (frame is null && !string.IsNullOrWhiteSpace(path))
        {
            _logger.LogDebug("Live preview skipped {File}.", Path.GetFileName(path));
        }

        return frame;
    }

    public ImageSource Render(PreviewImage image) => PreviewDecoder.Render(image);

    public ImageSource? LoadThumbnail(string path, int width = 160)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return PreviewDecoder.LoadDownscaled(path, width);
        }
        catch (Exception exception) when (PreviewDecoder.IsUnreadable(exception))
        {
            _logger.LogDebug(exception, "No thumbnail for {File}.", Path.GetFileName(path));
            return null;
        }
    }
}
