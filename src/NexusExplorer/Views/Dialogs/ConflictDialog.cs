using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Services;
namespace NexusExplorer.Views.Dialogs;
public record ConflictDecision(ConflictResolution Resolution, bool ApplyToAll);
public static class ConflictDialog
{
    public static async Task<ConflictDecision> ShowDecisionAsync(string fileName, string targetPath)
        => await MaterialDialogService.ShowAsync(Create(fileName, targetPath)) as ConflictDecision ?? new(ConflictResolution.Ask, false);
    internal static DialogSurface Create(string fileName, string targetPath)
    {
        var body = new StackPanel();
        void Text(string text, string style) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) }; block.SetResourceReference(FrameworkElement.StyleProperty, style); body.Children.Add(block); }
        Text("目标目录已存在同名文件:", "TextPrimary"); Text(fileName, "TextHeader"); Text(targetPath, "TextCaption");
        Text("跳过：使用目标目录已有文件，保留外部源文件；若目标已有分类记录则提示冲突。", "TextSecondary");
        var all = CreateApplyToAllCheckBox(); body.Children.Add(all);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        void Add(string label, ConflictResolution resolution, string style)
        { var button = new Button { Content = label, MinWidth = 80, Margin = new Thickness(8, 0, 0, 8) }; button.SetResourceReference(FrameworkElement.StyleProperty, style); button.Click += (_, _) => view.Complete(new ConflictDecision(resolution, all.IsChecked == true)); actions.Children.Add(button); }
        Add("替换", ConflictResolution.Replace, "ButtonGhost"); Add("保留两个", ConflictResolution.KeepBoth, "ButtonSecondary");
        Add("跳过", ConflictResolution.Skip, "ButtonText"); Add("取消整理", ConflictResolution.Ask, "ButtonText");
        view = new DialogSurface("文件冲突", body, actions); return view;
    }
    internal static CheckBox CreateApplyToAllCheckBox() => new() { Content = "应用到全部后续冲突", Margin = new Thickness(0, 8, 0, 0) };
}
