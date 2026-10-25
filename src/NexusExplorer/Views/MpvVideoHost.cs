using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;

namespace NexusExplorer.Views;

/// <summary>Owns the parent HWND only. mpv owns and releases its video child.</summary>
public sealed class MpvVideoHost : HwndHost
{
    private readonly NativeVideoBackground _background = new();
    public event Action<IntPtr>? SurfaceCreated;
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(MpvVideoHost), new PropertyMetadata(16d, (d, _) => ((MpvVideoHost)d).UpdateClip()));
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    { base.OnWindowPositionChanged(rcBoundingBox); UpdateClip(); }
    private (int Width, int Height, int RadiusX, int RadiusY)? _lastClip;
    private void UpdateClip()
    {
        if (Handle == IntPtr.Zero || !GetClientRect(Handle, out var size)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var radiusX = (int)(2 * CornerRadius * dpi.DpiScaleX);
        var radiusY = (int)(2 * CornerRadius * dpi.DpiScaleY);
        var shape = (size.Right, size.Bottom, radiusX, radiusY);
        if (_lastClip == shape) return;
        if (CornerRadius <= 0) { SetWindowRgn(Handle, IntPtr.Zero, true); _lastClip = shape; return; }
        var region = CreateRoundRectRgn(0, 0, size.Right + 1, size.Bottom + 1, radiusX, radiusY);
        if (SetWindowRgn(Handle, region, true) == 0) DeleteObject(region);
        else _lastClip = shape;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var hwnd = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new Win32Exception();
        _lastClip = null;
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
