using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace NexusExplorer.Views;
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, Changed));
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement view) return;
        if ((bool)e.NewValue) { view.PreviewMouseLeftButtonDown += Down; view.PreviewMouseLeftButtonUp += Up; view.MouseLeave += Leave; }
        else { view.PreviewMouseLeftButtonDown -= Down; view.PreviewMouseLeftButtonUp -= Up; view.MouseLeave -= Leave; }
    }
    private static void Animate(object sender, double target)
    {
        var view = (FrameworkElement)sender;
        if (view.RenderTransform is not TranslateTransform transform) view.RenderTransform = transform = new TranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private static void Down(object sender, MouseButtonEventArgs e) => Animate(sender, 1.5);
    private static void Up(object sender, MouseButtonEventArgs e) => Animate(sender, 0);
    private static void Leave(object sender, MouseEventArgs e) => Animate(sender, 0);
}
