namespace AstroDesk.Stacking;

/// <summary>
/// A camera app on the phone, paired with the folder it writes captures to.
/// </summary>
/// <remarks>
/// The package and the output folder have to travel together. Each Samsung
/// camera app writes somewhere different — Expert RAW to <c>DCIM/Expert RAW</c>,
/// the stock camera to <c>DCIM/Camera</c> — so watching one folder while
/// launching a different app produces a run that fires the shutter, captures
/// successfully, and imports nothing.
/// </remarks>
/// <param name="Name">Display name.</param>
/// <param name="Package">Android package, used to launch and to confirm it is up.</param>
/// <param name="RemoteFolder">Absolute device path the app saves captures to.</param>
public sealed record CameraApp(string Name, string Package, string RemoteFolder)
{
    /// <summary>
    /// Samsung's manual/RAW camera. The default for AstroDesk because it is the
    /// one that offers long exposures and writes DNG, which is what stacking wants.
    /// </summary>
    public static CameraApp ExpertRaw { get; } = new(
        "Expert RAW",
        "com.samsung.android.app.galaxyraw",
        "/sdcard/DCIM/Expert RAW");

    public static CameraApp StockCamera { get; } = new(
        "Camera",
        "com.sec.android.app.camera",
        "/sdcard/DCIM/Camera");

    public static CameraApp DeepSkyCamera { get; } = new(
        "DeepSky Camera",
        "de.seebi.deepskycamera",
        "/sdcard/DCIM/DeepSkyCamera");

    public static IReadOnlyList<CameraApp> All { get; } =
        [ExpertRaw, StockCamera, DeepSkyCamera];

    public static CameraApp Default => ExpertRaw;

    /// <summary>
    /// The folder quoted for use inside an <c>adb shell</c> command line.
    /// </summary>
    /// <remarks>
    /// adb concatenates shell arguments into one string that the device's shell
    /// then re-splits, so "Expert RAW" arrives as two arguments and the listing
    /// fails. Verified on device: unquoted returns "No such file or directory",
    /// single-quoted lists correctly. <c>adb pull</c> takes the path as a single
    /// argument and needs no quoting.
    /// </remarks>
    public string ShellQuotedFolder => $"'{RemoteFolder}'";

    public static CameraApp FromName(string? name) =>
        All.FirstOrDefault(app =>
            string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Default;

    public override string ToString() => Name;
}
