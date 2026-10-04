using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NexusExplorer.Infrastructure;

internal readonly record struct FileIdentity(ulong Volume, ulong Low, ulong High);

internal static class WindowsFileIdentity
{
    public static FileIdentity? Read(string path)
    {
        // Read metadata only. Respect exclusive locks; never read the file payload.
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (GetFileInformationByHandleEx(handle, 18 /* FileIdInfo */, out var info, (uint)Marshal.SizeOf<IdInfo>()))
            return info.Low == 0 && info.High == 0 ? null : new(info.Volume, info.Low, info.High);
        var error = Marshal.GetLastWin32Error();
        if (error is 1 or 50 or 87) return null; // Unsupported filesystem: use verified copy.
        throw new IOException("无法读取文件标识。", new Win32Exception(error));
    }
    public static bool Matches(string path, FileIdentity identity) => File.Exists(path) && Read(path) == identity;
    public static void Move(string source, string target)
    {
        // No COPY_ALLOWED or REPLACE_EXISTING: a changed mount or racing target
        // must fail rather than silently copy or overwrite user data.
        if (!MoveFileEx(source, target, 8 /* WRITE_THROUGH */))
            throw new IOException("同卷文件移动失败。", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IdInfo { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out IdInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string target, uint flags);
}
