using System.Windows;
using System.Windows.Controls;

namespace NexusExplorer.Views;

/// <summary>Connects detached menu popup surfaces to the existing wallpaper material.</summary>
public static class MenuGlass
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(MenuGlass), new PropertyMetadata(false, Changed));
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not PrismCard card) return;
        if ((bool)e.NewValue) card.Loaded += Loaded;
        else card.Loaded -= Loaded;
    }

    private static void Loaded(object sender, RoutedEventArgs e)
    {
        var card = (PrismCard)sender;
        // Popup HWNDs are not descendants of the main window. Do not use TranslatePoint
        // across them or capture screenshots on every menu open.
        var scene = Application.Current.MainWindow?.FindName("WallpaperScene") as FrameworkElement;
        card.PrepareDialogBackdrop(scene);
        card.MaxHeight = Math.Max(80, SystemParameters.WorkArea.Height - 32);
    }
}
