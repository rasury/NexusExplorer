using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
namespace NexusExplorer.Views.Dialogs;
public static class InputDialog
{
    public static async Task<string?> ShowAsync(string title, string label, string? defaultValue, bool selectFileStem = false)
    {
        var box = new TextBox { Text = defaultValue ?? "", Margin = new Thickness(0, 8, 0, 8) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextBoxStandard"); HintAssist.SetHint(box, label);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        var cancel = DialogSurface.Action("取消", "ButtonText", null, () => view);
        var accept = new Button { Content = "确定", MinWidth = 80, Margin = new Thickness(8, 0, 0, 8), IsDefault = true };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary"); accept.Click += (_, _) => view.Complete(box.Text);
        footer.Children.Add(cancel); footer.Children.Add(accept);
        view = new DialogSurface(title, box, footer, 480);
        box.Loaded += (_, _) => box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            box.Focus();
            var stemLength = System.IO.Path.GetFileNameWithoutExtension(box.Text).Length;
            if (selectFileStem && stemLength > 0)
                box.Select(0, stemLength);
            else box.SelectAll();
        }));
        return await MaterialDialogService.ShowAsync(view) as string;
    }
}
