using System.Runtime.InteropServices;

namespace AstroDesk.Stacking;

/// <summary>
/// Deletes capture folders to the Recycle Bin rather than out of existence.
/// </summary>
/// <remarks>
/// Deleting a run used to be permanent. For a button whose whole purpose is
/// freeing space, that is the wrong default: one mis-click on the wrong row
/// destroys a night's frames with nothing to undo it, and a night is not
/// repeatable - the sky has moved on and the weather with it. The Recycle Bin
/// costs nothing while the disk has room and turns a mistake into an
/// inconvenience.
/// </remarks>
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    /// <summary>
    /// Checks a folder is somewhere it is safe to delete from.
    /// </summary>
    /// <remarks>
    /// Refuses anything that is not strictly inside <paramref name="root"/>,
    /// including the root itself. Without this a bad or empty path could ask the
    /// shell to remove the whole capture folder, and the shell would oblige.
    /// </remarks>
    public static bool IsSafeToDelete(string? folderPath, string? root)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (string.Equals(target, parent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return target.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends a folder to the Recycle Bin.
    /// </summary>
    /// <returns>True when the shell reported success.</returns>
    public static bool TrySendFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        // SHFileOperation takes a double-null-terminated list of paths.
        ShFileOpStruct operation = new()
        {
            Func = FoDelete,
            From = Path.GetFullPath(folderPath) + '\0' + '\0',
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };

        return SHFileOperation(ref operation) == 0 && !operation.AnyOperationsAborted;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public nint Window;
        public uint Func;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public nint NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }
}
