using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace NexusExplorer.Views;
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, Changed));
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement view) return;
        if ((bool)e.NewValue) { view.PreviewMouseLeftButtonDown += Down; view.PreviewMouseLeftButtonUp += Up; view.MouseLeave += Leave; view.Unloaded += Unload; }
        else { view.PreviewMouseLeftButtonDown -= Down; view.PreviewMouseLeftButtonUp -= Up; view.MouseLeave -= Leave; view.Unloaded -= Unload; Unload(view, new RoutedEventArgs()); }
    }
    private static readonly DependencyProperty PressTransformProperty = DependencyProperty.RegisterAttached("PressTransform", typeof(TranslateTransform), typeof(PressFeedback));
    private static void Unload(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).GetValue(PressTransformProperty) is TranslateTransform transform)
        { PrismMotion.Stop(transform, TranslateTransform.YProperty); transform.Y = 0; }
    }
    private static void Animate(object sender, double target)
    {
        var view = (FrameworkElement)sender;
        if (view.GetValue(PressTransformProperty) is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(view.RenderTransform); group.Children.Add(transform);
            view.RenderTransform = group; view.SetValue(PressTransformProperty, transform);
        }
        PrismMotion.Animate(transform, TranslateTransform.YProperty, target, MotionProfiles.Press);
    }
    private static void Down(object sender, MouseButtonEventArgs e) => Animate(sender, MotionProfiles.PressDepth);
    private static void Up(object sender, MouseButtonEventArgs e) => Animate(sender, 0);
    private static void Leave(object sender, MouseEventArgs e) => Animate(sender, 0);
}
