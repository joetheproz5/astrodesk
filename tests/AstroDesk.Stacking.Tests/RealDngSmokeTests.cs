using System.Windows.Media.Imaging;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Runs the extractor against real captures when any are present on the machine.
/// </summary>
/// <remarks>
/// Skipped automatically when no captures exist, so the suite stays portable.
/// The synthetic tests cover the parsing rules; this catches the case where a
/// real file does not match the layout those rules assume, which is precisely
/// how the live preview came to fail silently on RAW-only sessions.
/// </remarks>
public sealed class RealDngSmokeTests
{
    private static string CaptureFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "AstroDesk",
        "Phone Captures");

    private static string[] FindDngs() =>
        Directory.Exists(CaptureFolder)
            ? Directory.GetFiles(CaptureFolder, "*.dng", SearchOption.AllDirectories)
            : [];

    [Fact]
    public void EveryRealDngYieldsADecodablePreview()
    {
        string[] files = FindDngs();
        if (files.Length == 0)
        {
            return; // No captures on this machine; nothing to assert.
        }

        foreach (string file in files.Take(5))
        {
            byte[]? jpeg = DngPreviewExtractor.TryExtract(file);

            Assert.True(jpeg is not null, $"no preview extracted from {Path.GetFileName(file)}");
            Assert.True(jpeg!.Length > 50_000, $"preview from {Path.GetFileName(file)} is implausibly small");

            // Extracting bytes is not enough: the truncated blob a naive marker
            // scan produces also looks like a JPEG at the start and then fails
            // here, which is the failure this guards against.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = new MemoryStream(jpeg);
            bitmap.EndInit();
            bitmap.Freeze();

            Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
        }
    }
}
