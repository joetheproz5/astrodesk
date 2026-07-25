using System.Globalization;
using System.Text.RegularExpressions;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

/// <summary>
/// Nudges the camera app into finishing a capture so the file lands promptly.
/// </summary>
public interface ICaptureFlushPrompt
{
    bool IsEnabled { get; set; }

    /// <summary>Opens the just-taken shot, which forces the app to finish it.</summary>
    Task PromptAsync(string serial, CancellationToken cancellationToken = default);

    /// <summary>Returns to the camera so the next shutter press lands.</summary>
    Task DismissAsync(string serial, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens and closes the review thumbnail after each shot.
/// </summary>
/// <remarks>
/// <para>
/// Expert RAW defers finishing a capture: measured on an S23 Ultra, a shot left
/// alone took about 95 seconds to appear on disk, and sometimes had still not
/// appeared after two minutes. Opening the review thumbnail makes the app
/// complete the pending work immediately — the same shot then landed in 28
/// seconds, with both the DNG and its JPEG.
/// </para>
/// <para>
/// This is a screen tap, which is exactly the fragility avoided for the shutter
/// itself, and it is used only because nothing else works. A MediaScanner
/// broadcast has no effect, opening Gallery has no effect, and
/// <c>uiautomator dump</c> cannot locate the control because a live camera
/// preview never reaches the idle state it waits for. The position is therefore
/// stored as a fraction of the screen rather than a pixel, so it survives a
/// different device resolution, and the whole behaviour is optional.
/// </para>
/// </remarks>
public sealed partial class CaptureFlushPrompt : ICaptureFlushPrompt
{
    /// <summary>
    /// Where the review thumbnail sits, as a fraction of screen width and height.
    /// Measured from a screenshot: (240, 2754) on a 1440x3088 panel.
    /// </summary>
    public const double DefaultHorizontalFraction = 240.0 / 1440.0;

    public const double DefaultVerticalFraction = 2754.0 / 3088.0;

    /// <summary>
    /// Pause between the shutter and the tap. The thumbnail shows the previous
    /// shot until the new one is registered, so tapping immediately opens the
    /// wrong image and does not prompt the pending capture.
    /// </summary>
    public static readonly TimeSpan DefaultPromptDelay = TimeSpan.FromSeconds(3);

    private readonly IAdbCommandExecutor _adb;
    private readonly IAdbInputService _input;
    private readonly ILogger<CaptureFlushPrompt> _logger;
    private (int Width, int Height)? _screenSize;

    public CaptureFlushPrompt(
        IAdbCommandExecutor adb,
        IAdbInputService input,
        ILogger<CaptureFlushPrompt>? logger = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _logger = logger ?? NullLogger<CaptureFlushPrompt>.Instance;
    }

    public bool IsEnabled { get; set; } = true;

    public double HorizontalFraction { get; set; } = DefaultHorizontalFraction;

    public double VerticalFraction { get; set; } = DefaultVerticalFraction;

    public TimeSpan PromptDelay { get; set; } = DefaultPromptDelay;

    public async Task PromptAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        try
        {
            await Task.Delay(PromptDelay, cancellationToken).ConfigureAwait(false);

            (int width, int height) = await GetScreenSizeAsync(serial, cancellationToken)
                .ConfigureAwait(false);

            int x = (int)Math.Round(width * HorizontalFraction);
            int y = (int)Math.Round(height * VerticalFraction);

            await _input.TapAsync(serial, x, y, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Prompted the camera to finish the capture at {X},{Y}.", x, y);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed prompt only costs latency; the capture still completes on
            // its own, so it must not abort the run.
            _logger.LogWarning(exception, "Could not prompt the camera to finish the capture.");
        }
    }

    public async Task DismissAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        try
        {
            // Back returns to the viewfinder. Without it the review screen keeps
            // the foreground and the next volume-key press never reaches the
            // shutter.
            await _input
                .SendKeyAsync(serial, AndroidKeyCode.Back, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not return to the camera after the review.");
        }
    }

    private async Task<(int Width, int Height)> GetScreenSizeAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (_screenSize is { } cached)
        {
            return cached;
        }

        ProcessExecutionResult result = await _adb
            .ExecuteAsync(serial, ["shell", "wm", "size"], cancellationToken)
            .ConfigureAwait(false);

        (int Width, int Height) size = ParseScreenSize(result.StandardOutput);
        _screenSize = size;
        return size;
    }

    /// <summary>
    /// Reads "Physical size: 1440x3088" from <c>wm size</c>, preferring an
    /// override line when the device reports one.
    /// </summary>
    public static (int Width, int Height) ParseScreenSize(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        MatchCollection matches = SizePattern().Matches(output);
        if (matches.Count == 0)
        {
            // A sensible fallback rather than a crash; the tap simply lands in
            // roughly the right place on a similar phone.
            return (1440, 3088);
        }

        // An override size, when present, is what is actually being displayed.
        Match match = matches[^1];
        return (
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"(\d{3,5})x(\d{3,5})", RegexOptions.CultureInvariant)]
    private static partial Regex SizePattern();
}
