using System.Windows;
using System.Windows.Controls;

namespace NexusExplorer.Views.Dialogs;

/// <summary>简单文字输入对话框。返回 null 表示取消。</summary>
public static class InputDialog
{
    public static string? Show(string title, string label, string? defaultValue)
    {
        string? result = null;

        var textBox = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            Style = (Style)System.Windows.Application.Current.Resources["TextBoxStandard"],
            Margin = new Thickness(0, 6, 0, 14)
        };

        var dialog = new Window
        {
            Title = title,
            Style = (Style)System.Windows.Application.Current.Resources["DialogWindow"],
            Width = 380,
            Owner = System.Windows.Application.Current.MainWindow
        };
        DialogChrome.Apply(dialog);

        var okButton = new Button
        {
            Content = "确定",
            Style = (Style)System.Windows.Application.Current.Resources["ButtonPrimary"],
            MinWidth = 80
        };
        okButton.Click += (_, _) =>
        {
            result = textBox.Text;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "取消",
            Style = (Style)System.Windows.Application.Current.Resources["ButtonGhost"],
            MinWidth = 80,
            Margin = new Thickness(8, 0, 0, 0)
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var dialogContent = new StackPanel { Margin = new Thickness(20) };
        dialogContent.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)System.Windows.Application.Current.Resources["TextSecondary"]
        });
        dialogContent.Children.Add(textBox);
        dialogContent.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { okButton, cancelButton }
        });

        dialog.Content = dialogContent;

        textBox.Focus();
        textBox.SelectAll();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                result = textBox.Text;
                dialog.Close();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                dialog.Close();
            }
        };

        dialog.ShowDialog();
        return result;
    }
}
