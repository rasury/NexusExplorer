using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NexusExplorer.Views;

/// <summary>Samples only the wallpaper, never video, text or the card itself.</summary>
public sealed class PrismCard : ContentControl
{
    internal FrameworkElement? BackdropScene { get; set; }
    private FrameworkElement? _backdrop;
    private Grid? _clip;
    private VisualBrush? _sample;
    private Rect _last;
    public PrismCard()
    {
        SetResourceReference(StyleProperty, "PrismCardStyle");
        Loaded += (_, _) => LayoutUpdated += UpdateSample;
        Unloaded += (_, _) => { LayoutUpdated -= UpdateSample; if (_sample is not null) _sample.Visual = null; _sample = null; };
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backdrop = GetTemplateChild("PART_Backdrop") as FrameworkElement;
        _clip = GetTemplateChild("PART_Clip") as Grid;
        _sample = null;
    }
    private void UpdateSample(object? sender, EventArgs e)
    {
        if (_clip is null || _backdrop is not Border backdrop || ActualWidth <= 0 || ActualHeight <= 0) return;
        var clip = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_clip.Clip is not RectangleGeometry geometry || geometry.Rect != clip)
            _clip.Clip = new RectangleGeometry(clip, 24, 24);
        var window = Window.GetWindow(this);
        var scene = BackdropScene ?? window?.FindName("WallpaperScene") as FrameworkElement;
        if (scene is null || PresentationSource.FromVisual(scene) is null || PresentationSource.FromVisual(this) is null) return;
        var origin = scene.PointFromScreen(PointToScreen(new Point(-18, -18)));
        var rect = new Rect(origin, new Size(ActualWidth + 36, ActualHeight + 36));
        if (_sample is null)
        {
            _sample = new VisualBrush(scene) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            backdrop.Background = _sample; _last = Rect.Empty;
        }
        if (_last != rect) { _last = rect; _sample.Viewbox = rect; }
    }
}
