using System.IO;
using System.Runtime.InteropServices;

namespace DirSnap2.Services;

/// <summary>
/// Moves files and folders to the Windows Recycle Bin (SHFileOperation with FOF_ALLOWUNDO).
/// Drives without a Recycle Bin (network, removable) are refused: there Windows would
/// delete permanently without asking.
/// </summary>
public static class RecycleBinService
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>Null when the path's drive has a Recycle Bin, otherwise the reason it has not.</summary>
    public static string? GetUnsupportedReason(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return "Network paths have no Recycle Bin: the item would be deleted permanently.";

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return "Unknown drive.";
            var drive = new DriveInfo(root);
            return drive.DriveType switch
            {
                DriveType.Fixed => null,
                DriveType.Network => "Network drives have no Recycle Bin: the item would be deleted permanently.",
                DriveType.Removable => "Removable drives (USB sticks, SD cards) have no Recycle Bin: the item would be deleted permanently.",
                _ => $"This drive ({drive.DriveType}) has no Recycle Bin: the item would be deleted permanently.",
            };
        }
        catch (Exception ex)
        {
            return $"Cannot check the drive: {ex.Message}";
        }
    }

    /// <summary>Moves the item to the Recycle Bin. Throws when it fails or it is still there.</summary>
    public static void MoveToRecycleBin(string path)
    {
        if (GetUnsupportedReason(path) is { } reason)
            throw new InvalidOperationException(reason);

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path).TrimEnd('\\') + "\0\0", // double-null terminated list
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"Windows could not move the item to the Recycle Bin (error 0x{result:X}). It may be open in another program.");
        if (op.fAnyOperationsAborted)
            throw new IOException("The operation was cancelled.");
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("The item is still there: it may be open in another program.");
    }
}
