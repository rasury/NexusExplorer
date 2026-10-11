using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Views.Dialogs;

internal static class PdfPasswordDialog
{
    internal static async Task<string?> ShowAsync()
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 8), MinHeight = 56 };
        box.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignOutlinedPasswordBox"); HintAssist.SetHint(box, "PDF 密码");
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        actions.Children.Add(DialogSurface.Action("取消", "ButtonText", null, () => view));
        var accept = new Button { Content = "确定", MinWidth = 80, Margin = new Thickness(8, 0, 0, 8), IsDefault = true };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        accept.Click += (_, _) => view.Complete(box.Password); actions.Children.Add(accept);
        view = new DialogSurface("打开加密 PDF", box, actions, 480) { RequiresInputWindow = true };
        box.Loaded += (_, _) => box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => box.Focus()));
        try { return await MaterialDialogService.ShowAsync(view) as string; }
        finally { box.Clear(); }
    }
}
