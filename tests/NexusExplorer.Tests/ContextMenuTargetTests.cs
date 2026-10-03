using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NexusExplorer.Models;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Tests;

public class ContextMenuTargetTests
{
    [Fact]
    public async Task ContextMenuRecordsActualHitAndDragRejectsScrollbarAndExpander()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var panel = new CategoryFilePanel();
            var catA = new Category { Id = 1, Name = "A" }; var catB = new Category { Id = 2, Name = "B" };
            var text = new TextBlock { Text = catB.Name };
            var row = new TreeViewItem { Header = catB }; row.Items.Add(text);
            var tree = new TreeView(); tree.Items.Add(new TreeViewItem { Header = catA, IsSelected = true }); tree.Items.Add(row);
            panel.RecordContextMenuSource(text);
            Assert.Same(catB, panel.GetContextMenuCategory());
            Assert.Same(catB, CategoryFilePanel.HitCategory(text));
            var expander = new ToggleButton(); row.Items.Clear(); row.Items.Add(expander);
            Assert.Null(CategoryFilePanel.HitCategory(expander));

            var file = new FileItem { Id = 77, FileName = "new.mp4" };
            var fileText = new TextBlock(); var fileRow = new ListBoxItem { DataContext = file, Content = fileText };
            Assert.Same(file, CategoryFilePanel.HitFile(fileText));
            var scrollbar = new ScrollBar(); fileRow.Content = scrollbar;
            Assert.Null(CategoryFilePanel.HitFile(scrollbar));
            Assert.True(CategoryFilePanel.IsControlChrome(scrollbar));
            Assert.Equal(SelectionMode.Extended, ((ListBox)panel.FindName("FileListBox")).SelectionMode);
        });
    }

    [Fact]
    public async Task RealConflictCheckboxAppliesResourcesWithoutTargetTypeMismatch()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var checkbox = ConflictDialog.CreateApplyToAllCheckBox();
            checkbox.Measure(new Size(400, 100)); checkbox.Arrange(new Rect(0, 0, 400, 100));
            checkbox.ApplyTemplate();
            Assert.NotNull(checkbox.Foreground);
            Assert.True(checkbox.Style is null || checkbox.Style.TargetType.IsAssignableFrom(typeof(CheckBox)));
        });
    }

    [Fact]
    public async Task ActualPinnedLayoutWrapsInsideNarrowViewport()
    {
        using var host = new TestHost();
        var pinned = new List<Category>();
        for (var i = 0; i < 12; i++)
        {
            var c = await host.Categories.CreateAsync("快捷分类" + i, null);
            await host.Categories.PinAsync(c.Id); pinned.Add(c);
        }
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            await main.Navigation.RefreshPinnedAsync();
            var bar = new NavigationBar(); bar.Initialize(main);
            bar.Measure(new Size(520, 500)); bar.Arrange(new Rect(0, 0, 520, bar.DesiredSize.Height)); bar.UpdateLayout();
            var items = (ItemsControl)bar.FindName("PinnedHost");
            var first = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0);
            var last = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(11);
            Assert.NotNull(first); Assert.NotNull(last);
            Assert.True(last.TranslatePoint(new Point(), items).Y > first.TranslatePoint(new Point(), items).Y);
            Assert.True(last.TranslatePoint(new Point(), items).X + last.ActualWidth <= items.ActualWidth + 1);
            Assert.True(bar.ActualHeight < 340);
        });
    }

    [Fact]
    public async Task ActualImageControlsRemainVisibleAndFitScaleIsFinite()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(() =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new PlayerPanel(); panel.Initialize(main);
            main.Player.Kind = MediaKind.Image;
            // Exercise the production state refresh through the public event.
            main.Player.ResetZoom();
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("ImageButtonsRow")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("ControlsBar")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("MediaButtonsRow")).Visibility);
            Assert.InRange(PlayerPanel.FitScale(4000, 2000, 500, 500), .124, .126);
            Assert.True(double.IsFinite(PlayerPanel.FitScale(4000, 2000, 0, 0)));
            panel.Detach();
        });
    }
}
