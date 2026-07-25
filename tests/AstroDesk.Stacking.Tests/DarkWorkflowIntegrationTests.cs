using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Exercises collecting darks and then actually calibrating with them.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of this were built separately: the session service routes
/// captures into a darks folder, and the script builder subtracts a master dark.
/// Each is covered on its own, and neither test would notice if they stopped
/// agreeing about where the frames go - which is the one failure that would
/// leave a night's calibration silently undone.
/// </para>
/// <para>
/// Runs the real Siril when it is present and skips otherwise, so the suite
/// stays portable. Frames carry deliberate hot pixels, because a hot pixel is
/// what a dark is for: it is identical in every exposure, so stacking preserves
/// it perfectly and only calibration removes it.
/// </para>
/// </remarks>
public sealed class DarkWorkflowIntegrationTests : IDisposable
{
    private const int Width = 300;
    private const int Height = 220;

    /// <summary>Sites deliberately made hot in every light and every dark.</summary>
    private static readonly (int X, int Y)[] HotPixels =
        [(5, 5), (295, 215), (150, 30), (40, 200)];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "astrodesk-darkflow",
        Guid.NewGuid().ToString("N"));

    public DarkWorkflowIntegrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Siril can still be releasing handles as the test finishes. A
            // leftover temp folder must not fail the suite.
        }
    }

    private static string? FindSiril()
    {
        string[] candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "astro 2", "tools", "siril", "bin", "siril-cli.exe"),
            @"C:\Program Files\Siril\bin\siril-cli.exe",
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public void CollectedDarksCalibrateTheLightsTheyWereCollectedWith()
    {
        string? siril = FindSiril();
        if (siril is null)
        {
            // Portable by design: the script shape is covered by unit tests, and
            // this adds the part only a real run can answer.
            return;
        }

        // --- collect, through the same service the app uses -------------------
        var session = new StackSessionService();
        string runFolder = session.Arm(_root, targetFrameCount: 0);

        string incoming = Path.Combine(_root, "incoming");
        Directory.CreateDirectory(incoming);

        for (int frame = 0; frame < 8; frame++)
        {
            string path = Path.Combine(incoming, $"light{frame:00}.png");
            WriteFrame(path, withStars: true, drift: frame);
            Assert.NotNull(session.Offer(path, DateTimeOffset.Now));
        }

        session.StartDarkCollection();

        for (int frame = 0; frame < 6; frame++)
        {
            string path = Path.Combine(incoming, $"dark{frame:00}.png");
            WriteFrame(path, withStars: false, drift: 0);
            Assert.NotNull(session.Offer(path, DateTimeOffset.Now));
        }

        session.StopDarkCollection();

        // The lights must not have been polluted by the darks.
        Assert.Equal(8, session.Frames.Count);
        Assert.Equal(6, session.DarkFrameCount);

        // --- calibrate, through the same builder the app uses -----------------
        var request = new StackRequest(
            runFolder,
            "png",
            DarksDirectory: session.DarksFolder);

        Assert.True(
            SirilScriptBuilder.HasDarks(request),
            "the collected darks were not visible to the stack request");

        string scriptPath = Path.Combine(runFolder, "calibrate.ssf");
        File.WriteAllText(scriptPath, SirilScriptBuilder.Build(request, session.Frames.Count));

        string log = RunSiril(siril, runFolder, scriptPath);

        // --- did the calibration actually remove the hot pixels? --------------
        string process = Path.Combine(runFolder, SirilScriptBuilder.ProcessFolderName);
        Assert.True(
            Directory.Exists(process),
            $"Siril produced no process folder. Its output was:{Environment.NewLine}{Tail(log)}");

        string[] calibrated = Directory.GetFiles(process, "pp_*.fit");
        Assert.True(
            calibrated.Length > 0,
            "no calibrated frames were produced, so the darks were never applied");

        string raw = ExportFrame(siril, runFolder, $"{SirilScriptBuilder.SequenceName}00001", "probe_raw");
        string cal = ExportFrame(siril, runFolder, $"pp_{SirilScriptBuilder.SequenceName}00001", "probe_cal");

        foreach ((int x, int y) in HotPixels)
        {
            int before = Sample(raw, x, y);
            int after = Sample(cal, x, y);

            Assert.True(
                before > 200,
                $"the hot pixel at {x},{y} was not hot to begin with ({before})");
            Assert.True(
                after < 40,
                $"the hot pixel at {x},{y} survived calibration ({before} -> {after})");
        }
    }

    /// <summary>
    /// The last few lines of a Siril log, which is where it says what went wrong.
    /// </summary>
    private static string Tail(string log) =>
        string.Join(
            Environment.NewLine,
            log.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(12));

    private static string ExportFrame(string siril, string runFolder, string sequenceFrame, string outputName)
    {
        string script = string.Join(
            Environment.NewLine,
            "requires 1.2.0",
            $"cd {SirilScriptBuilder.ProcessFolderName}",
            $"load {sequenceFrame}",
            $"savetif ../{outputName}",
            "close");

        string path = Path.Combine(runFolder, $"{outputName}.ssf");
        File.WriteAllText(path, script);
        RunSiril(siril, runFolder, path);

        return Path.Combine(runFolder, $"{outputName}.tif");
    }

    /// <summary>
    /// Runs a Siril script and returns everything it said.
    /// </summary>
    /// <remarks>
    /// The output is read while the process runs rather than after it exits.
    /// Siril is verbose enough to fill the pipe buffer, and a redirected stream
    /// that nobody drains blocks the writer - the run then hangs until the
    /// timeout and looks like a silent failure rather than a deadlock.
    /// </remarks>
    private static string RunSiril(string siril, string workingDirectory, string scriptPath)
    {
        using Process process = Process.Start(new ProcessStartInfo(siril)
        {
            ArgumentList = { "-d", workingDirectory, "-s", scriptPath },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(milliseconds: 180_000))
        {
            process.Kill(entireProcessTree: true);
            return "Siril did not finish in time.";
        }

        return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads one pixel out of a 16-bit TIFF Siril wrote, scaled back to 0-255.
    /// </summary>
    private static int Sample(string tif, int x, int y)
    {
        var decoder = new TiffBitmapDecoder(
            new Uri(tif),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var grey = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Gray8, null, 0);
        grey.Freeze();

        byte[] pixel = new byte[1];
        grey.CopyPixels(
            new System.Windows.Int32Rect(
                Math.Clamp(x, 0, grey.PixelWidth - 1),
                Math.Clamp(y, 0, grey.PixelHeight - 1),
                1,
                1),
            pixel,
            1,
            0);

        return pixel[0];
    }

    /// <summary>
    /// A frame of sky, or of nothing. Both carry the same hot pixels; only the
    /// lights carry stars, and only the lights drift.
    /// </summary>
    private static void WriteFrame(string path, bool withStars, int drift)
    {
        var random = new Random(withStars ? 11 + drift : 500 + drift);
        byte[] pixels = new byte[Width * Height];

        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)random.Next(6, 14);
        }

        if (withStars)
        {
            var stars = new Random(3);
            for (int star = 0; star < 40; star++)
            {
                DrawStar(
                    pixels,
                    stars.Next(15, Width - 15) + (drift * 2),
                    stars.Next(15, Height - 15) + drift,
                    stars.Next(140, 255));
            }
        }

        foreach ((int x, int y) in HotPixels)
        {
            pixels[(y * Width) + x] = 255;
        }

        BitmapSource source = BitmapSource.Create(
            Width, Height, 96, 96, PixelFormats.Gray8, null, pixels, Width);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawStar(byte[] pixels, int centreX, int centreY, int peak)
    {
        for (int dy = -3; dy <= 3; dy++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = centreX + dx;
                int y = centreY + dy;
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    continue;
                }

                int value = (int)(peak * Math.Exp(-((dx * dx) + (dy * dy)) * 0.4));
                int index = (y * Width) + x;
                pixels[index] = (byte)Math.Min(255, pixels[index] + value);
            }
        }
    }
}
