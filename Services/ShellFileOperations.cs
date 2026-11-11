using System.IO;
using System.Runtime.InteropServices;

namespace DirSnap2.Services;

/// <summary>
/// Copy / move through the Windows shell (SHFileOperation), like Explorer: Windows shows its own
/// progress window and asks what to do when a name already exists (replace, skip...).
/// </summary>
public static class ShellFileOperations
{
    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const ushort FOF_RENAMEONCOLLISION = 0x0008;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const int ERROR_CANCELLED = 0x04C7;

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

    /// <summary>
    /// Copies the items into <paramref name="destFolder"/>. With <paramref name="renameOnCollision"/>
    /// an existing name gets a new one ("name - Copy.txt") instead of the replace question.
    /// Returns false when the user cancelled.
    /// </summary>
    public static bool Copy(IReadOnlyList<string> sources, string destFolder, bool renameOnCollision, IntPtr owner) =>
        Run(FO_COPY, sources, destFolder, renameOnCollision, owner);

    /// <summary>Moves the items into <paramref name="destFolder"/>. Returns false when the user cancelled.</summary>
    public static bool Move(IReadOnlyList<string> sources, string destFolder, IntPtr owner) =>
        Run(FO_MOVE, sources, destFolder, renameOnCollision: false, owner);

    /// <summary>Full path without a trailing backslash, except for drive roots ("C:\" must keep it).</summary>
    public static string CleanPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ? full : full.TrimEnd('\\', '/');
    }

    private static bool Run(uint func, IReadOnlyList<string> sources, string destFolder, bool renameOnCollision, IntPtr owner)
    {
        if (sources.Count == 0) return true;

        ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR;
        if (renameOnCollision) flags |= FOF_RENAMEONCOLLISION;

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = func,
            // Both lists are double-null terminated
            pFrom = string.Join("\0", sources.Select(CleanPath)) + "\0\0",
            pTo = CleanPath(destFolder) + "\0\0",
            fFlags = flags,
        };

        int result = SHFileOperation(ref op);
        if (op.fAnyOperationsAborted || result == ERROR_CANCELLED)
            return false;
        if (result != 0)
            throw new IOException($"Windows could not complete the operation (error 0x{result:X}).");
        return true;
    }
}
