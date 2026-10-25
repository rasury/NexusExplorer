using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NexusExplorer.Tests;
public class CategoryRowSpacingTests
{
    [Fact]
    public async Task SiblingAndNestedRowsKeepGapDuringPress()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var style = (Style)Application.Current.FindResource("PrismTreeItem");
            var first = new TreeViewItem { Header = "test3", Style = style };
            var child = new TreeViewItem { Header = "child", Style = style };
            var second = new TreeViewItem { Header = "test4", Style = style };
            first.Items.Add(child);
            var tree = new TreeView(); tree.Items.Add(first); tree.Items.Add(second);
            var window = new Window { Content = tree, Width = 320, Height = 400, ShowInTaskbar = false };
            try
            {
                window.Show(); tree.UpdateLayout();
                Border Row(TreeViewItem item) => (Border)item.Template.FindName("Row", item);
                double Gap(TreeViewItem a, TreeViewItem b) => Row(b).TranslatePoint(new Point(), tree).Y
                    - Row(a).TranslatePoint(new Point(0, Row(a).ActualHeight), tree).Y;
                Assert.Equal(4, Gap(first, second), 1);
                var row = Row(first);
                row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                await Task.Delay(180);
                Assert.InRange(Gap(first, second), 2.4, 4);
                row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                await Task.Delay(180);
                first.IsExpanded = true; tree.UpdateLayout();
                Assert.Equal(4, Gap(first, child), 1);
                Assert.Equal(4, Gap(child, second), 1);
            }
            finally { window.Close(); }
        });
    }
}
