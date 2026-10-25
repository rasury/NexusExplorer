using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NexusExplorer.Models;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class DragWheelScrollerTests
{
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static ScrollViewer ActualTreeViewport()
    {
        var panel = new CategoryFilePanel();
        var tree = (TreeView)panel.FindName("CategoryTree");
        tree.ItemsSource = Enumerable.Range(1, 100).Select(i => new Category { Id = i, Name = "分类" + i }).ToArray();
        panel.Measure(new Size(340, 500)); panel.Arrange(new Rect(0, 0, 340, 500)); panel.UpdateLayout();
        var viewer = FindChild<ScrollViewer>(tree); Assert.NotNull(viewer);
        Assert.True(viewer.ScrollableHeight > 0); return viewer;
    }
    private static async Task Flush(ScrollViewer viewer)
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        viewer.UpdateLayout(); await Task.Delay(30);
    }
    [Fact]
    public async Task DragWheelScrollsActualTreeInBothDirectionsAndAccumulatesSmallDeltas()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var viewer = ActualTreeViewport(); var point = new Point(20, 20);
            using var scroller = new DragWheelScroller(); scroller.Start(viewer);
            Assert.True(scroller.HasNativeHook);
            Assert.True(scroller.TryQueueWheel(-60, point)); await Flush(viewer);
            Assert.Equal(0, viewer.VerticalOffset);
            Assert.True(scroller.TryQueueWheel(-60, point)); await Flush(viewer);
            if (SystemParameters.WheelScrollLines != 0) Assert.True(viewer.VerticalOffset > 0);
            Assert.True(scroller.TryQueueWheel(120, point)); await Flush(viewer);
            Assert.Equal(0, viewer.VerticalOffset);
        });
    }
    [Fact]
    public async Task LeavingDroppingAndDisposingDoNotScrollOtherAreasOrRunQueuedWork()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var viewer = ActualTreeViewport();
            using var scroller = new DragWheelScroller(); scroller.Start(viewer);
            Assert.False(scroller.TryQueueWheel(-120, new Point(-2, 10)));
            Assert.False(scroller.TryQueueWheel(-120, new Point(10, viewer.ActualHeight + 2)));
            Assert.False(scroller.TryQueueWheel(0, new Point(10, 10)));
            Assert.True(scroller.TryQueueWheel(-120, new Point(10, 10)));
            scroller.Dispose(); Assert.False(scroller.IsActive); Assert.False(scroller.HasNativeHook);
            Assert.False(scroller.TryQueueWheel(-120, new Point(10, 10)));
            scroller.Start(viewer); // a new drag must not receive wheel work from a completed drag
            await Flush(viewer); Assert.Equal(0, viewer.VerticalOffset);
            scroller.Dispose(); Assert.False(scroller.HasNativeHook);
        });
    }
}
