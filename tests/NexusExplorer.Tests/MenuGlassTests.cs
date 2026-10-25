using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class MenuGlassTests
{
    [Fact]
    public async Task MenuMaterialKeepsCommandsChecksSubmenusAndRoundedRows()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var owner = new Button();
            var window = new Window { Content = owner, Width = 320, Height = 240, ShowInTaskbar = false };
            var menu = new ContextMenu { PlacementTarget = owner };
            var action = new MenuItem { Header = "测试", IsCheckable = true };
            var parent = new MenuItem { Header = "子菜单" };
            parent.Items.Add(new MenuItem { Header = "子项" });
            menu.Items.Add(action); menu.Items.Add(new Separator()); menu.Items.Add(parent);
            var invoked = false; action.Click += (_, _) => invoked = true;
            try
            {
                window.Show(); menu.IsOpen = true;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.NotNull(Find<PrismCard>(menu));
                var row = (Border)action.Template.FindName("Row", action);
                Assert.Equal(new CornerRadius(10), row.CornerRadius);
                action.IsChecked = true;
                Assert.Equal(Visibility.Visible, ((TextBlock)action.Template.FindName("Check", action)).Visibility);
                action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.True(invoked);
                menu.IsOpen = true; parent.IsSubmenuOpen = true;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var popup = (Popup)parent.Template.FindName("PART_Popup", parent);
                Assert.True(popup.IsOpen); Assert.IsType<PrismCard>(popup.Child);
                parent.IsSubmenuOpen = false;
            }
            finally { menu.IsOpen = false; window.Close(); }
        });
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
