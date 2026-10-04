using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace NexusExplorer.Views.Dialogs;

/// <summary>
/// 对话框统一外观。窗口行为属性(边框样式、不可调大小、高度适配、居中所有者、
/// 不出现在任务栏)在代码中设置——放在 ResourceDictionary 样式里会在样式首次
/// 延迟实例化时抛 XamlParseException(Setter.Property 无法解析)。
/// </summary>
internal static class DialogChrome
{
    public static void Apply(Window window)
    {
        window.WindowStyle = WindowStyle.ToolWindow;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = double.IsNaN(window.Height) ? SizeToContent.Height : SizeToContent.Manual;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false;
        window.SourceInitialized += (_, _) => LimitToWorkArea(window, WorkArea(window));
        window.ContentRendered += (_, _) => KeepOnScreen(window, WorkArea(window));
    }

    public static Window? Owner => System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner ? owner : null;

    public static void SetContent(Window window, UIElement body, FrameworkElement footer)
    {
        var layout = new Grid { Margin = new Thickness(20) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        footer.Margin = new Thickness(0, 16, 0, 0);
        Grid.SetRow(footer, 1);
        layout.Children.Add(scroll); layout.Children.Add(footer);
        window.Content = layout;
    }

    internal static void LimitToWorkArea(Window window, Rect workArea)
    {
        window.MaxWidth = Math.Max(1, workArea.Width - 32);
        window.MaxHeight = Math.Max(1, workArea.Height - 32);
        window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth);
        window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
        if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, window.MaxWidth);
        if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, window.MaxHeight);
    }

    private static void KeepOnScreen(Window window, Rect workArea)
    {
        window.Left = Math.Clamp(window.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - window.ActualWidth));
        window.Top = Math.Clamp(window.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - window.ActualHeight));
    }

    private static Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window.Owner ?? window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return SystemParameters.WorkArea;
        // Win32 work-area coordinates are physical pixels; WPF dimensions are DIPs.
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1 : dpi / 96d;
        return new Rect(info.Work.Left / scale, info.Work.Top / scale,
            (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
}
