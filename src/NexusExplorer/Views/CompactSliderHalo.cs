using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace NexusExplorer.Views;

/// <summary>Keep the Material thumb/template; reduce only its outlying halo and hit area.</summary>
public static class CompactSliderHalo
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(CompactSliderHalo), new PropertyMetadata(false, Changed));
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Slider slider) return;
        if ((bool)args.NewValue) { slider.Loaded += Loaded; if (slider.IsLoaded) Apply(slider); }
        else slider.Loaded -= Loaded;
    }
    private static void Loaded(object sender, RoutedEventArgs args) => Apply((Slider)sender);
    internal static void Apply(Slider slider)
    {
        slider.ApplyTemplate();
        if (slider.Template.FindName("PART_Track", slider) is not Track { Thumb: { } thumb }) return;
        thumb.ApplyTemplate();
        if (thumb.Template.FindName("grip", thumb) is not Ellipse grip) return;
        var diameter = Math.Max(grip.ActualWidth, grip.ActualHeight);
        if (diameter <= 0) diameter = 20; // Fixed MaterialDesignThemes 5.3.2 continuous thumb.
        // Framework halo is 48dp, with a 20dp solid grip. Outside extension: 14 → 5.6dp.
        var haloDiameter = diameter + (48 - diameter) * .4;
        foreach (var name in new[] { "halo", "focusedHalo" })
            if (thumb.Template.FindName(name, thumb) is Ellipse halo)
            {
                halo.SetCurrentValue(FrameworkElement.WidthProperty, haloDiameter);
                halo.SetCurrentValue(FrameworkElement.HeightProperty, haloDiameter);
                halo.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(-haloDiameter / 2));
            }
    }
}
