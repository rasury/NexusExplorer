using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace NexusExplorer.Views;

// Capture WPF chrome once; only the mask animates. Native video HWNDs keep rendering.
internal static class ThemeTransition
{
    internal static bool IsRunning { get; private set; }
    private static Task _active = Task.CompletedTask;
    internal static Task Completion => _active.IsCompleted ? Task.CompletedTask : _active;

    internal static Task RunAsync(Grid root, Grid? dialog, Action apply)
    {
        root.Dispatcher.VerifyAccess();
        if (IsRunning) return _active;
        IsRunning = true;
        return _active = RunCoreAsync(root, dialog, apply);
    }

    private static async Task RunCoreAsync(Grid root, Grid? dialog, Action apply)
    {
        var layers = new List<Layer>();
        var watch = Stopwatch.StartNew();
        try
        {
            if (root.IsVisible && root.ActualWidth > 0 && root.ActualHeight > 0)
            {
                layers.Add(new Layer(root, root));
                if (dialog is { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 })
                    layers.Add(new Layer(dialog, root));
            }
            var captureMs = watch.Elapsed.TotalMilliseconds;
            apply();
            Serilog.Log.Information("主题过渡准备;截图 {CaptureMs:F1} ms;主题应用 {ApplyMs:F1} ms", captureMs, watch.Elapsed.TotalMilliseconds - captureMs);
            await Task.WhenAll(layers.Select(layer => layer.PlayAsync()));
        }
        finally
        {
            foreach (var layer in layers) layer.Dispose();
            IsRunning = false;
        }
    }

    private sealed class Layer : IDisposable
    {
        private readonly Grid _target;
        private readonly Image _image;
        private readonly GradientStop _clear, _opaque;
        private readonly double _distance;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Layer(Grid target, Grid root)
        {
            _target = target;
            var dpi = VisualTreeHelper.GetDpi(target);
            // This short-lived old-theme veil need not render glass effects at screen DPI.
            // The revealed live UI always retains full resolution.
            var scale = Math.Min(dpi.DpiScaleX, MotionProfiles.ThemeSnapshotEdge / Math.Max(target.ActualWidth, target.ActualHeight));
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(target.ActualWidth * scale)),
                Math.Max(1, (int)Math.Ceiling(target.ActualHeight * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(target); bitmap.Freeze();
            var top = (target.PointToScreen(new Point()).Y - root.PointToScreen(new Point()).Y) / VisualTreeHelper.GetDpi(root).DpiScaleY;
            var feather = root.ActualHeight * MotionProfiles.ThemeFeather;
            _clear = new GradientStop(Colors.Transparent, (-feather - top) / target.ActualHeight);
            _opaque = new GradientStop(Colors.Black, -top / target.ActualHeight);
            _distance = (root.ActualHeight + feather) / target.ActualHeight;
            _image = new Image { Source = bitmap, IsHitTestVisible = false, Stretch = Stretch.Fill,
                OpacityMask = new LinearGradientBrush(new GradientStopCollection { _clear, _opaque }, new Point(0, 0), new Point(0, 1)) };
            Panel.SetZIndex(_image, int.MaxValue);
            Grid.SetRowSpan(_image, Math.Max(1, target.RowDefinitions.Count));
            Grid.SetColumnSpan(_image, Math.Max(1, target.ColumnDefinitions.Count));
            target.Children.Add(_image);
            target.Unloaded += OnUnloaded;
            target.SizeChanged += OnSizeChanged;
            target.Dispatcher.ShutdownStarted += OnShutdown;
        }
        internal Task PlayAsync()
        {
            if (!_target.IsLoaded || _target.Dispatcher.HasShutdownStarted) return Task.CompletedTask;
            PrismMotion.Animate(_clear, GradientStop.OffsetProperty, _clear.Offset + _distance, MotionProfiles.Theme);
            PrismMotion.Animate(_opaque, GradientStop.OffsetProperty, _opaque.Offset + _distance, MotionProfiles.Theme,
                () => _done.TrySetResult());
            return _done.Task;
        }
        private void OnUnloaded(object sender, RoutedEventArgs e) => _done.TrySetResult();
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => _done.TrySetResult();
        private void OnShutdown(object? sender, EventArgs e) => _done.TrySetResult();
        public void Dispose()
        {
            _target.Unloaded -= OnUnloaded; _target.SizeChanged -= OnSizeChanged;
            _target.Dispatcher.ShutdownStarted -= OnShutdown;
            PrismMotion.StopAll(_clear); PrismMotion.StopAll(_opaque);
            _target.Children.Remove(_image); _image.Source = null; _image.OpacityMask = null;
        }
    }
}
