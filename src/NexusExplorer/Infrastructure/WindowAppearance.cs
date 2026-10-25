using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NexusExplorer.Infrastructure;
internal static class WindowAppearance
{
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = UiThemeService.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var rounded = 2; // DWMWCP_ROUND; DWM removes rounding while maximized.
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }
    internal static void UpdateMaximizedInset(Window window, FrameworkElement surface)
    {
        if (window.WindowState != WindowState.Maximized) { surface.Margin = new Thickness(0); return; }
        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetWindowRect(hwnd, out var rect)) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        surface.Margin = new Thickness(Math.Max(0, info.Work.Left - rect.Left) / dpi.DpiScaleX,
            Math.Max(0, info.Work.Top - rect.Top) / dpi.DpiScaleY,
            Math.Max(0, rect.Right - info.Work.Right) / dpi.DpiScaleX,
            Math.Max(0, rect.Bottom - info.Work.Bottom) / dpi.DpiScaleY);
    }
    internal static void FillMonitor(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
            SetWindowPos(hwnd, IntPtr.Zero, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 0x24);
    }
    internal static void SetFullscreenFrame(Window window, bool fullscreen)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var corner = fullscreen ? 1 : 2;
        var border = fullscreen ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        _ = DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectInt rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
    [StructLayout(LayoutKind.Sequential)] private struct PointInt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectInt { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointInt Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectInt Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
