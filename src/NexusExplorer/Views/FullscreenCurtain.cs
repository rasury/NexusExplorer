using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NexusExplorer.Views;

// A short-lived, non-activating native window covers both WPF and mpv HWNDs.
// The desktop capture stays in memory and is discarded after the layout transaction.
internal sealed class FullscreenCurtain : IDisposable
{
    private readonly Window _window;
    private readonly Image _image;
    private FullscreenCurtain(Window window, Image image) { _window = window; _image = image; }

    internal static FullscreenCurtain Create(Window owner)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) throw new Win32Exception();
        var bounds = info.Monitor;
        var image = new Image { Source = Capture(bounds), Stretch = Stretch.Fill };
        var window = new Window
        {
            Owner = owner, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Background = Brushes.Black, Content = image, Left = owner.Left, Top = owner.Top,
            Width = 1, Height = 1
        };
        var result = new FullscreenCurtain(window, image);
        try
        {
            var cover = new WindowInteropHelper(window).EnsureHandle();
            var disabled = 1;
            DwmSetWindowAttribute(cover, 3, ref disabled, sizeof(int)); // No OS window transition.
            DwmSetWindowAttribute(cover, 33, ref disabled, sizeof(int)); // No rounded corners.
            // Position before Show, using physical monitor coordinates (including negative origins).
            SetWindowPos(cover, IntPtr.Zero, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0014);
            window.Show();
            window.UpdateLayout();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal async Task PresentAsync()
    {
        // Drain WPF's layout/render work before waiting for this process's queued DWM updates.
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        DwmFlush();
    }

    public void Dispose()
    {
        _window.Close();
        _image.Source = null;
    }

    private static BitmapSource Capture(NativeRect rect)
    {
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new Win32Exception();
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x40CC0020)) throw new Win32Exception();
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); return source;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, int flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
