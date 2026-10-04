using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class PinnedMultiSelectionTests
{
    private static async Task<List<Category>> CreatePins(TestHost host, int count = 5)
    {
        var pins = new List<Category>();
        for (var i = 0; i < count; i++)
        {
            var category = await host.Categories.CreateAsync("快捷" + i, null);
            await host.Categories.PinAsync(category.Id); pins.Add(category);
        }
        return pins;
    }

    private static async Task<(MainViewModel Main, NavigationBar Bar)> CreateBar(TestHost host)
    {
        var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
        await main.Navigation.RefreshPinnedAsync();
        var bar = new NavigationBar(); bar.Initialize(main);
        bar.Measure(new Size(520, 500)); bar.Arrange(new Rect(0, 0, 520, bar.DesiredSize.Height)); bar.UpdateLayout();
        return (main, bar);
    }

    private static Button PinButton(NavigationBar bar, int index)
    {
        var host = (ItemsControl)bar.FindName("PinnedHost");
        var presenter = (ContentPresenter)host.ItemContainerGenerator.ContainerFromIndex(index);
        presenter.ApplyTemplate(); return (Button)VisualTreeHelper.GetChild(presenter, 0);
    }

    private static void Selected(NavigationViewModel vm, params int[] ids)
    {
        Assert.Equal(ids.Order(), vm.SelectedPinnedIds.Order());
        Assert.Equal(ids.Length, vm.SelectedPinnedCount);
        Assert.Equal(ids.Order(), vm.PinnedCategories.Where(c => c.IsSelected).Select(c => c.Id).Order());
    }

    [Fact]
    public async Task CtrlTogglesPinsAndPlainClickRestoresSingleClassificationTarget()
    {
        using var host = new TestHost(); var pins = await CreatePins(host);
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            await vm.OnCurrentFileChangedAsync(pins[4]);
            await bar.HandlePinnedClickAsync(pins[0], ModifierKeys.None); Selected(vm, pins[0].Id);
            var confirm = (Button)bar.FindName("ConfirmButton"); Assert.True(confirm.IsEnabled);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Control); Selected(vm, pins[0].Id, pins[2].Id);
            Assert.Null(vm.SelectedCategory); Assert.False(confirm.IsEnabled);
            await Dispatcher.Yield(DispatcherPriority.DataBind); bar.UpdateLayout();
            Assert.Equal(((SolidColorBrush)PinButton(bar, 0).Background).Color, ((SolidColorBrush)PinButton(bar, 2).Background).Color);
            Assert.NotEqual(((SolidColorBrush)PinButton(bar, 0).Background).Color, ((SolidColorBrush)PinButton(bar, 1).Background).Color);
            await bar.HandlePinnedClickAsync(pins[0], ModifierKeys.Control); Selected(vm, pins[2].Id);
            Assert.Equal(pins[2].Id, vm.SelectedCategory?.Id);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Control); Selected(vm);
            Assert.Null(vm.SelectedCategory); Assert.False(confirm.IsEnabled);
            await bar.HandlePinnedClickAsync(pins[1], ModifierKeys.None); Selected(vm, pins[1].Id);
            Assert.Equal(pins[1].Id, vm.SelectedCategory?.Id); Assert.True(confirm.IsEnabled);
        });
    }

    [Fact]
    public async Task ShiftSelectsAcrossWrappedRowsAndCtrlShiftAddsRange()
    {
        using var host = new TestHost(); var pins = await CreatePins(host, 12);
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            Assert.True(PinButton(bar, 8).TranslatePoint(new Point(), bar).Y > PinButton(bar, 1).TranslatePoint(new Point(), bar).Y);
            await bar.HandlePinnedClickAsync(pins[1], ModifierKeys.None);
            await bar.HandlePinnedClickAsync(pins[8], ModifierKeys.Shift);
            Selected(vm, pins.Skip(1).Take(8).Select(c => c.Id).ToArray());
            await bar.HandlePinnedClickAsync(pins[4], ModifierKeys.Shift);
            Selected(vm, pins.Skip(1).Take(4).Select(c => c.Id).ToArray());
            await bar.HandlePinnedClickAsync(pins[8], ModifierKeys.Control);
            await bar.HandlePinnedClickAsync(pins[6], ModifierKeys.Control | ModifierKeys.Shift);
            Selected(vm, pins.Skip(1).Take(4).Concat(pins.Skip(6).Take(3)).Select(c => c.Id).ToArray());
        });
    }

    [Fact]
    public async Task RealContextMenuUnpinsEntireSelectedGroupAndPreservesFilesAndOtherPins()
    {
        using var host = new TestHost(); var pins = await CreatePins(host);
        var path = Path.Combine(pins[0].PhysicalPath, "keep.txt"); File.WriteAllText(path, "KEEP");
        var file = await host.Files.AddAsync(path, pins[0].Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            await bar.HandlePinnedClickAsync(pins[0], ModifierKeys.None);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Control);
            var button = PinButton(bar, 0); await bar.PreparePinnedContextAsync(button);
            Selected(vm, pins[0].Id, pins[2].Id);
            var menu = button.ContextMenu; menu.PlacementTarget = button;
            var action = (MenuItem)menu.Items[0]; Assert.Contains("2", (string)action.Header);
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PinnedCategories)) refreshed.TrySetResult(); };
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { pins[1].Id, pins[3].Id, pins[4].Id }, (await new CategoryService(host.DbFactory).GetPinnedAsync()).Select(c => c.Id));
            Selected(vm); Assert.Equal(5, (await host.Categories.GetChildrenAsync(null)).Count);
            Assert.Equal(path, (await host.Files.GetByIdAsync(file.Id))?.AbsolutePath);
            Assert.Equal("KEEP", File.ReadAllText(path)); Assert.All(pins, c => Assert.True(Directory.Exists(c.PhysicalPath)));
        });
    }

    [Fact]
    public async Task RightClickOutsideSelectionTargetsOnlyThatPin()
    {
        using var host = new TestHost(); var pins = await CreatePins(host);
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            await bar.HandlePinnedClickAsync(pins[0], ModifierKeys.None);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Control);
            await bar.PreparePinnedContextAsync(PinButton(bar, 3)); Selected(vm, pins[3].Id);
            await vm.UnpinSelectedAsync(pins[3].Id);
            Assert.Equal(pins.Where((_, index) => index != 3).Select(c => c.Id), (await host.Categories.GetPinnedAsync()).Select(c => c.Id));
        });
    }

    [Fact]
    public async Task ReorderRefreshPreservesSelectedIdsAndShiftAnchorUsesCurrentOrder()
    {
        using var host = new TestHost(); var pins = await CreatePins(host);
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            await bar.HandlePinnedClickAsync(pins[1], ModifierKeys.None);
            await bar.HandlePinnedClickAsync(pins[3], ModifierKeys.Control);
            await host.Categories.ReorderPinnedAsync(pins[3].Id, 0); await vm.OnPinsChangedAsync();
            Selected(vm, pins[1].Id, pins[3].Id);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Shift);
            Selected(vm, pins.Take(4).Select(c => c.Id).ToArray());
            await host.Categories.RenameAsync(pins[1].Id, "改名"); await vm.RefreshAfterTreeChangeAsync();
            Selected(vm, pins.Take(4).Select(c => c.Id).ToArray());
            Assert.Equal("改名", vm.Breadcrumb[^1].Name);
            Assert.Equal("改名", vm.PinnedCategories.Single(c => c.Id == pins[1].Id).Name);
            await host.Categories.UnpinAsync(pins[3].Id); await vm.OnPinsChangedAsync();
            await bar.HandlePinnedClickAsync(pins[4], ModifierKeys.Shift); Selected(vm, pins[4].Id);
        });
    }

    [Fact]
    public async Task BatchSqliteFailureRollsBackEveryPinAndRetainsSelection()
    {
        using var host = new TestHost(); var pins = await CreatePins(host);
        await using (var db = host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailBatch BEFORE UPDATE OF IsPinned ON Categories WHEN OLD.Name = '快捷2' BEGIN SELECT RAISE(ABORT, 'injected unpin failure'); END");
        await WpfTestHost.RunAsync(async () =>
        {
            var (main, bar) = await CreateBar(host); var vm = main.Navigation;
            await bar.HandlePinnedClickAsync(pins[0], ModifierKeys.None);
            await bar.HandlePinnedClickAsync(pins[2], ModifierKeys.Control);
            await Assert.ThrowsAsync<SqliteException>(() => vm.UnpinSelectedAsync(pins[0].Id));
            Selected(vm, pins[0].Id, pins[2].Id);
            Assert.Equal(pins.Select(c => c.Id), (await host.Categories.GetPinnedAsync()).Select(c => c.Id));
        });
    }
}
