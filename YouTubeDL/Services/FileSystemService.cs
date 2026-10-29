using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YouTubeDL.Services;

/// <summary>Opening files/folders, free space and deleting to the recycle bin.</summary>
public static class FileSystemService
{
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        Start(folder);
    }

    /// <summary>Shows the file selected in Explorer (Windows) or opens its folder.</summary>
    public static void RevealFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            Start("explorer.exe", $"/select,\"{path}\"");
            return;
        }

        OpenFolder(path);
    }

    public static void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Start(path);
            return;
        }

        if (File.Exists(path))
            Start(path);
    }

    public static long? GetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
                return null;

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Moves a file to the recycle bin on Windows; deletes it elsewhere.</summary>
    public static bool DeleteToRecycleBin(string path, bool confirm)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var operation = new ShFileOpStruct
                {
                    wFunc = FoDelete,
                    pFrom = path + '\0' + '\0',
                    fFlags = (ushort)(FoAllowUndo | (confirm ? 0 : FoNoConfirmation | FoSilent))
                };

                return SHFileOperation(ref operation) == 0;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static void Start(string fileName, string? arguments = null)
    {
        try
        {
            var info = arguments is null
                ? new ProcessStartInfo(fileName) { UseShellExecute = true }
                : new ProcessStartInfo(fileName, arguments) { UseShellExecute = true };

            Process.Start(info);
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private const uint FoDelete = 0x0003;
    private const ushort FoNoConfirmation = 0x0010;
    private const ushort FoAllowUndo = 0x0040;
    private const ushort FoSilent = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
