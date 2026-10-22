using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NexusExplorer.Views;

/// <summary>Samples only the wallpaper, never video, text or the card itself.</summary>
public sealed class PrismCard : ContentControl
{
    public static readonly DependencyProperty IsPlainProperty = DependencyProperty.Register(nameof(IsPlain), typeof(bool), typeof(PrismCard), new PropertyMetadata(false));
    public bool IsPlain { get => (bool)GetValue(IsPlainProperty); set => SetValue(IsPlainProperty, value); }
    internal FrameworkElement? BackdropScene { get; set; }
    private ImageBrush? _dialogBackdrop;
    internal void PrepareDialogBackdrop(FrameworkElement? scene)
    {
        if (scene is null || scene.ActualWidth <= 0 || scene.ActualHeight <= 0) return;
        var scale = Math.Min(1, 900 / scene.ActualWidth);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1, (int)(scene.ActualWidth * scale)), Math.Max(1, (int)(scene.ActualHeight * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(scene); bitmap.Freeze();
        _dialogBackdrop = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        if (_backdrop is Border border) border.Background = _dialogBackdrop;
    }
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
        if (_backdrop is Border border && _dialogBackdrop is not null) border.Background = _dialogBackdrop;
        _clip = GetTemplateChild("PART_Clip") as Grid;
        _sample = null;
    }
    private void UpdateSample(object? sender, EventArgs e)
    {
        if (_clip is null || _backdrop is not Border backdrop || ActualWidth <= 0 || ActualHeight <= 0) return;
        var clip = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_clip.Clip is not RectangleGeometry geometry || geometry.Rect != clip || geometry.RadiusX != (IsPlain ? 0 : 24))
            _clip.Clip = new RectangleGeometry(clip, IsPlain ? 0 : 24, IsPlain ? 0 : 24);
        if (IsPlain) return;
        if (_dialogBackdrop is not null) return;
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
