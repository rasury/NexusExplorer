using System.Windows;
using System.Windows.Controls;

namespace NexusExplorer.Views.Dialogs;

internal static class LocationPreviewDialog
{
    public static bool Confirm(IReadOnlyCollection<string> mapping)
    {
        var dialog = new Window
        {
            Title = "重新定位预览", Owner = System.Windows.Application.Current.MainWindow,
            Width = 800, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Style = (Style)System.Windows.Application.Current.Resources["DialogWindow"]
        };
        var panel = new DockPanel { Margin = new Thickness(20) };
        var description = new TextBlock { Text = $"仅更新位置，不搬文件。请核对全部 {mapping.Count} 项映射；显示失效的文件不会被猜测修复。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(description, Dock.Top); panel.Children.Add(description);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button { Content = "确认重新定位", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8), IsDefault = true };
        var cancel = new Button { Content = "取消", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8), IsCancel = true };
        accept.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(accept); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = string.Join("\n\n", mapping), TextWrapping = TextWrapping.Wrap } });
        dialog.Content = panel; DialogChrome.Apply(dialog); return dialog.ShowDialog() == true;
    }
}
