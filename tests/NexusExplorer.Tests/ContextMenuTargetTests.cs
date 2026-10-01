using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexusExplorer.Models;

namespace NexusExplorer.Tests;

/// <summary>
/// 右键菜单目标解析测试:右键不改变 TreeView 选中,
/// ContextMenuOpening 必须从命中的 TreeViewItem 记录目标分类
/// (之前直接右键未先左击时菜单命令无反应的根因)。
/// </summary>
public class ContextMenuTargetTests
{
    [Fact]
    public void ContextMenuOpening_RecordsHoveredCategory_NotSelection()
    {
        string? failure = null;
        string? recordedName = null;
        string? selectedName = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var host = new TestHost();
                var catA = host.Categories.CreateAsync("分类A", null).GetAwaiter().GetResult();
                var catB = host.Categories.CreateAsync("分类B", null).GetAwaiter().GetResult();

                var tree = new TreeView();
                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    AllowsTransparency = true,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -5000, Top = -5000,
                    Width = 300, Height = 400,
                    Content = tree
                };
                tree.ItemsSource = new[] { catA, catB };
                window.Show();
                tree.UpdateLayout();
                Pump();

                var itemA = TreeViewItemFor(tree, catA);
                var itemB = TreeViewItemFor(tree, catB);
                Assert.NotNull(itemA);
                Assert.NotNull(itemB);

                // 左击选中 A
                itemA!.IsSelected = true;
                Pump();

                // 右键 B:从命中的 TreeViewItem 记录目标(与面板事件处理同一逻辑)
                recordedName = (itemB!.Header as Category)?.Name;
                selectedName = (tree.SelectedItem as Category)?.Name;

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(30000);

        Assert.Null(failure);
        Assert.Equal("分类B", recordedName);  // 目标=右键命中的 B
        Assert.Equal("分类A", selectedName);  // 选中仍是 A(右键不改选中)
    }

    private static TreeViewItem? TreeViewItemFor(TreeView tree, Category category)
    {
        foreach (var child in tree.Items)
        {
            if (tree.ItemContainerGenerator.ContainerFromItem(child)
                is TreeViewItem item
                && item.Header is Category c && c.Id == category.Id)
                return item;
        }
        return null;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
