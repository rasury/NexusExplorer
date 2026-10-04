using System.Windows;
using System.Windows.Controls;

namespace NexusExplorer.Views.Dialogs;

/// <summary>确认与错误提示也使用可滚动正文，避免长文件清单遮住操作按钮。</summary>
internal static class MessageDialog
{
    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var app = System.Windows.Application.Current;
        var previousMain = app.MainWindow;
        var previousShutdown = app.ShutdownMode;
        Window? dialog = null;
        // Startup recovery may show a dialog before the application's main window exists.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            dialog = Create(message, title, buttons, image);
            dialog.ShowDialog();
            return (MessageBoxResult)dialog.Tag;
        }
        finally
        {
            if (dialog is not null && ReferenceEquals(app.MainWindow, dialog)) app.MainWindow = previousMain;
            app.ShutdownMode = previousShutdown;
        }
    }

    internal static Window Create(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new Window
        {
            Title = title, Width = 480, Owner = DialogChrome.Owner,
            Tag = buttons == MessageBoxButton.OK ? MessageBoxResult.OK :
                buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel
        };
        dialog.SetResourceReference(FrameworkElement.StyleProperty, "DialogWindow");
        var body = new StackPanel();
        if (image != MessageBoxImage.None)
        {
            var icon = new TextBlock { Text = image is MessageBoxImage.Error or MessageBoxImage.Warning ? "⚠" : image == MessageBoxImage.Question ? "?" : "ⓘ", FontSize = 24, Margin = new Thickness(0, 0, 0, 8) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "BrushSecondaryText");
            body.Children.Add(icon);
        }
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(FrameworkElement.StyleProperty, "TextPrimary");
        body.Children.Add(text);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string caption, MessageBoxResult result, bool primary, bool cancel = false)
        {
            var button = new Button { Content = caption, MinWidth = 80, Margin = new Thickness(8, 0, 0, 6), IsDefault = primary, IsCancel = cancel };
            button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "ButtonPrimary" : "ButtonGhost");
            button.Click += (_, _) => { dialog.Tag = result; dialog.Close(); };
            footer.Children.Add(button);
        }
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel)
        {
            Add("是", MessageBoxResult.Yes, true);
            Add("否", MessageBoxResult.No, false, buttons == MessageBoxButton.YesNo);
        }
        else Add("确定", MessageBoxResult.OK, true, buttons == MessageBoxButton.OK);
        if (buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel) Add("取消", MessageBoxResult.Cancel, false, true);
        DialogChrome.SetContent(dialog, body, footer); DialogChrome.Apply(dialog);
        return dialog;
    }
}
