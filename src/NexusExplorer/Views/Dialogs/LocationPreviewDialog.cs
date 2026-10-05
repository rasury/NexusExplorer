using System.Windows;
using System.Windows.Controls;
namespace NexusExplorer.Views.Dialogs;
internal static class LocationPreviewDialog
{
    public static async Task<bool> ConfirmAsync(IReadOnlyCollection<string> mapping) => await MaterialDialogService.ShowAsync(Create(mapping)) is true;
    internal static DialogSurface Create(IReadOnlyCollection<string> mapping)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"请核对全部 {mapping.Count} 项映射；显示失效的文件不会被猜测修复。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        body.Children.Add(new TextBlock { Text = string.Join("\n\n", mapping), TextWrapping = TextWrapping.Wrap });
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        footer.Children.Add(DialogSurface.Action("取消", "ButtonText", false, () => view));
        footer.Children.Add(DialogSurface.Action("确认重新定位", "ButtonPrimary", true, () => view));
        view = new DialogSurface("重新定位预览", body, footer, 720); return view;
    }
}
