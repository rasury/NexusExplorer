using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NexusExplorer.Infrastructure;

internal static class WindowsVolume
{
    private static string? Volume(string path)
    {
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing))
        { var parent = Path.GetDirectoryName(existing); if (parent is null) return null; existing = parent; }
        var mount = new StringBuilder(1024); var volume = new StringBuilder(1024);
        return GetVolumePathName(existing, mount, mount.Capacity) && GetVolumeNameForVolumeMountPoint(mount.ToString(), volume, volume.Capacity)
            ? volume.ToString() : null;
    }
    public static bool SameVolume(string source, string target)
    {
        var volume = Volume(source);
        return volume is not null && string.Equals(volume, Volume(target), StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetVolumePathName(string path, StringBuilder mount, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder volume, int length);
}
