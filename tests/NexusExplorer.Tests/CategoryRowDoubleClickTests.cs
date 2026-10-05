using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class CategoryRowDoubleClickTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("icon")]
    [InlineData("blank")]
    public async Task ActualCategoryRowDoubleClickOpensFilesFromEveryHeaderRegion(string region)
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("file.txt"), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new CategoryFilePanel(); panel.Initialize(main, host.RecycleBin);
            await main.RefreshTreeAsync();
            var window = new Window { Content = panel, Width = 500, Height = 900, ShowActivated = false, ShowInTaskbar = false, Left = -5000, Top = -5000 };
            try
            {
                window.Show(); window.UpdateLayout();
                var tree = (TreeView)panel.FindName("CategoryTree");
                var item = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                var border = (MaterialDesignThemes.Wpf.Ripple)item.Template.FindName("Ripple", item);
                Assert.True(border.ActualWidth > 350);
                var source = region switch
                {
                    "name" => (DependencyObject)Descendants<TextBlock>(border).Single(),
                    "icon" => Descendants<System.Windows.Shapes.Path>(border).Single(),
                    _ => (DependencyObject)border.InputHitTest(new Point(border.ActualWidth - 5, border.ActualHeight / 2))!
                };
                Assert.Equal(category.Id, CategoryFilePanel.HitCategory(source)?.Id);
                Assert.Null(main.CurrentCategory);
                var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                main.FileListChanged += () => updated.TrySetResult();
                tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = source });
                await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(category.Id, main.CurrentCategory?.Id);
                Assert.Equal(file.Id, Assert.Single(main.CurrentFiles).Id);
                await panel.PrepareForCloseAsync();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task SelectionExpanderAndScrollbarDoNotOpenCategoryFiles()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        await host.Categories.CreateAsync("child", category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new CategoryFilePanel(); panel.Initialize(main, host.RecycleBin);
            await main.RefreshTreeAsync();
            var window = new Window { Content = panel, Width = 500, Height = 900, ShowActivated = false, ShowInTaskbar = false, Left = -5000, Top = -5000 };
            try
            {
                window.Show(); window.UpdateLayout();
                var tree = (TreeView)panel.FindName("CategoryTree");
                var item = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                item.IsSelected = true; Assert.Null(main.CurrentCategory);
                var expander = (System.Windows.Controls.Primitives.ToggleButton)item.Template.FindName("Expander", item);
                expander.IsChecked = true;
                tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = expander });
                Assert.True(item.IsExpanded); Assert.Null(main.CurrentCategory);
                var scrollbar = Descendants<System.Windows.Controls.Primitives.ScrollBar>(tree).First();
                tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = scrollbar });
                Assert.Null(main.CurrentCategory);
                var border = (MaterialDesignThemes.Wpf.Ripple)item.Template.FindName("Ripple", item);
                tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = border });
                Assert.Null(main.CurrentCategory);
                await panel.PrepareForCloseAsync();
            }
            finally { window.Close(); }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
