using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Serilog;

namespace NexusExplorer.Views;

/// <summary>OLE captures mouse input during dragging; route wheel input only inside our drop viewport.</summary>
internal sealed class DragWheelScroller : IDisposable
{
    private const int MouseLowLevel = 14, MouseWheel = 0x020A;
    private readonly HookProc _callback;
    private ScrollViewer? _viewer;
    private IntPtr _hook;
    private int _wheelRemainder;
    private long _generation;
    internal bool IsActive => _viewer is not null;
    internal bool HasNativeHook => _hook != IntPtr.Zero;
    public DragWheelScroller() => _callback = OnMouse;

    public void Start(ScrollViewer viewer)
    {
        viewer.Dispatcher.VerifyAccess();
        if (ReferenceEquals(viewer, _viewer)) return;
        Dispose(); _viewer = viewer;
        // A thread-only mouse hook misses drags originating in Explorer, whose OLE loop owns capture.
        _hook = SetWindowsHookEx(MouseLowLevel, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Warning(new Win32Exception(Marshal.GetLastWin32Error()), "无法启用拖放时的滚轮滚动");
    }
    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() == MouseWheel && _viewer is { } viewer)
        {
            try
            {
                var mouse = Marshal.PtrToStructure<MouseData>(data);
                var source = PresentationSource.FromVisual(viewer) as HwndSource;
                // Do not consume wheel input for another application covering our window.
                if (source is not null && GetAncestor(WindowFromPoint(mouse.Position), 2) == source.Handle)
                {
                    var point = viewer.PointFromScreen(new Point(mouse.Position.X, mouse.Position.Y));
                    if (TryQueueWheel(unchecked((short)(mouse.Data >> 16)), point)) return (IntPtr)1;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "拖放滚轮已交回系统处理"); }
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    internal bool TryQueueWheel(int delta, Point viewportPoint)
    {
        if (_viewer is not { } viewer || delta == 0 || viewer.ScrollableHeight <= 0 ||
            !new Rect(0, 0, viewer.ActualWidth, viewer.ActualHeight).Contains(viewportPoint)) return false;
        var generation = _generation;
        // Keep native callbacks short and let OLE continue. Drop/leave invalidates already queued work.
        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
        {
            if (generation != _generation || !ReferenceEquals(viewer, _viewer)) return;
            _wheelRemainder += delta;
            var clicks = _wheelRemainder / 120; _wheelRemainder %= 120;
            var lines = SystemParameters.WheelScrollLines;
            for (var click = 0; click < Math.Abs(clicks); click++)
            {
                if (lines < 0) { if (clicks > 0) viewer.PageUp(); else viewer.PageDown(); }
                else for (var line = 0; line < lines; line++)
                    if (clicks > 0) viewer.LineUp(); else viewer.LineDown();
            }
        }));
        return true;
    }
    public void Dispose()
    {
        _viewer?.Dispatcher.VerifyAccess();
        _generation++; _viewer = null; _wheelRemainder = 0;
        if (_hook == IntPtr.Zero) return;
        if (!UnhookWindowsHookEx(_hook)) Log.Warning("拖放滚轮挂钩释放失败：{Error}", Marshal.GetLastWin32Error());
        _hook = IntPtr.Zero;
    }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    { public ScreenPoint Position; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
