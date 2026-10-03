using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Services;

namespace NexusExplorer.Views.Dialogs;

/// <summary>整理时同名文件冲突对话框:替换/跳过/保留两个/取消。支持应用到全部。</summary>
public record ConflictDecision(ConflictResolution Resolution, bool ApplyToAll);
public static class ConflictDialog
{
    public static ConflictResolution Show(string fileName, string targetPath)
        => ShowDecision(fileName, targetPath).Resolution;
    public static ConflictDecision ShowDecision(string fileName, string targetPath)
    {
        ConflictResolution result = ConflictResolution.Ask;
        bool applyToAll = false;

        var dialog = new Window
        {
            Title = "文件冲突",
            Style = (Style)System.Windows.Application.Current.Resources["DialogWindow"],
            Width = 460,
            Owner = System.Windows.Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize
        };
        DialogChrome.Apply(dialog);

        var applyToAllCheckBox = CreateApplyToAllCheckBox();

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = $"目标目录已存在同名文件:",
            Style = (Style)System.Windows.Application.Current.Resources["TextHeader"],
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName,
            Style = (Style)System.Windows.Application.Current.Resources["TextPrimary"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = targetPath,
            Style = (Style)System.Windows.Application.Current.Resources["TextCaption"],
            Margin = new Thickness(0, 4, 0, 16),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock { Text = "跳过：使用目标目录已有文件，保留外部源文件；若目标已有分类记录则提示冲突。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        void AddButton(string text, ConflictResolution resolution, string styleKey, int marginLeft = 8)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)System.Windows.Application.Current.Resources[styleKey],
                MinWidth = 88,
                Margin = new Thickness(marginLeft, 0, 0, 0)
            };
            button.Click += (_, _) =>
            {
                result = resolution;
                applyToAll = applyToAllCheckBox.IsChecked == true;
                dialog.Close();
            };
            buttons.Children.Add(button);
        }

        AddButton("替换", ConflictResolution.Replace, "ButtonPrimary", 0);
        AddButton("保留两个", ConflictResolution.KeepBoth, "ButtonSecondary");
        AddButton("跳过", ConflictResolution.Skip, "ButtonGhost");
        AddButton("取消整理", ConflictResolution.Ask, "ButtonGhost");

        panel.Children.Add(applyToAllCheckBox);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        dialog.ShowDialog();

        // 取消整理:返回特殊标记(Ask 表示中止)
        return new ConflictDecision(result, applyToAll);
    }
    internal static CheckBox CreateApplyToAllCheckBox()
    {
        var checkBox = new CheckBox { Content = "应用到全部后续冲突", Margin = new Thickness(0, 12, 0, 0) };
        checkBox.SetResourceReference(Control.ForegroundProperty, "BrushSecondaryText");
        return checkBox;
    }
}
