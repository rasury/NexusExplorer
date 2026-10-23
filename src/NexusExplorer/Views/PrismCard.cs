using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NexusExplorer.Views;

/// <summary>Samples only the wallpaper, never video, text or the card itself.</summary>
public sealed class PrismCard : ContentControl
{
    public static readonly DependencyProperty IsPlainProperty = DependencyProperty.Register(nameof(IsPlain), typeof(bool), typeof(PrismCard), new PropertyMetadata(false));
    public bool IsPlain { get => (bool)GetValue(IsPlainProperty); set => SetValue(IsPlainProperty, value); }
    internal FrameworkElement? BackdropScene { get; set; }
    private VisualBrush? _dialogBackdrop;
    internal void PrepareDialogBackdrop(FrameworkElement? scene)
    {
        if (scene is null) return;
        // Keep a stable view of the wallpaper, with live compositor updates.
        // No repeated RenderTargetBitmap allocation while dragging opacity.
        _dialogBackdrop = new VisualBrush(scene) { Stretch = Stretch.UniformToFill };
        if (_backdrop is Border border) border.Background = _dialogBackdrop;
    }
    private FrameworkElement? _backdrop;
    private Grid? _clip;
    private VisualBrush? _sample;
    private Rect _last;
    private FrameworkElement? _scene;
    private readonly RectangleGeometry _geometry = new();
    // One dispatcher operation for all dirty cards, rather than one allocation per card/layout.
    private static class BackdropUpdates
    {
        private static HashSet<PrismCard> _pending = new();
        private static HashSet<PrismCard> _processing = new();
        private static DispatcherOperation? _operation;
        private static readonly Action FlushAction = Flush;
        internal static void Add(PrismCard card)
        {
            _pending.Add(card);
            _operation ??= card.Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushAction);
        }
        internal static void Remove(PrismCard card) => _pending.Remove(card);
        private static void Flush()
        {
            _operation = null;
            (_pending, _processing) = (_processing, _pending);
            foreach (var card in _processing) if (card.IsLoaded) card.UpdateSample();
            _processing.Clear();
        }
    }
    private void QueueSample(object? sender, EventArgs e)
    { if (IsLoaded) BackdropUpdates.Add(this); }
    public PrismCard()
    {
        SetResourceReference(StyleProperty, "PrismCardStyle");
        Loaded += (_, _) => { LayoutUpdated += QueueSample; QueueSample(null, EventArgs.Empty); };
        Unloaded += (_, _) => { LayoutUpdated -= QueueSample; BackdropUpdates.Remove(this); _scene = null; if (_sample is not null) _sample.Visual = null; _sample = null; };
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backdrop = GetTemplateChild("PART_Backdrop") as FrameworkElement;
        if (_backdrop is Border border && _dialogBackdrop is not null) border.Background = _dialogBackdrop;
        _clip = GetTemplateChild("PART_Clip") as Grid;
        _sample = null;
        if (_clip is not null) _clip.Clip = _geometry;
        UpdateClip();
        QueueSample(null, EventArgs.Empty);
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Hit testing must see the new clip immediately, before a popup accepts input.
        UpdateClip();
    }
    private void UpdateClip()
    {
        var clip = new Rect(0, 0, ActualWidth, ActualHeight);
        var radius = IsPlain ? 0 : 24;
        if (_geometry.Rect != clip) _geometry.Rect = clip;
        if (_geometry.RadiusX != radius) { _geometry.RadiusX = radius; _geometry.RadiusY = radius; }
    }
    private void UpdateSample()
    {
        if (_clip is null || _backdrop is not Border backdrop || ActualWidth <= 0 || ActualHeight <= 0) return;
        UpdateClip();
        if (IsPlain || _dialogBackdrop is not null) return;
        _scene ??= BackdropScene ?? Window.GetWindow(this)?.FindName("WallpaperScene") as FrameworkElement;
        if (_scene is null || PresentationSource.FromVisual(_scene) is null || PresentationSource.FromVisual(this) is null) return;
        // Main-window cards share a visual tree. Avoid two screen-coordinate conversions per sample.
        var origin = TranslatePoint(new Point(-18, -18), _scene);
        var rect = new Rect(origin, new Size(ActualWidth + 36, ActualHeight + 36));
        if (_sample is null)
        {
            _sample = new VisualBrush(_scene) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            backdrop.Background = _sample; _last = Rect.Empty;
        }
        if (_last != rect) { _last = rect; _sample.Viewbox = rect; }
    }
}
