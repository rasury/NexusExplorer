using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NexusExplorer.Views;

/// <summary>One full-resolution wallpaper blur reused by main-window cards.</summary>
public sealed class WallpaperBlurHost : Grid
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(FrameworkElement), typeof(WallpaperBlurHost),
        new PropertyMetadata(null, (d, e) => { var host = (WallpaperBlurHost)d; if (!host.IsResizing) host._source.Visual = e.NewValue as Visual; }));
    public FrameworkElement? Source { get => (FrameworkElement?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    private readonly VisualBrush _source = new()
    {
        AutoLayoutContent = false, ViewboxUnits = BrushMappingMode.Absolute,
        ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.FlipXY, Stretch = Stretch.Fill
    };
    private readonly RectangleGeometry _bounds = new();
    internal BitmapCacheBrush SharedBrush { get; }
    internal bool IsResizing { get; private set; }
    internal event Action? AvailabilityChanged;
    private HwndSource? _windowSource;
    private DispatcherOperation? _resume;
    private bool _interactiveResize;
    public WallpaperBlurHost()
    {
        IsHitTestVisible = false;
        // The brush ignores the root clip; a child clip bounds the cached content.
        Clip = new RectangleGeometry(Rect.Empty);
        var crop = new Grid { Clip = _bounds };
        crop.Children.Add(new Border
        {
            Margin = new Thickness(-18), Background = _source,
            Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance }
        });
        Children.Add(crop);
        SharedBrush = new BitmapCacheBrush(this)
        {
            AutoLayoutContent = false,
            BitmapCache = new BitmapCache { RenderAtScale = 1, EnableClearType = false }
        };
        Loaded += (_, _) =>
        {
            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            _windowSource?.AddHook(WindowMessage);
        };
        Unloaded += (_, _) =>
        {
            _windowSource?.RemoveHook(WindowMessage); _windowSource = null;
            _resume?.Abort(); _resume = null; _interactiveResize = false;
            SetResizing(false);
        };
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0231) // WM_ENTERSIZEMOVE: interactive sizing/moving loop
        {
            _interactiveResize = true; _resume?.Abort(); SetResizing(true);
        }
        else if (message == 0x0232) // WM_EXITSIZEMOVE
        {
            _interactiveResize = false; ResumeAfterLayout();
        }
        else if (message == 0x0112 && ((wParam.ToInt64() & 0xfff0) is 0xf030 or 0xf120))
        {
            SetResizing(true); ResumeAfterLayout(); // SC_MAXIMIZE / SC_RESTORE, before native sizing
        }
        return IntPtr.Zero;
    }
    internal void SetResizing(bool value)
    {
        if (IsResizing == value) return;
        IsResizing = value;
        // Disconnect the live source too: an unused cache must not track every wallpaper resize.
        _source.Visual = value ? null : Source;
        AvailabilityChanged?.Invoke();
    }
    private void ResumeAfterLayout()
    {
        _resume?.Abort();
        _resume = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _resume = null;
            if (!_interactiveResize && IsLoaded) SetResizing(false);
        }));
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        // Also covers programmatic sizing and DPI changes without a system command.
        SetResizing(true);
        if (!_interactiveResize) ResumeAfterLayout();
        var size = info.NewSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        _bounds.Rect = new Rect(size);
        _source.Viewbox = new Rect(size);
        // Mirror outside the scene rather than sample transparent edges.
        _source.Viewport = new Rect(new Point(18, 18), size);
    }
    internal static Matrix MapUnitSurface(Visual scene, Visual destination, Size size)
    {
        var mapping = scene.TransformToVisual(destination);
        var origin = mapping.Transform(new Point());
        var x = mapping.Transform(new Point(size.Width, 0));
        var y = mapping.Transform(new Point(0, size.Height));
        return new Matrix(x.X - origin.X, x.Y - origin.Y,
            y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
    }
}
