using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NexusExplorer.Views;

/// <summary>Owns the parent HWND only. mpv owns and releases its video child.</summary>
public sealed class MpvVideoHost : HwndHost
{
    private readonly NativeVideoBackground _background = new();
    public event Action<IntPtr>? SurfaceCreated;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var hwnd = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new Win32Exception();
        _background.Attach(hwnd);
        SurfaceCreated?.Invoke(hwnd);
        return new HandleRef(this, hwnd);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _background.Dispose();
        DestroyWindow(hwnd.Handle);
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
}
