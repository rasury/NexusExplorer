using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NexusExplorer.Views;

/// <summary>Paint only our native host's uncovered area; VLC owns its child renderer.</summary>
internal sealed class NativeVideoBackground : IDisposable
{
    private const uint WmPaint = 0x000F, WmEraseBackground = 0x0014, WmPrintClient = 0x0318, WmDestroy = 0x0082;
    private readonly SubclassProc _callback;
    private IntPtr _window;
    private static long _nextId;
    private readonly UIntPtr _id = (UIntPtr)(ulong)Interlocked.Increment(ref _nextId);
    internal NativeVideoBackground() => _callback = Paint;
    internal void Attach(IntPtr window)
    {
        if (window == IntPtr.Zero || window == _window) return;
        Dispose();
        if (!SetWindowSubclass(window, _callback, _id, UIntPtr.Zero)) throw new Win32Exception();
        _window = window;
        InvalidateRect(window, IntPtr.Zero, true);
    }
    private IntPtr Paint(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == WmDestroy) { RemoveWindowSubclass(window, _callback, id); _window = IntPtr.Zero; }
        if (message is WmEraseBackground or WmPrintClient)
        { FillBlack(window, wParam); return (IntPtr)1; }
        if (message == WmPaint)
        {
            var dc = BeginPaint(window, out var paint);
            try { FillBlack(window, dc); } finally { EndPaint(window, ref paint); }
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private static void FillBlack(IntPtr window, IntPtr dc)
    { GetClientRect(window, out var rect); FillRect(dc, ref rect, GetStockObject(4)); /* BLACK_BRUSH */ }
    public void Dispose()
    {
        if (_window != IntPtr.Zero) RemoveWindowSubclass(_window, _callback, _id);
        _window = IntPtr.Zero;
    }
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct
    {
        public IntPtr Dc; public int Erase; public Rect Rect; public int Restore, Incremental;
        public long Reserved1, Reserved2, Reserved3, Reserved4;
    }
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
}
