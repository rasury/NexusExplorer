using System.Windows;
using System.Windows.Media;

namespace NexusExplorer.Views;

/// <summary>Static, resolution-independent backdrop; brushes are rebuilt only on theme changes.</summary>
public sealed class PrismBackground : FrameworkElement
{
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(
        nameof(IsDark), typeof(bool), typeof(PrismBackground),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPaletteChanged));
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }
    private Brush _base = null!, _cool = null!, _warm = null!, _light = null!, _orb = null!;
    private Pen _rim = null!;
    public PrismBackground() { IsHitTestVisible = false; ClipToBounds = true; BuildPalette(); }
    private static void OnPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PrismBackground)d).BuildPalette();
    private void BuildPalette()
    {
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
        Brush Radial(string color, Point origin, double radiusX, double radiusY)
        {
            var start = C(color); var end = Color.FromArgb(0, start.R, start.G, start.B);
            return Freeze(new RadialGradientBrush(start, end) { Center = origin, GradientOrigin = origin, RadiusX = radiusX, RadiusY = radiusY });
        }
        _base = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(C(IsDark ? "#122735" : "#D6E8ED"), 0),
            new(C(IsDark ? "#1B3440" : "#E4EEF0"), .54),
            new(C(IsDark ? "#153C46" : "#B8D7DE"), 1)
        }, new Point(0, 0), new Point(1, .84)));
        _cool = Radial(IsDark ? "#366477" : "#AFD1DE", new Point(0, .4), .65, .65);
        _warm = Radial(IsDark ? "#66504C" : "#FEE6D5", new Point(.7, 1.05), .55, .55);
        _light = Radial(IsDark ? "#245E8996" : "#9EFFFFFF", new Point(.5, 0), .6, .6);
        _orb = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(C(IsDark ? "#287DC1D1" : "#70FFFFFF"), 0),
            new(C(IsDark ? "#08347B92" : "#10D4F4F9"), .65),
            new(C(IsDark ? "#30618B9B" : "#386D9BAC"), .9),
            new(C(IsDark ? "#5086BAC6" : "#90FFFFFF"), 1)
        }) { Center = new Point(.5, .5), GradientOrigin = new Point(.32, .22), RadiusX = .52, RadiusY = .52 });
        _rim = new Pen(new SolidColorBrush(C(IsDark ? "#448FD0DC" : "#90FFFFFF")), 1); _rim.Freeze();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(_base, null, bounds);
        dc.DrawRectangle(_cool, null, bounds);
        dc.DrawRectangle(_warm, null, bounds);
        dc.DrawRectangle(_light, null, bounds);
        var unit = Math.Min(ActualWidth, ActualHeight);
        dc.DrawEllipse(_orb, _rim, new Point(ActualWidth * .12, ActualHeight * .23), unit * .13, unit * .13);
        dc.DrawEllipse(_orb, _rim, new Point(ActualWidth * .84, ActualHeight * .76), unit * .18, unit * .18);
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    { base.OnRenderSizeChanged(sizeInfo); InvalidateVisual(); }
}
