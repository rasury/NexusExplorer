using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class DrawerVirtualizationTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(1000)]
    public async Task DrawerKeepsRowsVirtualizedAndPreservesStateAcrossToggleAndResize(int count)
    {
        using var host = new TestHost();
        var path = host.CreateTestFile("sample.mp3");
        await WpfTestHost.RunAsync(async () =>
        {
            // Load the production layout without startup, database restore, or exit handlers.
            var sourcePath = Path.GetFullPath("../../../../../src/NexusExplorer/Views/MainWindow.xaml", AppContext.BaseDirectory);
            var xaml = Regex.Replace(File.ReadAllText(sourcePath), "\\s+x:Class=\"[^\"]+\"|\\s+\\w+=\"On\\w+\"", "");
            xaml = xaml.Replace("clr-namespace:NexusExplorer.Views\"", "clr-namespace:NexusExplorer.Views;assembly=NexusExplorer\"");
            var window = (Window)XamlReader.Parse(xaml);
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -5000; window.Top = -5000;
            var drawer = (DrawerHost)window.FindName("NavigationDrawer");
            var panel = (CategoryFilePanel)window.FindName("LeftPanel");
            var list = (ListBox)panel.FindName("FileListBox");
            var player = (PlayerPanel)window.FindName("PlayerArea");
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var records = new ObservableCollection<FileItem>(Enumerable.Range(0, count)
                .Select(i => new FileItem { Id = i + 1, FileName = $"sample-{i:D4}.mp3", AbsolutePath = path }));
            main.CurrentFiles = records; list.ItemsSource = records; player.Initialize(main);
            var maxRealized = 0;
            list.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (list.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                    maxRealized = Math.Max(maxRealized, Enumerable.Range(0, count)
                        .Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null));
            };
            var report = new List<string>();
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                report.Add($"count={count} initialHeight={panel.Height:F1} viewport={drawer.ActualHeight:F1} maxRows={maxRealized}");
                Assert.True(ScrollViewer.GetCanContentScroll(list));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                var viewer = Descendants<ScrollViewer>(list).First();
                var playing = records[count / 2];
                await main.SelectFileAsync(playing);
                list.SelectedItems.Add(playing); list.SelectedItems.Add(records[count / 2 + 1]);
                list.ScrollIntoView(playing); window.UpdateLayout();
                var selected = list.SelectedItems.Cast<FileItem>().Select(f => f.Id).Order().ToArray();
                var queue = main.Session.Queue.ToArray();
                var itemsSource = list.ItemsSource;
                var offset = viewer.VerticalOffset;
                var originalHeight = panel.ActualHeight;
                var button = Descendants<Button>(window).Single(b => b.ToolTip is string text && text == "显示或隐藏分类面板");
                button.Click += (_, _) => drawer.IsLeftDrawerOpen = !drawer.IsLeftDrawerOpen;
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    foreach (var open in new[] { false, true })
                    {
                        var timing = Stopwatch.StartNew();
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var surface = (FrameworkElement)drawer.Template.FindName("PART_LeftDrawer", drawer);
                        await BoundedDialogTests.Until(() => Math.Abs(surface.Margin.Left - (open ? 0 : -surface.ActualWidth)) < .5);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        report.Add($"count={count} cycle={cycle} open={open} settledMs={timing.Elapsed.TotalMilliseconds:F1} maxRows={maxRealized}");
                        Assert.InRange(maxRealized, 1, Math.Min(count, 50));
                        Assert.InRange(Math.Abs(panel.ActualHeight - drawer.ActualHeight), 0, 1);
                        Assert.Same(itemsSource, list.ItemsSource);
                        Assert.Equal(selected, list.SelectedItems.Cast<FileItem>().Select(f => f.Id).Order().ToArray());
                        Assert.Equal(offset, viewer.VerticalOffset);
                        Assert.Same(playing, main.CurrentFile); Assert.True(playing.IsCurrent);
                        Assert.True(engine.Snapshot.IsPlaying); Assert.Single(engine.Played);
                        Assert.Equal(queue, main.Session.Queue.ToArray());
                    }
                    if (cycle == 0)
                    {
                        window.Height = 720; window.Width = 960;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        Assert.True(panel.ActualHeight < originalHeight);
                        Assert.InRange(Math.Abs(panel.ActualHeight - drawer.ActualHeight), 0, 1);
                        offset = viewer.VerticalOffset;
                    }
                }
            }
            finally
            {
                player.Detach(); window.Close();
                var directory = Path.GetFullPath("../../../../../artifacts/drawer-analysis", AppContext.BaseDirectory);
                Directory.CreateDirectory(directory); File.WriteAllLines(Path.Combine(directory, $"fixed-{count}.txt"), report);
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
